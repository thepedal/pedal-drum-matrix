using System;
using Buzz.MachineInterface;   // IBuzzMachine, IBuzzMachineHost, MachineDecl, ParameterDecl, Sample, WorkModes
using BuzzGUI.Interfaces;      // IMachine, IBuzz

namespace PedalDrumMatrix
{
    [MachineDecl(Name = "Pedal Drum Matrix", ShortName = "DrumMtx",
                 Author = "thepedal", MaxTracks = 0)]
    public class PedalDrumMatrixMachine : IBuzzMachine
    {
        const int SLOTS = 6;

        readonly IBuzzMachineHost host;
        readonly Slot[] _slots = new Slot[SLOTS];
        readonly OutputLimiter _limiter = new OutputLimiter();
        readonly AutoGain _autoGain = new AutoGain();
        float _sampleRate = 0f;

        public PedalDrumMatrixMachine(IBuzzMachineHost host)
        {
            this.host = host;
            for (int i = 0; i < SLOTS; i++) _slots[i] = new Slot();
        }

        public IMachine Machine { get; set; }

        // ── Parameters ───────────────────────────────────────────────────────
        // Declaration ORDER is the preset contract (Build §3.3) — append only.
        // Per slot: a Type selector (enum, click-free swappable, peer/MIDI
        // mappable like any param) followed by its Amount macro (the six
        // mappable performance controls). The ValueDescriptions literal must be
        // inline per attribute (a static-field reference won't compile) and its
        // order must match the FxType enum.

