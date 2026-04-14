/**
 * BiomeMusic — procedural ambient music generators.
 *
 * Each generator returns a `BiomeMusicHandle` that can be faded out and stopped.
 * All audio is created with Web Audio API oscillators and noise buffers —
 * no external files.
 */

import { AudioEngine } from './AudioEngine';
import { NOTES } from './notes';

export type BiomeKey =
  | 'forest'
  | 'mountain'
  | 'combat'
  | 'homestead'
  | 'water'
  | 'desert'
  | 'wyrd'
  | 'grassland'   // maps to gentle forest-like
  | 'default';

export interface BiomeMusicHandle {
  /** Gradually fade to silence over `durationSec` then call `stop()`. */
  fadeOut(durationSec: number): void;
  /** Immediately stop and disconnect all nodes. */
  stop(): void;
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function ramp(param: AudioParam, target: number, time: number, duration: number): void {
  param.setTargetAtTime(target, time, duration / 3);
}

/**
 * Play a sine-oscillator "piano" note:
 *  - ADSR-style envelope
 *  - connects to the given output node
 *  - returns the oscillator so the caller can stop it
 */
function pianoNote(
  ctx: AudioContext,
  out: AudioNode,
  freq: number,
  startTime: number,
  duration: number,
  peakGain = 0.18,
): OscillatorNode {
  const osc = ctx.createOscillator();
  const env = ctx.createGain();

  osc.type = 'sine';
  osc.frequency.value = freq;

  // Add slight 2nd harmonic for warmth
  const osc2 = ctx.createOscillator();
  const env2 = ctx.createGain();
  osc2.type = 'sine';
  osc2.frequency.value = freq * 2;
  env2.gain.value = peakGain * 0.15;
  osc2.connect(env2);
  env2.connect(out);

  env.gain.setValueAtTime(0, startTime);
  env.gain.linearRampToValueAtTime(peakGain, startTime + 0.02); // attack
  env.gain.setTargetAtTime(peakGain * 0.6, startTime + 0.02, 0.08); // decay
  env.gain.setTargetAtTime(0, startTime + duration * 0.6, duration * 0.3); // release

  osc.connect(env);
  env.connect(out);

  osc.start(startTime);
  osc2.start(startTime);
  osc.stop(startTime + duration + 0.5);
  osc2.stop(startTime + duration + 0.5);

  return osc;
}

/**
 * Cello-like sustained tone:
 *  - sawtooth wave with slow LFO vibrato
 *  - gentle low-pass filter to soften the harsh saw
 */
function celloTone(
  ctx: AudioContext,
  out: AudioNode,
  freq: number,
  gain = 0.12,
): { fadeOut: (d: number) => void; stop: () => void } {
  const osc  = ctx.createOscillator();
  const filt = ctx.createBiquadFilter();
  const env  = ctx.createGain();
  const lfo  = ctx.createOscillator();
  const lfog = ctx.createGain();

  osc.type = 'sawtooth';
  osc.frequency.value = freq;

  filt.type = 'lowpass';
  filt.frequency.value = freq * 6;
  filt.Q.value = 0.8;

  // Vibrato: ±2 Hz at 5 Hz
  lfo.type = 'sine';
  lfo.frequency.value = 5;
  lfog.gain.value = 2;
  lfo.connect(lfog);
  lfog.connect(osc.frequency);

  env.gain.setValueAtTime(0, ctx.currentTime);
  env.gain.linearRampToValueAtTime(gain, ctx.currentTime + 1.5); // slow attack

  osc.connect(filt);
  filt.connect(env);
  env.connect(out);

  osc.start();
  lfo.start();

  return {
    fadeOut(d: number) {
      env.gain.setTargetAtTime(0, ctx.currentTime, d / 3);
    },
    stop() {
      try { osc.stop(); lfo.stop(); } catch { /* already stopped */ }
      osc.disconnect(); filt.disconnect(); env.disconnect(); lfo.disconnect(); lfog.disconnect();
    },
  };
}

/**
 * Wind / ocean — bandpass-filtered noise with slow volume LFO.
 */
function noisePad(
  ctx: AudioContext,
  out: AudioNode,
  centreFreq: number,
  Q: number,
  peakGain: number,
  lfoFreq: number,
  lfoDepth: number,
): { fadeOut: (d: number) => void; stop: () => void } {
  const engine = AudioEngine.getInstance();
  const src    = ctx.createBufferSource();
  const filt   = ctx.createBiquadFilter();
  const env    = ctx.createGain();
  const lfo    = ctx.createOscillator();
  const lfog   = ctx.createGain();

  src.buffer = engine.getNoiseBuffer();
  src.loop = true;

  filt.type = 'bandpass';
  filt.frequency.value = centreFreq;
  filt.Q.value = Q;

  env.gain.value = peakGain;

  // Volume LFO
  lfo.type = 'sine';
  lfo.frequency.value = lfoFreq;
  lfog.gain.value = lfoDepth;
  lfo.connect(lfog);
  lfog.connect(env.gain);

  src.connect(filt);
  filt.connect(env);
  env.connect(out);

  src.start();
  lfo.start();

  return {
    fadeOut(d: number) {
      env.gain.setTargetAtTime(0, ctx.currentTime, d / 3);
    },
    stop() {
      try { src.stop(); lfo.stop(); } catch { /* already stopped */ }
      src.disconnect(); filt.disconnect(); env.disconnect(); lfo.disconnect(); lfog.disconnect();
    },
  };
}

// ─── Arpeggio scheduler ──────────────────────────────────────────────────────

interface ArpeggioHandle {
  fadeOut(d: number): void;
  stop(): void;
}

/**
 * Schedules arpeggiated piano notes into the future using Web Audio
 * look-ahead scheduling. The `gain` node the arpeggio writes to is exposed
 * so callers can fade it out cleanly.
 */
function arpeggio(
  ctx: AudioContext,
  out: AudioNode,
  notes: number[],
  noteLength: number,   // seconds per note
  peakGain = 0.14,
  reverb?: ConvolverNode,
): ArpeggioHandle {
  const bus  = ctx.createGain();
  bus.gain.value = 1;
  bus.connect(out);

  if (reverb) {
    const reverbBus = ctx.createGain();
    reverbBus.gain.value = 0.25;
    bus.connect(reverbBus);
    reverbBus.connect(reverb);
    reverb.connect(out);
  }

  let running = true;
  let beat = 0;

  function schedule() {
    if (!running) return;

    // Schedule the next note from the current audio clock
    const nextBeatTime = ctx.currentTime;
    const freq = notes[beat % notes.length];
    pianoNote(ctx, bus, freq, nextBeatTime, noteLength * 0.9, peakGain);
    beat++;

    if (running) {
      setTimeout(schedule, noteLength * 1000 - 10);
    }
  }

  schedule();

  return {
    fadeOut(d: number) {
      ramp(bus.gain, 0, ctx.currentTime, d);
      running = false;
    },
    stop() {
      running = false;
      try { bus.disconnect(); } catch { /* ok */ }
    },
  };
}

// ─── Biome generators ────────────────────────────────────────────────────────

function makeForestMusic(engine: AudioEngine): BiomeMusicHandle {
  const ctx = engine.ctx;
  const out = engine.musicBus;

  // Reverb
  const reverb = engine.createReverb(2.0, 2.5);

  // Cello: sustained A2 + E3
  const celloA = celloTone(ctx, out, NOTES.A2, 0.10);
  const celloE = celloTone(ctx, out, NOTES.E3, 0.07);

  // Piano arpeggio: Am pentatonic — A3 C4 E4 A4
  const pianoArp = arpeggio(
    ctx, out,
    [NOTES.A3, NOTES.C4, NOTES.E4, NOTES.A4, NOTES.E4, NOTES.C4],
    0.5,
    0.14,
    reverb,
  );

  // Occasional bird chirps: random high sine pings
  let birdRunning = true;
  function scheduleBird() {
    if (!birdRunning) return;
    const delay = 4000 + Math.random() * 8000;
    setTimeout(() => {
      if (!birdRunning) return;
      const freq = 2000 + Math.random() * 1500;
      const osc = ctx.createOscillator();
      const env = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(freq, ctx.currentTime);
      osc.frequency.linearRampToValueAtTime(freq * 1.3, ctx.currentTime + 0.05);
      env.gain.setValueAtTime(0.04, ctx.currentTime);
      env.gain.setTargetAtTime(0, ctx.currentTime + 0.05, 0.04);
      osc.connect(env);
      env.connect(out);
      osc.start();
      osc.stop(ctx.currentTime + 0.2);
      scheduleBird();
    }, delay);
  }
  scheduleBird();

  return {
    fadeOut(d: number) {
      birdRunning = false;
      celloA.fadeOut(d); celloE.fadeOut(d);
      pianoArp.fadeOut(d);
    },
    stop() {
      birdRunning = false;
      celloA.stop(); celloE.stop();
      pianoArp.stop();
      try { reverb.disconnect(); } catch { /* ok */ }
    },
  };
}

function makeMountainMusic(engine: AudioEngine): BiomeMusicHandle {
  const ctx = engine.ctx;
  const out = engine.musicBus;

  // Deep cello drone: D2
  const celloD = celloTone(ctx, out, NOTES.D2, 0.13);

  // Wind noise
  const wind = noisePad(ctx, out, 600, 1.5, 0.06, 0.08, 0.04);

  // Reverb for bell
  const reverb = engine.createReverb(3.0, 1.5);

  // Piano: open fifths D3-A3, slow whole notes
  const pianoArp = arpeggio(
    ctx, out,
    [NOTES.D3, NOTES.A3, NOTES.D4, NOTES.A3],
    2.0,
    0.12,
    reverb,
  );

  // Occasional bell ping: triangle wave at D5
  let bellRunning = true;
  function scheduleBell() {
    if (!bellRunning) return;
    const delay = 8000 + Math.random() * 12000;
    setTimeout(() => {
      if (!bellRunning) return;
      const osc = ctx.createOscillator();
      const env = ctx.createGain();
      osc.type = 'triangle';
      osc.frequency.value = NOTES.D5;
      env.gain.setValueAtTime(0.15, ctx.currentTime);
      env.gain.setTargetAtTime(0, ctx.currentTime + 0.1, 1.5);
      osc.connect(env);
      env.connect(reverb);
      reverb.connect(out);
      osc.start();
      osc.stop(ctx.currentTime + 4);
      scheduleBell();
    }, delay);
  }
  scheduleBell();

  return {
    fadeOut(d: number) {
      bellRunning = false;
      celloD.fadeOut(d); wind.fadeOut(d); pianoArp.fadeOut(d);
    },
    stop() {
      bellRunning = false;
      celloD.stop(); wind.stop(); pianoArp.stop();
      try { reverb.disconnect(); } catch { /* ok */ }
    },
  };
}

function makeCombatMusic(engine: AudioEngine): BiomeMusicHandle {
  const ctx = engine.ctx;
  const out = engine.musicBus;

  // Staccato cello E2 — rhythmic short bursts at quarter notes (120 bpm = 0.5s)
  const beatInterval = 0.5;
  let combatRunning = true;
  const combatTimers: ReturnType<typeof setTimeout>[] = [];

  const celloMasterGain = ctx.createGain();
  celloMasterGain.gain.value = 1;
  celloMasterGain.connect(out);

  function scheduleStaccato() {
    if (!combatRunning) return;
    const osc  = ctx.createOscillator();
    const env  = ctx.createGain();
    const filt = ctx.createBiquadFilter();
    osc.type = 'sawtooth';
    osc.frequency.value = NOTES.E2;
    filt.type = 'lowpass';
    filt.frequency.value = NOTES.E2 * 8;
    env.gain.setValueAtTime(0.15, ctx.currentTime);
    env.gain.setTargetAtTime(0, ctx.currentTime + 0.05, 0.08);
    osc.connect(filt);
    filt.connect(env);
    env.connect(celloMasterGain);
    osc.start();
    osc.stop(ctx.currentTime + 0.3);

    const t = combatTimers.push(setTimeout(scheduleStaccato, beatInterval * 1000));
    void t;
  }

  scheduleStaccato();

  // Driving piano: E3-B3-E4 at quarter notes offset by half
  const pianoArp = arpeggio(
    ctx, out,
    [NOTES.E3, NOTES.B3, NOTES.E4, NOTES.B3],
    beatInterval,
    0.16,
  );

  // Percussion: noise burst on beats 1 and 3
  const percGain = ctx.createGain();
  percGain.gain.value = 1;
  percGain.connect(out);

  function schedulePerc(beat: number) {
    if (!combatRunning) return;
    if (beat % 2 === 0) {
      const src  = ctx.createBufferSource();
      const filt = ctx.createBiquadFilter();
      const env  = ctx.createGain();
      src.buffer = engine.getNoiseBuffer();
      src.loop = false;
      filt.type = 'bandpass';
      filt.frequency.value = 150;
      filt.Q.value = 0.5;
      env.gain.setValueAtTime(0.25, ctx.currentTime);
      env.gain.setTargetAtTime(0, ctx.currentTime + 0.01, 0.04);
      src.connect(filt);
      filt.connect(env);
      env.connect(percGain);
      src.start();
      src.stop(ctx.currentTime + 0.1);
    }
    combatTimers.push(setTimeout(() => schedulePerc(beat + 1), beatInterval * 500)); // every half-beat
  }

  schedulePerc(0);

  return {
    fadeOut(d: number) {
      combatRunning = false;
      combatTimers.forEach(t => clearTimeout(t));
      pianoArp.fadeOut(d);
      ramp(celloMasterGain.gain, 0, ctx.currentTime, d);
      ramp(percGain.gain, 0, ctx.currentTime, d);
    },
    stop() {
      combatRunning = false;
      combatTimers.forEach(t => clearTimeout(t));
      pianoArp.stop();
      try { celloMasterGain.disconnect(); percGain.disconnect(); } catch { /* ok */ }
    },
  };
}

function makeHomesteadMusic(engine: AudioEngine): BiomeMusicHandle {
  const ctx = engine.ctx;
  const out = engine.musicBus;

  // Warm C major pad: layered sines
  const pad: { fadeOut: (d: number) => void; stop: () => void }[] = [];
  const padFreqs = [NOTES.C3, NOTES.E3, NOTES.G3, NOTES.C4];
  for (const freq of padFreqs) {
    const osc = ctx.createOscillator();
    const env = ctx.createGain();
    osc.type = 'sine';
    osc.frequency.value = freq;
    env.gain.setValueAtTime(0, ctx.currentTime);
    env.gain.linearRampToValueAtTime(0.06, ctx.currentTime + 2);
    osc.connect(env);
    env.connect(out);
    osc.start();
    pad.push({
      fadeOut(d: number) { ramp(env.gain, 0, ctx.currentTime, d); },
      stop() {
        try { osc.stop(); } catch { /* ok */ }
        osc.disconnect(); env.disconnect();
      },
    });
  }

  // Gentle piano arpeggio: C3→E3→G3→C4, slow (1.2s per note)
  const reverb = engine.createReverb(1.5, 2.0);
  const pianoArp = arpeggio(
    ctx, out,
    [NOTES.C3, NOTES.E3, NOTES.G3, NOTES.C4, NOTES.G3, NOTES.E3],
    1.2,
    0.12,
    reverb,
  );

  return {
    fadeOut(d: number) {
      pad.forEach(p => p.fadeOut(d));
      pianoArp.fadeOut(d);
    },
    stop() {
      pad.forEach(p => p.stop());
      pianoArp.stop();
      try { reverb.disconnect(); } catch { /* ok */ }
    },
  };
}

function makeWaterMusic(engine: AudioEngine): BiomeMusicHandle {
  const ctx = engine.ctx;
  const out = engine.musicBus;

  // Wave rhythm: filtered noise, slow LFO (0.15 Hz = ~7s wave cycle)
  const wave = noisePad(ctx, out, 400, 1.2, 0.08, 0.15, 0.06);

  // Sub bass: F1 very quiet
  const subOsc = ctx.createOscillator();
  const subEnv = ctx.createGain();
  subOsc.type = 'sine';
  subOsc.frequency.value = NOTES.A1 * 0.7; // approximate F1
  subEnv.gain.setValueAtTime(0, ctx.currentTime);
  subEnv.gain.linearRampToValueAtTime(0.04, ctx.currentTime + 2);
  subOsc.connect(subEnv);
  subEnv.connect(out);
  subOsc.start();

  // Piano arpeggios: F3→A3→C4, gentle
  const reverb = engine.createReverb(2.0, 1.8);
  const pianoArp = arpeggio(
    ctx, out,
    [NOTES.F3, NOTES.A3, NOTES.C4, NOTES.F4, NOTES.C4, NOTES.A3],
    0.7,
    0.11,
    reverb,
  );

  return {
    fadeOut(d: number) {
      wave.fadeOut(d);
      ramp(subEnv.gain, 0, ctx.currentTime, d);
      pianoArp.fadeOut(d);
    },
    stop() {
      wave.stop();
      try { subOsc.stop(); } catch { /* ok */ }
      subOsc.disconnect(); subEnv.disconnect();
      pianoArp.stop();
      try { reverb.disconnect(); } catch { /* ok */ }
    },
  };
}

function makeDesertMusic(engine: AudioEngine): BiomeMusicHandle {
  const ctx = engine.ctx;
  const out = engine.musicBus;

  // Sparse dry wind noise
  const wind = noisePad(ctx, out, 800, 3, 0.03, 0.05, 0.02);

  // Sparse plucked notes: Bb3, F3, sparse timing
  const pluckNotes = [NOTES.Bb3, NOTES.F3, NOTES.Eb3, NOTES.Bb2];
  let desertRunning = true;

  function schedulePluck() {
    if (!desertRunning) return;
    const delay = 3000 + Math.random() * 6000;
    setTimeout(() => {
      if (!desertRunning) return;
      const freq = pluckNotes[Math.floor(Math.random() * pluckNotes.length)];
      pianoNote(ctx, out, freq, ctx.currentTime, 1.5, 0.10);
      schedulePluck();
    }, delay);
  }

  schedulePluck();

  return {
    fadeOut(d: number) {
      desertRunning = false;
      wind.fadeOut(d);
    },
    stop() {
      desertRunning = false;
      wind.stop();
    },
  };
}

function makeWyrdMusic(engine: AudioEngine): BiomeMusicHandle {
  const ctx = engine.ctx;
  const out = engine.musicBus;

  // Two detuned oscillators, phase-shifting
  const osc1 = ctx.createOscillator();
  const osc2 = ctx.createOscillator();
  const env1  = ctx.createGain();
  const env2  = ctx.createGain();

  // Tritone interval: C3 + Gb3 (dissonant)
  osc1.type = 'sine';
  osc1.frequency.value = NOTES.C3;
  osc2.type = 'sine';
  osc2.frequency.value = NOTES.Gb3 + 3; // detune slightly for phasing

  env1.gain.setValueAtTime(0, ctx.currentTime);
  env1.gain.linearRampToValueAtTime(0.10, ctx.currentTime + 3);
  env2.gain.setValueAtTime(0, ctx.currentTime);
  env2.gain.linearRampToValueAtTime(0.10, ctx.currentTime + 3);

  osc1.connect(env1); env1.connect(out);
  osc2.connect(env2); env2.connect(out);
  osc1.start(); osc2.start();

  // Reverse-envelope pads: volume ramps UP then cuts
  let wyrdRunning = true;
  const wyrdTimers: ReturnType<typeof setTimeout>[] = [];

  function scheduleReversePad() {
    if (!wyrdRunning) return;
    const delay = 3000 + Math.random() * 5000;
    wyrdTimers.push(setTimeout(() => {
      if (!wyrdRunning) return;
      // Pick a chromatic note
      const chromatic = [NOTES.Db4, NOTES.E4, NOTES.A4, NOTES.Bb3, NOTES.Eb4];
      const freq = chromatic[Math.floor(Math.random() * chromatic.length)];
      const osc  = ctx.createOscillator();
      const env  = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.value = freq;
      env.gain.setValueAtTime(0, ctx.currentTime);
      env.gain.linearRampToValueAtTime(0.12, ctx.currentTime + 1.5); // ramp UP
      env.gain.setValueAtTime(0, ctx.currentTime + 1.5 + 0.001);    // sudden cut
      osc.connect(env);
      env.connect(out);
      osc.start();
      osc.stop(ctx.currentTime + 2);
      scheduleReversePad();
    }, delay));
  }

  // Random pitch glitches
  function scheduleGlitch() {
    if (!wyrdRunning) return;
    const delay = 5000 + Math.random() * 10000;
    wyrdTimers.push(setTimeout(() => {
      if (!wyrdRunning) return;
      // Briefly pitch-shift osc1 to a minor 2nd
      const base = NOTES.C3;
      osc1.frequency.setValueAtTime(base * 1.06, ctx.currentTime);          // minor 2nd up
      osc1.frequency.setTargetAtTime(base, ctx.currentTime + 0.1, 0.2);    // glide back
      scheduleGlitch();
    }, delay));
  }

  scheduleReversePad();
  scheduleGlitch();

  return {
    fadeOut(d: number) {
      wyrdRunning = false;
      wyrdTimers.forEach(t => clearTimeout(t));
      ramp(env1.gain, 0, ctx.currentTime, d);
      ramp(env2.gain, 0, ctx.currentTime, d);
    },
    stop() {
      wyrdRunning = false;
      wyrdTimers.forEach(t => clearTimeout(t));
      try { osc1.stop(); osc2.stop(); } catch { /* ok */ }
      osc1.disconnect(); osc2.disconnect(); env1.disconnect(); env2.disconnect();
    },
  };
}

// ─── Public API ──────────────────────────────────────────────────────────────

/**
 * Map BiomeType names from biome.ts to a BiomeKey.
 * Also accepts combat / homestead overrides.
 */
export function resolveBiomeKey(
  biomeType: string,
  inCombat: boolean,
  atHomestead: boolean,
): BiomeKey {
  if (inCombat)    return 'combat';
  if (atHomestead) return 'homestead';

  switch (biomeType) {
    case 'denseForest':
    case 'lightForest': return 'forest';
    case 'mountain':
    case 'snowMountain': return 'mountain';
    case 'water':       return 'water';
    case 'sand':        return 'desert';
    case 'grassland':   return 'grassland';
    default:            return 'default';
  }
}

export function startBiomeMusic(key: BiomeKey): BiomeMusicHandle {
  const engine = AudioEngine.getInstance();
  console.log('[audio] schedule-oscillator', { key, ctxState: engine.ctx.state, musicVol: engine.getMusicVolume(), muted: engine.isMuted() });

  switch (key) {
    case 'forest':
    case 'grassland':
    case 'default':   return makeForestMusic(engine);
    case 'mountain':  return makeMountainMusic(engine);
    case 'combat':    return makeCombatMusic(engine);
    case 'homestead': return makeHomesteadMusic(engine);
    case 'water':     return makeWaterMusic(engine);
    case 'desert':    return makeDesertMusic(engine);
    case 'wyrd':      return makeWyrdMusic(engine);
  }
}
