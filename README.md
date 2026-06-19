# Pedal Drum Matrix

A single stereo-in / stereo-out drum-fx **rack** for ReBuzz: six serial slots,
each one a swappable effect. Aimed at serious textural transformation of drum
sounds.

## How it's controlled

Per slot:
- **Slot N Type** — picks the effect (None / Bitcrush / Drive / Filter / RingMod
  / Comb / Stutter / Delay / Reverb / Gate). Automatable and peer/MIDI mappable.
- **Slot N Amount** — overall intensity / wet (amount 0 = clean).
- **Slot N Char** — continuous 0–100 character control (effect-specific). Maps
  to a BCR2000 encoder's rotation.
- **Slot N Mode** — switch (effect-specific). Maps to the encoder's push.

The Char + Mode pair per slot is designed for the BCR2000's six dual-function
(rotary + push) encoders — one encoder per slot. Per-effect meanings:

| Effect | Char (rotary) | Mode (switch) |
|---|---|---|
| Bitcrush | bit-crush ↔ decimation tilt | anti-alias filter |
| Drive | bias / asymmetry | hard clip vs soft |
| Filter | resonance (Q) | lowpass vs highpass |
| RingMod | carrier fine tune (±1 oct) | ring-mod vs AM |
| Comb | feedback damping | +/− feedback sign |
| Stutter | repeats (2–8) | reverse slice |
| Delay | feedback (mix stays on Amount) | ping-pong |
| Reverb | damping (bright↔dark) | bright tilt |
| Gate | duty cycle | triplet timing |

Char/Mode were appended after the original Type/Amount params (Build §3.3), so
older test songs keep their values; they appear at the bottom of the rack.
Note the new defaults (Char 64, Mode off) re-voice some effects slightly versus
v0.3 — e.g. Filter resonance and Delay feedback are now their own controls.

Signal flow is serial: slot 1 → 2 → 3 → 4 → 5 → 6 → out.

## Architecture

- Each `Slot` pre-builds one instance of **every** effect type, so changing a
  slot's type at runtime is an index change, never an audio-thread allocation
  (no GC). Swaps run a 10 ms equal-power crossfade → click-free.
- `IDrumFx` exposes `IsRinging`; the machine ORs it across all slots so a Reverb
  or Delay in any slot keeps its tail after the drums stop (tail-aware
  `WM_NOIO`, Core §33).
- Amount is smoothed per sample (Core §32).
- The `FxType` enum order is the preset contract (Build §3.3): **append only**;
  never reorder. The `ValueDescriptions` literal on each Slot Type param must
  match it.

## Status

v1.1 — DescribeValue labels each control's value with its real per-effect function. **All ten slot types are implemented**: None,
Bitcrush, Drive, Filter, RingMod, Comb, Stutter, Delay, Reverb, Gate. Each of
the six slots selects independently, so any slot can hold any effect (and the
same effect can sit in more than one slot — each slot owns its own instances).

Effect notes:
- Bitcrush — decimation + bit reduction. Tail-free.
- Drive — tanh saturation with makeup gain. Tail-free.
- Filter — TPT state-variable LP; amount sweeps cutoff 18 kHz → 150 Hz. Tail-free.
- RingMod — sine carrier 30 Hz → 3 kHz, wet scales with amount. Tail-free.
- Comb — short feedback resonator (metallic); amount raises pitch + feedback. Rings.
- Delay — tempo-synced (≈6 ticks, from `host.MasterInfo.SamplesPerTick`),
  feedback + mix scale with amount. Rings.
- Gate — tempo-synced rhythmic gate, 8→1 ticks/cycle, 50% duty. Tail-free.
- Stutter — tempo-synced beat-repeat: latches a slice and loops it 4× before
  grabbing a new one; amount shortens the slice + raises wet. Rings, then
  self-silences once it latches silence.
- Reverb — Freeverb-style (8 lowpass-comb + 4 allpass per channel); amount
  sets tail length + wet. Rings.

Comb and Delay report `IsRinging` via a decaying-energy tracker so their tails
survive `WM_NOIO` and the rack sleeps only once everything is quiet.

## DSP to lift from existing repos

Drive → pedal-shaper · Filter → pedal-zplane · Reverb → pedal-converb ·
Comb/movement → pedal-chorus · bipolar-param offset → pedal-comp §2.

## Files

- `PedalDrumMatrix.NET.csproj` — Build §1.2 props, deploy to `Gear\Effects`.
- `PedalDrumMatrix.cs` — machine, 6×(Type + Amount), serial Work.
- `Slot.cs` — slot host, swap crossfade, amount smoothing, tail flag.
- `DrumFx.cs` — `IDrumFx`, `FxType`, factory, `NoneFx`, `BitcrushFx`.

## Next steps

1. Build against local ReBuzz; confirm it loads and Bitcrush sounds on a slot.
2. Fill effects in order of value for e-drums: Drive, Filter, RingMod, then the
   time-based ones (Delay, Reverb) which exercise the tail logic.
3. Consider a GUI later to expose per-effect detail params + visual rack.


## Value labelling

Hovering a control shows its real current function via `DescribeValue`, read
live from the slot's Type. A slot's **Char** reads e.g. `Q 2.0` under Filter,
`Feedback 47%` under Delay, `Repeats 5` under Stutter; its **Mode** reads
`Lowpass`/`Highpass`, `Ping-pong`/`Mono`, etc. Amount reads as a percentage.
The static control names stay generic (the per-knob labels are a GUI job); the
value readout carries the meaning.

## Sample scale

ReBuzz audio is ±32768, not ±1.0 (Core §38 / PedalComp §1). The machine
normalises to ±1.0 at the input, runs all DSP there, and denormalises (×32768)
at the output. Without this the output limiter crushed full-scale drums to
near-silence, Drive saturated everything to ±1, and Bitcrush's bit-reduction
did almost nothing. Linear/ratio effects (Filter, Delay, Comb, Reverb, RingMod,
Gate, Stutter) were unaffected.

## Output stage & CPU hygiene

- **Output limiter** (global `Limiter`, On by default): zero-latency
  stereo-linked peak limiter (1 ms attack / 100 ms release) into a cubic
  soft-clip ceiling at ~0.95. Catches level swings from Drive, resonant
  Filter, and the feedback effects without a separate machine/connection.
- **Denormal flush-to-zero** on all feedback states (Comb, Delay, Reverb).
  Decaying tails otherwise drift into subnormal floats, which spike CPU on
  some hardware and cause dropouts. `Dsp.Ftz` keeps them normal.
- Building both in-machine (vs. external comp/limiter machines) avoids the
  per-machine + per-connection host overhead that ReBuzz incurs per tick.