        [ParameterDecl(DefValue = 0, ValueDescriptions = new[] {
            "None","Bitcrush","Drive","Filter","RingMod","Comb","Stutter","Delay","Reverb","Gate" },
            Description = "Slot 1 — effect type")]
        public int Slot1Type { get; set; } = 0;
        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 0, Description = "Slot 1 — amount")]
        public int Slot1Amount { get; set; } = 0;

        [ParameterDecl(DefValue = 0, ValueDescriptions = new[] {
            "None","Bitcrush","Drive","Filter","RingMod","Comb","Stutter","Delay","Reverb","Gate" },
            Description = "Slot 2 — effect type")]
        public int Slot2Type { get; set; } = 0;
        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 0, Description = "Slot 2 — amount")]
        public int Slot2Amount { get; set; } = 0;

        [ParameterDecl(DefValue = 0, ValueDescriptions = new[] {
            "None","Bitcrush","Drive","Filter","RingMod","Comb","Stutter","Delay","Reverb","Gate" },
            Description = "Slot 3 — effect type")]
        public int Slot3Type { get; set; } = 0;
        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 0, Description = "Slot 3 — amount")]
        public int Slot3Amount { get; set; } = 0;

        [ParameterDecl(DefValue = 0, ValueDescriptions = new[] {
            "None","Bitcrush","Drive","Filter","RingMod","Comb","Stutter","Delay","Reverb","Gate" },
            Description = "Slot 4 — effect type")]
        public int Slot4Type { get; set; } = 0;
        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 0, Description = "Slot 4 — amount")]
        public int Slot4Amount { get; set; } = 0;

        [ParameterDecl(DefValue = 0, ValueDescriptions = new[] {
            "None","Bitcrush","Drive","Filter","RingMod","Comb","Stutter","Delay","Reverb","Gate" },
            Description = "Slot 5 — effect type")]
        public int Slot5Type { get; set; } = 0;
        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 0, Description = "Slot 5 — amount")]
        public int Slot5Amount { get; set; } = 0;

        [ParameterDecl(DefValue = 0, ValueDescriptions = new[] {
            "None","Bitcrush","Drive","Filter","RingMod","Comb","Stutter","Delay","Reverb","Gate" },
            Description = "Slot 6 — effect type")]
        public int Slot6Type { get; set; } = 0;
        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 0, Description = "Slot 6 — amount")]
        public int Slot6Amount { get; set; } = 0;

        // ── Character controls (appended — Build §3.3) ───────────────────────
        // Per slot: Char (continuous rotary, effect-specific) + Mode (switch).
        // Maps to a BCR2000 dual-function encoder: rotation → Char, push → Mode.
        // Appended after the original 12 params so existing songs keep their
        // Type/Amount values; they group at the bottom of the rack.

        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 64, Description = "Slot 1 — char")]
        public int Slot1Char { get; set; } = 64;
        [ParameterDecl(MinValue = 0, MaxValue = 1, DefValue = 0, ValueDescriptions = new[] { "Off", "On" }, Description = "Slot 1 — mode")]
        public int Slot1Mode { get; set; } = 0;

        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 64, Description = "Slot 2 — char")]
        public int Slot2Char { get; set; } = 64;
        [ParameterDecl(MinValue = 0, MaxValue = 1, DefValue = 0, ValueDescriptions = new[] { "Off", "On" }, Description = "Slot 2 — mode")]
        public int Slot2Mode { get; set; } = 0;

        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 64, Description = "Slot 3 — char")]
        public int Slot3Char { get; set; } = 64;
        [ParameterDecl(MinValue = 0, MaxValue = 1, DefValue = 0, ValueDescriptions = new[] { "Off", "On" }, Description = "Slot 3 — mode")]
        public int Slot3Mode { get; set; } = 0;

        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 64, Description = "Slot 4 — char")]
        public int Slot4Char { get; set; } = 64;
        [ParameterDecl(MinValue = 0, MaxValue = 1, DefValue = 0, ValueDescriptions = new[] { "Off", "On" }, Description = "Slot 4 — mode")]
        public int Slot4Mode { get; set; } = 0;

        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 64, Description = "Slot 5 — char")]
        public int Slot5Char { get; set; } = 64;
        [ParameterDecl(MinValue = 0, MaxValue = 1, DefValue = 0, ValueDescriptions = new[] { "Off", "On" }, Description = "Slot 5 — mode")]
        public int Slot5Mode { get; set; } = 0;

        [ParameterDecl(MinValue = 0, MaxValue = 127, DefValue = 64, Description = "Slot 6 — char")]
        public int Slot6Char { get; set; } = 64;
        [ParameterDecl(MinValue = 0, MaxValue = 1, DefValue = 0, ValueDescriptions = new[] { "Off", "On" }, Description = "Slot 6 — mode")]
        public int Slot6Mode { get; set; } = 0;

        // Output safety limiter (zero-latency peak limiter + soft-clip ceiling).
        // On by default; appended last (Build §3.3).
        [ParameterDecl(MinValue = 0, MaxValue = 1, DefValue = 1, ValueDescriptions = new[] { "Off", "On" }, Description = "Output limiter")]
        public int Limiter { get; set; } = 1;

        // Auto-gain leveler — brings quiet output up toward a target, holds on
        // silence. Pair with the limiter for "set and forget" output level.
        // Default Off so existing songs are unchanged; appended (Build §3.3).
        [ParameterDecl(MinValue = 0, MaxValue = 1, DefValue = 0, ValueDescriptions = new[] { "Off", "On" }, Description = "Auto gain (leveler)")]
        public int AutoGainOn { get; set; } = 0;

        void PushParamsToSlots()
        {
            _slots[0].SetParams(Slot1Type, Slot1Amount, Slot1Char, Slot1Mode != 0);
            _slots[1].SetParams(Slot2Type, Slot2Amount, Slot2Char, Slot2Mode != 0);
            _slots[2].SetParams(Slot3Type, Slot3Amount, Slot3Char, Slot3Mode != 0);
            _slots[3].SetParams(Slot4Type, Slot4Amount, Slot4Char, Slot4Mode != 0);
            _slots[4].SetParams(Slot5Type, Slot5Amount, Slot5Char, Slot5Mode != 0);
            _slots[5].SetParams(Slot6Type, Slot6Amount, Slot6Char, Slot6Mode != 0);
        }

        bool AnyTailRinging()
        {
            for (int s = 0; s < SLOTS; s++) if (_slots[s].IsRinging) return true;
            return false;
        }

        // ── Work — single stereo in/out EffectBlock ─────────────────────────
        public bool Work(Sample[] output, Sample[] input, int n, WorkModes mode)
        {
            int sr = (int)host.MasterInfo.SamplesPerSec;
            float spt = (float)host.MasterInfo.SamplesPerTick;
            if (sr > 0 && sr != _sampleRate)
            {
                _sampleRate = sr;
                for (int s = 0; s < SLOTS; s++) _slots[s].Prepare(_sampleRate, spt);
                _limiter.Prepare(_sampleRate);
                _autoGain.Prepare(_sampleRate);
            }

            // WM_NOIO: input silent. Keep rendering tails (a Reverb/Delay in any
            // slot is still ringing); sleep only once the whole rack is quiet.
            if (mode == WorkModes.WM_NOIO)
            {
                if (!AnyTailRinging()) return false;
                if (input == null)
                    for (int i = 0; i < n; i++) { output[i].L = 0f; output[i].R = 0f; }
            }

            PushParamsToSlots();

            // ReBuzz audio is ±32768, not ±1.0 (Core §38 / PedalComp §1).
            // Normalise to ±1.0 for the DSP, denormalise on output — otherwise
            // any absolute-threshold stage (limiter, drive, soft-clip) is wildly
            // mis-scaled (the limiter crushes full-scale audio to silence).
            const float INV = 1f / 32768f, SCALE = 32768f;
            for (int i = 0; i < n; i++)
            {
                float l = (input != null ? input[i].L : output[i].L) * INV;
                float r = (input != null ? input[i].R : output[i].R) * INV;

                // serial rack: slot 0 → slot 5
                for (int s = 0; s < SLOTS; s++)
                    _slots[s].Process(ref l, ref r);

                if (AutoGainOn != 0) _autoGain.Process(ref l, ref r);
                if (Limiter != 0) _limiter.Process(ref l, ref r);

                output[i].L = l * SCALE;
                output[i].R = r * SCALE;
            }

            return true;
        }

        // ── Value labelling (DescribeValue) ─────────────────────────────────
        // Called only on cursor hover (UI thread, never the audio thread), so it
        // can be as descriptive as we like. Char/Mode read the slot's current
        // Type, so the readout reflects whichever effect is loaded — e.g. a
        // slot's Char shows "Q 2.0" under Filter but "Feedback 47%" under Delay,
        // and its Mode shows "Lowpass" / "Ping-pong" accordingly. Returning null
        // falls back to ValueDescriptions (Type, Limiter, Auto Gain).
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string Pct(int v) => ((int)MathF.Round(v * 100f / 127f)).ToString(Inv) + "%";

        int SlotTypeOf(int slot) => slot switch
        {
            1 => Slot1Type, 2 => Slot2Type, 3 => Slot3Type,
            4 => Slot4Type, 5 => Slot5Type, 6 => Slot6Type, _ => 0
        };

        public string DescribeValue(IParameter param, int value)
        {
            string n = param?.Name ?? "";
            if (n.StartsWith("Slot") && n.Length > 5 && char.IsDigit(n[4]))
            {
                FxType t = (FxType)SlotTypeOf(n[4] - '0');
                if (n.EndsWith("Amount")) return Pct(value);
                if (n.EndsWith("Char"))   return CharLabel(t, value);
                if (n.EndsWith("Mode"))   return ModeLabel(t, value != 0);
            }
            return null;   // Type / Limiter / Auto Gain → ValueDescriptions
        }

        static string CharLabel(FxType t, int v)
        {
            float p = v / 127f;
            switch (t)
            {
                case FxType.Bitcrush: return "Crush\u2194Rate " + Pct(v);
                case FxType.Drive:    return "Bias " + ((p - 0.5f) * 0.8f).ToString("+0.00;-0.00;0.00", Inv);
                case FxType.Filter:   return "Q " + (0.5f * MathF.Pow(16f, p)).ToString("0.0", Inv);
                case FxType.RingMod:  return "Tune " + ((p - 0.5f) * 2f).ToString("+0.00;-0.00;0.00", Inv) + " oct";
                case FxType.Comb:     return "Damp " + Pct(v);
                case FxType.Stutter:  return "Repeats " + (2 + (int)MathF.Round(p * 6f)).ToString(Inv);
                case FxType.Delay:    return "Feedback " + ((int)MathF.Round(p * 95f)).ToString(Inv) + "%";
                case FxType.Reverb:   return "Damping " + Pct(v);
                case FxType.Gate:     return "Duty " + ((int)MathF.Round((0.05f + p * 0.9f) * 100f)).ToString(Inv) + "%";
                default:              return "\u2014";
            }
        }

        static string ModeLabel(FxType t, bool on)
        {
            switch (t)
            {
                case FxType.Bitcrush: return on ? "Anti-alias"   : "Raw";
                case FxType.Drive:    return on ? "Hard clip"    : "Soft";
                case FxType.Filter:   return on ? "Highpass"     : "Lowpass";
                case FxType.RingMod:  return on ? "AM"           : "Ring mod";
                case FxType.Comb:     return on ? "Neg feedback" : "Pos feedback";
                case FxType.Stutter:  return on ? "Reverse"      : "Forward";
                case FxType.Delay:    return on ? "Ping-pong"    : "Mono";
                case FxType.Reverb:   return on ? "Bright"       : "Normal";
                case FxType.Gate:     return on ? "Triplet"      : "Straight";
                default:              return "\u2014";
            }
        }
    }
}
