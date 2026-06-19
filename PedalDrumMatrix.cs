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
    }
}
