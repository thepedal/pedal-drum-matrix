using System;

namespace PedalDrumMatrix
{
    // Fixed-order palette. ORDER IS A PRESET CONTRACT (Build §3.3): append only.
    public enum FxType
    {
        None = 0, Bitcrush, Drive, Filter, RingMod, Comb, Stutter, Delay, Reverb, Gate
    }

    // One effect occupying a slot. Stereo, per-sample.
    //   amount = smoothed 0..1 slot macro (amount 0 = clean pass-through)
    //   p1     = smoothed 0..1 "Char" rotary (effect-specific character)
    //   sw     = "Mode" switch (effect-specific toggle)
    public interface IDrumFx
    {
        void Prepare(float sampleRate, float samplesPerTick);
        void Reset();
        void Process(ref float l, ref float r, float amount, float p1, bool sw);
        bool IsRinging { get; }
    }

    public static class FxFactory
    {
        public static IDrumFx Create(FxType t) => t switch
        {
            FxType.Bitcrush => new BitcrushFx(),
            FxType.Drive    => new DriveFx(),
            FxType.Filter   => new FilterFx(),
            FxType.RingMod  => new RingModFx(),
            FxType.Comb     => new CombFx(),
            FxType.Stutter  => new StutterFx(),
            FxType.Delay    => new DelayFx(),
            FxType.Reverb   => new ReverbFx(),
            FxType.Gate     => new GateFx(),
            _ => new NoneFx()
        };
    }

    // Shared decaying-energy tail tracker for feedback effects.
    internal struct Tail
    {
        float _level, _decay;
        public void Prepare(float sr) => _decay = (float)Math.Exp(-1.0 / (0.060 * sr));
        public void Reset() => _level = 0f;
        public void Feed(float a, float b)
        {
            float m = MathF.Abs(a) + MathF.Abs(b);
            _level = MathF.Max(_level * _decay, m);
        }
        public bool Ringing => _level > 1e-4f;
    }

    internal static class Dsp
    {
        // Flush-to-zero: keeps decaying feedback states out of the subnormal
        // range, where some CPUs slow down ~10× and cause dropout spikes.
        public static float Ftz(float v) => (v > -1e-15f && v < 1e-15f) ? 0f : v;
    }

    // Zero-latency stereo-linked peak limiter + cubic soft-clip ceiling.
    // Fast attack / slow release on the gain; the soft-clip is the true
    // brickwall for transients the follower lets slip. ~10 flops/sample.
    public sealed class OutputLimiter
    {
        const float Ceiling = 0.95f;
        float _gain = 1f, _atk, _rel;
        public void Prepare(float sr)
        {
            sr = sr > 0 ? sr : 44100f;
            _atk = (float)Math.Exp(-1.0 / (0.001 * sr));   // 1 ms
            _rel = (float)Math.Exp(-1.0 / (0.100 * sr));   // 100 ms
        }
        public void Reset() => _gain = 1f;
        static float SoftClip(float x)
        {
            if (x < -1.5f) return -1f;
            if (x >  1.5f) return  1f;
            return x - (x * x * x) * (1f / 6.75f);          // ±1.5 → ±1.0, smooth knee
        }
        public void Process(ref float l, ref float r)
        {
            float peak = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            float target = peak > Ceiling ? Ceiling / peak : 1f;
            float coef = target < _gain ? _atk : _rel;       // attack fast, release slow
            _gain = target + (_gain - target) * coef;
            l = SoftClip(l * _gain);
            r = SoftClip(r * _gain);
        }
    }

