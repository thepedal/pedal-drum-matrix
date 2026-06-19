using System;

namespace PedalDrumMatrix
{
    // One rack slot. Owns a pre-built instance of every FxType (no audio-thread
    // allocation on swap). Type changes run a short equal-power crossfade so
    // they're click-free. Amount and Char are smoothed; Mode is a plain switch.
    public sealed class Slot
    {
        readonly IDrumFx[] _fx;
        FxType _active = FxType.None;
        FxType _previous = FxType.None;

        float _amount, _amountTarget;
        float _p1, _p1Target;
        bool  _mode;
        float _smoothCoef;

        int _xfadeRemain, _xfadeLen = 1;

        public Slot()
        {
            _fx = new IDrumFx[Enum.GetValues(typeof(FxType)).Length];
            for (int i = 0; i < _fx.Length; i++) _fx[i] = FxFactory.Create((FxType)i);
        }

        public void Prepare(float sampleRate, float samplesPerTick)
        {
            foreach (var f in _fx) f.Prepare(sampleRate, samplesPerTick);
            _smoothCoef = (float)Math.Exp(-1.0 / (0.020 * sampleRate)); // 20 ms
            _xfadeLen   = Math.Max(1, (int)(0.010f * sampleRate));      // 10 ms swap
        }

        // Called once per Work block, before the sample loop.
        public void SetParams(int typeRaw, int amountRaw, int charRaw, bool mode)
        {
            _amountTarget = amountRaw / 127f;
            _p1Target     = charRaw   / 127f;
            _mode         = mode;

            var t = (FxType)typeRaw;
            if (t != _active)
            {
                _previous    = _active;
                _active      = t;
                _xfadeRemain = _xfadeLen;
                _fx[(int)_active].Reset();
            }
        }

        public void Process(ref float l, ref float r)
        {
            _amount = _amountTarget + (_amount - _amountTarget) * _smoothCoef;
            _p1     = _p1Target     + (_p1     - _p1Target)     * _smoothCoef;

            if (_xfadeRemain > 0)
            {
                float t  = 1f - (_xfadeRemain / (float)_xfadeLen);
                float gN = MathF.Sin(t * (MathF.PI * 0.5f));
                float gO = MathF.Cos(t * (MathF.PI * 0.5f));

                float oL = l, oR = r, nL = l, nR = r;
                _fx[(int)_previous].Process(ref oL, ref oR, _amount, _p1, _mode);
                _fx[(int)_active  ].Process(ref nL, ref nR, _amount, _p1, _mode);

                l = oL * gO + nL * gN;
                r = oR * gO + nR * gN;

                _xfadeRemain--;
                if (_xfadeRemain == 0) _fx[(int)_previous].Reset();
            }
            else
            {
                _fx[(int)_active].Process(ref l, ref r, _amount, _p1, _mode);
            }
        }

        public bool IsRinging =>
            _xfadeRemain > 0
                ? (_fx[(int)_active].IsRinging || _fx[(int)_previous].IsRinging)
                : _fx[(int)_active].IsRinging;
    }
}