    // Auto-gain leveler (peak-targeting). Sets gain so the output peak sits near
    // a target just under the limiter ceiling — so quiet effect settings get
    // brought up to a consistent level, while drum transients keep their punch
    // (gain is set by peak, not RMS, so peaks aren't crushed into the limiter).
    // Freezes on gaps/decays below a noise floor so silence is never amplified.
    public sealed class AutoGain
    {
        const float TargetPk = 0.5f;     // ~ -6 dBFS peak target (under the 0.95 limiter)
        const float PkFloor  = 0.012f;   // ~ -38 dBFS: below this (fast env), freeze gain
        const float MaxGain  = 8f;       // +18 dB ceiling on boost
        const float MinGain  = 0.25f;    // -12 dB floor on cut
        float _fast, _slow, _gain = 1f, _fastRel, _slowRel, _rise, _fall;
        public void Prepare(float sr)
        {
            sr = sr > 0 ? sr : 44100f;
            _fastRel = (float)Math.Exp(-1.0 / (0.030 * sr));  // 30 ms: "is there sound now"
            _slowRel = (float)Math.Exp(-1.0 / (0.300 * sr));  // 300 ms: level estimate
            _rise    = (float)Math.Exp(-1.0 / (0.700 * sr));  // gain up: 700 ms (slow → no pumping)
            _fall    = (float)Math.Exp(-1.0 / (0.120 * sr));  // gain down: 120 ms
        }
        public void Reset() { _fast = _slow = 0f; _gain = 1f; }
        public void Process(ref float l, ref float r)
        {
            float a = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            _fast = a > _fast ? a : a + (_fast - a) * _fastRel;   // gate detector
            _slow = a > _slow ? a : a + (_slow - a) * _slowRel;   // level detector

            float desired = TargetPk / MathF.Max(_slow, 1e-6f);
            if (desired > MaxGain) desired = MaxGain;
            else if (desired < MinGain) desired = MinGain;

            // Freeze gain in gaps/decays (fast env below floor) so silence and
            // tails are never boosted and the gain can't ratchet up between hits.
            if (_fast < PkFloor) desired = _gain;

            float coef = desired > _gain ? _rise : _fall;        // up slow, down faster
            _gain = desired + (_gain - desired) * coef;
            l *= _gain; r *= _gain;
        }
    }

    // ── None ────────────────────────────────────────────────────────────────
    public sealed class NoneFx : IDrumFx
    {
        public void Prepare(float sr, float spt) { }
        public void Reset() { }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw) { }
        public bool IsRinging => false;
    }

    // ── Bitcrush ─ char: bits↔rate tilt · mode: anti-alias filter ───────────
    public sealed class BitcrushFx : IDrumFx
    {
        float _holdL, _holdR, _phase, _lpL, _lpR;
        public void Prepare(float sr, float spt) { Reset(); }
        public void Reset() { _holdL = _holdR = 0f; _phase = 1f; _lpL = _lpR = 0f; }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            if (amount <= 0f) return;
            float rateAmt = amount * (0.5f + 0.5f * p1);
            float bitAmt  = amount * (0.5f + 0.5f * (1f - p1));
            float step = 1f - rateAmt * 0.975f; if (step < 0.025f) step = 0.025f;
            _phase += step;
            if (_phase >= 1f) { _phase -= 1f; _holdL = l; _holdR = r; }
            float levels = MathF.Pow(2f, 16f - bitAmt * 13f);
            float ql = MathF.Round(_holdL * levels) / levels;
            float qr = MathF.Round(_holdR * levels) / levels;
            if (sw)
            {
                const float a = 0.5f;            // gentle post lowpass
                _lpL += a * (ql - _lpL); _lpR += a * (qr - _lpR);
                ql = _lpL; qr = _lpR;
            }
            l = ql; r = qr;
        }
        public bool IsRinging => false;
    }

    // ── Drive ─ char: bias/asymmetry · mode: hard clip vs soft ──────────────
    public sealed class DriveFx : IDrumFx
    {
        public void Prepare(float sr, float spt) { }
        public void Reset() { }
        static float Clip(float x) => x < -1f ? -1f : (x > 1f ? 1f : x);
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            if (amount <= 0f) return;
            float pre = 1f + amount * 23f;
            float makeup = 1f / MathF.Sqrt(pre);
            float bias = (p1 - 0.5f) * 0.8f;
            float dc = sw ? Clip(pre * bias) : MathF.Tanh(pre * bias);
            float yl = sw ? Clip(pre * (l + bias)) : MathF.Tanh(pre * (l + bias));
            float yr = sw ? Clip(pre * (r + bias)) : MathF.Tanh(pre * (r + bias));
            l = (yl - dc) * makeup; r = (yr - dc) * makeup;
        }
        public bool IsRinging => false;
    }

    // ── Filter ─ char: resonance · mode: LP vs HP. amount sweeps cutoff ─────
    public sealed class FilterFx : IDrumFx
    {
        float _sr = 44100f, _ic1L, _ic2L, _ic1R, _ic2R;
        public void Prepare(float sr, float spt) { _sr = sr > 0 ? sr : 44100f; Reset(); }
        public void Reset() { _ic1L = _ic2L = _ic1R = _ic2R = 0f; }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            if (amount <= 0f) return;
            float fc = 18000f * MathF.Pow(150f / 18000f, amount);
            float q  = 0.5f * MathF.Pow(16f, p1);          // Q 0.5 → 8
            float g  = MathF.Tan(MathF.PI * fc / _sr);
            float k  = 1f / q;
            float a1 = 1f / (1f + g * (g + k)), a2 = g * a1, a3 = g * a2;

            float v0 = l, v3 = v0 - _ic2L;
            float v1 = a1 * _ic1L + a2 * v3;
            float v2 = _ic2L + a2 * _ic1L + a3 * v3;
            _ic1L = 2f * v1 - _ic1L; _ic2L = 2f * v2 - _ic2L;
            l = sw ? (v0 - k * v1 - v2) : v2;

            v0 = r; v3 = v0 - _ic2R;
            v1 = a1 * _ic1R + a2 * v3;
            v2 = _ic2R + a2 * _ic1R + a3 * v3;
            _ic1R = 2f * v1 - _ic1R; _ic2R = 2f * v2 - _ic2R;
            r = sw ? (v0 - k * v1 - v2) : v2;
        }
        public bool IsRinging => false;
    }

    // ── RingMod ─ char: carrier fine tune · mode: RM vs AM ──────────────────
    public sealed class RingModFx : IDrumFx
    {
        float _sr = 44100f, _phase;
        public void Prepare(float sr, float spt) { _sr = sr > 0 ? sr : 44100f; Reset(); }
        public void Reset() { _phase = 0f; }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            if (amount <= 0f) return;
            float f = 30f * MathF.Pow(100f, amount) * MathF.Pow(2f, (p1 - 0.5f) * 2f);
            _phase += 2f * MathF.PI * f / _sr;
            if (_phase > 2f * MathF.PI) _phase -= 2f * MathF.PI;
            float c = MathF.Sin(_phase);
            float carrier = sw ? (0.5f + 0.5f * c) : c;     // AM (unipolar) vs RM
            float w = amount;
            l = (1f - w) * l + w * (l * carrier);
            r = (1f - w) * r + w * (r * carrier);
        }
        public bool IsRinging => false;
    }

    // ── Comb ─ char: feedback damping · mode: +/- feedback sign (rings) ─────
    public sealed class CombFx : IDrumFx
    {
        float[] _bL, _bR; int _w, _n;
        float _sr = 44100f, _dL, _dR; Tail _tail;
        public void Prepare(float sr, float spt)
        {
            _sr = sr > 0 ? sr : 44100f;
            _n = Math.Max(8, (int)(0.025f * _sr));
            _bL = new float[_n]; _bR = new float[_n];
            _tail.Prepare(_sr); Reset();
        }
        public void Reset() { Array.Clear(_bL,0,_n); Array.Clear(_bR,0,_n); _w=0; _dL=_dR=0f; _tail.Reset(); }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            if (amount <= 0f) { _tail.Reset(); return; }
            int d = Math.Max(1, (int)(_n * (1f - amount * 0.92f)));
            float fb = (0.5f + amount * 0.45f) * (sw ? -1f : 1f);
            float damp = p1 * 0.7f;
            int rp = _w - d; if (rp < 0) rp += _n;

            _dL = Dsp.Ftz(_bL[rp] * (1f - damp) + _dL * damp);
            _dR = Dsp.Ftz(_bR[rp] * (1f - damp) + _dR * damp);
            float yL = l + fb * _dL, yR = r + fb * _dR;
            _bL[_w] = Dsp.Ftz(yL); _bR[_w] = Dsp.Ftz(yR);
            _w++; if (_w >= _n) _w = 0;

            float w = amount;
            l = (1f - w) * l + w * yL; r = (1f - w) * r + w * yR;
            _tail.Feed(l, r);
        }
        public bool IsRinging => _tail.Ringing;
    }

    // ── Stutter ─ char: repeats (2-8) · mode: reverse slice (rings) ─────────
    public sealed class StutterFx : IDrumFx
    {
        float[] _ringL, _ringR, _sliceL, _sliceR;
        int _n, _w, _slice, _hold, _play, _counter, _reps = 4; bool _rev;
        float _sr = 44100f, _spt = 11025f, _holdAmount; Tail _tail;
        public void Prepare(float sr, float spt)
        {
            _sr = sr > 0 ? sr : 44100f;
            _spt = spt > 1f ? spt : _sr / 8f;
            _n = Math.Max(8, (int)(0.75f * _sr));
            _ringL = new float[_n]; _ringR = new float[_n];
            int maxSlice = Math.Max(64, (int)_spt);
            _sliceL = new float[maxSlice]; _sliceR = new float[maxSlice];
            _tail.Prepare(_sr); Reset();
        }
        public void Reset()
        {
            Array.Clear(_ringL,0,_n); Array.Clear(_ringR,0,_n);
            _w=0; _slice=0; _hold=0; _play=0; _counter=0; _tail.Reset();
        }
        void Latch()
        {
            _slice = Math.Min(_sliceL.Length, Math.Max(64, (int)(_spt))); // ~1 tick
            int start = _w - _slice; if (start < 0) start += _n;
            for (int i = 0; i < _slice; i++)
            {
                int idx = start + i; if (idx >= _n) idx -= _n;
                _sliceL[i] = _ringL[idx]; _sliceR[i] = _ringR[idx];
            }
            _hold = _slice * Math.Max(1, _reps); _play = 0; _counter = 0;
        }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            _holdAmount = amount; _reps = 2 + (int)MathF.Round(p1 * 6f); _rev = sw;
            _ringL[_w] = l; _ringR[_w] = r; _w++; if (_w >= _n) _w = 0;
            if (amount <= 0f) { _tail.Reset(); return; }
            if (_hold <= 0 || _slice <= 0) Latch();

            int pi = _rev ? (_slice - 1 - _play) : _play;
            if (pi < 0) pi = 0;
            float sl = _sliceL[pi], sr2 = _sliceR[pi];
            _play++; if (_play >= _slice) _play = 0;
            _counter++; if (_counter >= _hold) Latch();

            l = (1f - amount) * l + amount * sl;
            r = (1f - amount) * r + amount * sr2;
            _tail.Feed(amount * sl, amount * sr2);
        }
        public bool IsRinging => _tail.Ringing;
    }

    // ── Delay ─ char: feedback · mode: ping-pong (rings). amount = mix ──────
    public sealed class DelayFx : IDrumFx
    {
        float[] _bL, _bR; int _w, _n, _d;
        float _sr = 44100f, _spt = 11025f; Tail _tail;
        public void Prepare(float sr, float spt)
        {
            _sr = sr > 0 ? sr : 44100f;
            _spt = spt > 1f ? spt : _sr / 8f;
            _n = Math.Max(8, (int)(2.0f * _sr));
            _bL = new float[_n]; _bR = new float[_n];
            _tail.Prepare(_sr);
            _d = Math.Min(_n - 1, Math.Max(1, (int)(6f * _spt)));
            Reset();
        }
        public void Reset() { Array.Clear(_bL,0,_n); Array.Clear(_bR,0,_n); _w=0; _tail.Reset(); }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            if (amount <= 0f) { _tail.Reset(); return; }
            float fb = p1 * 0.95f, wet = amount;
            int rp = _w - _d; if (rp < 0) rp += _n;
            float dl = _bL[rp], dr = _bR[rp];
            if (sw) { _bL[_w] = Dsp.Ftz(l + fb * dr); _bR[_w] = Dsp.Ftz(r + fb * dl); }   // ping-pong
            else    { _bL[_w] = Dsp.Ftz(l + fb * dl); _bR[_w] = Dsp.Ftz(r + fb * dr); }
            _w++; if (_w >= _n) _w = 0;
            l = l + wet * dl; r = r + wet * dr;
            _tail.Feed(wet * dl, wet * dr);
        }
        public bool IsRinging => _tail.Ringing;
    }

    // ── Reverb ─ char: damping · mode: bright tilt (rings) ──────────────────
    public sealed class ReverbFx : IDrumFx
    {
        static readonly int[] CombTune = { 1116,1188,1277,1356,1422,1491,1557,1617 };
        static readonly int[] ApTune   = { 556,441,341,225 };
        const int Spread = 23; const float FixedGain = 0.015f, ApFb = 0.5f;
        float[][] _cbL, _cbR, _apL, _apR;
        int[] _cbiL, _cbiR, _apiL, _apiR;
        float[] _cfL, _cfR;
        float _sr = 44100f, _feedback = 0.84f, _damp = 0.25f;
        float _hpL, _hpR, _hxL, _hxR; Tail _tail;

        static float[][] MakeBufs(int[] tune, float scale, int spread)
        {
            var a = new float[tune.Length][];
            for (int i = 0; i < tune.Length; i++)
                a[i] = new float[Math.Max(1, (int)((tune[i] + spread) * scale))];
            return a;
        }
        public void Prepare(float sr, float spt)
        {
            _sr = sr > 0 ? sr : 44100f; float s = _sr / 44100f;
            _cbL = MakeBufs(CombTune,s,0); _cbR = MakeBufs(CombTune,s,Spread);
            _apL = MakeBufs(ApTune,s,0);   _apR = MakeBufs(ApTune,s,Spread);
            _cbiL = new int[CombTune.Length]; _cbiR = new int[CombTune.Length];
            _apiL = new int[ApTune.Length];   _apiR = new int[ApTune.Length];
            _cfL = new float[CombTune.Length]; _cfR = new float[CombTune.Length];
            _tail.Prepare(_sr); Reset();
        }
        public void Reset()
        {
            foreach (var b in _cbL) Array.Clear(b,0,b.Length);
            foreach (var b in _cbR) Array.Clear(b,0,b.Length);
            foreach (var b in _apL) Array.Clear(b,0,b.Length);
            foreach (var b in _apR) Array.Clear(b,0,b.Length);
            Array.Clear(_cbiL,0,_cbiL.Length); Array.Clear(_cbiR,0,_cbiR.Length);
            Array.Clear(_apiL,0,_apiL.Length); Array.Clear(_apiR,0,_apiR.Length);
            Array.Clear(_cfL,0,_cfL.Length);   Array.Clear(_cfR,0,_cfR.Length);
            _hpL=_hpR=_hxL=_hxR=0f; _tail.Reset();
        }
        float Comb(float[] buf, ref int idx, ref float store, float input)
        {
            float o = buf[idx];
            store = Dsp.Ftz(o * (1f - _damp) + store * _damp);
            buf[idx] = Dsp.Ftz(input + store * _feedback);
            idx++; if (idx >= buf.Length) idx = 0;
            return o;
        }
        float Allpass(float[] buf, ref int idx, float input)
        {
            float bo = buf[idx];
            float o = -input + bo;
            buf[idx] = input + bo * ApFb;
            idx++; if (idx >= buf.Length) idx = 0;
            return o;
        }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            if (amount <= 0f) { _tail.Reset(); return; }
            _feedback = 0.70f + amount * 0.28f;
            _damp = p1 * 0.6f;
            float input = (l + r) * FixedGain;
            float wl = 0f, wr = 0f;
            for (int i = 0; i < CombTune.Length; i++)
            {
                wl += Comb(_cbL[i], ref _cbiL[i], ref _cfL[i], input);
                wr += Comb(_cbR[i], ref _cbiR[i], ref _cfR[i], input);
            }
            for (int i = 0; i < ApTune.Length; i++)
            {
                wl = Allpass(_apL[i], ref _apiL[i], wl);
                wr = Allpass(_apR[i], ref _apiR[i], wr);
            }
            if (sw)   // bright tilt: one-pole highpass on the wet signal
            {
                const float a = 0.85f;
                float yl = a * (_hpL + wl - _hxL); _hxL = wl; _hpL = yl; wl = yl;
                float yr = a * (_hpR + wr - _hxR); _hxR = wr; _hpR = yr; wr = yr;
            }
            l = l + amount * wl; r = r + amount * wr;
            _tail.Feed(amount * wl, amount * wr);
        }
        public bool IsRinging => _tail.Ringing;
    }

    // ── Gate ─ char: duty cycle · mode: triplet timing (tail-free) ──────────
    public sealed class GateFx : IDrumFx
    {
        float _sr = 44100f, _spt = 11025f, _pos, _env, _coef;
        public void Prepare(float sr, float spt)
        {
            _sr = sr > 0 ? sr : 44100f;
            _spt = spt > 1f ? spt : _sr / 8f;
            _coef = (float)Math.Exp(-1.0 / (0.003 * _sr));
            Reset();
        }
        public void Reset() { _pos = 0f; _env = 1f; }
        public void Process(ref float l, ref float r, float amount, float p1, bool sw)
        {
            if (amount <= 0f) { _env = 1f; return; }
            float cyc = 8f - amount * 7f; if (sw) cyc *= 2f / 3f;     // triplet
            float period = cyc * _spt; if (period < 2f) period = 2f;
            float duty = 0.05f + p1 * 0.9f;
            _pos += 1f; if (_pos >= period) _pos -= period;
            float target = (_pos < period * duty) ? 1f : 0f;
            _env = target + (_env - target) * _coef;
            float g = 1f - amount * (1f - _env);
            l *= g; r *= g;
        }
        public bool IsRinging => false;
    }
}
