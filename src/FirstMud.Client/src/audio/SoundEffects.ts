/**
 * SoundEffects — one-shot procedural sound generators.
 *
 * All methods are static. Each creates and immediately plays a short audio
 * event using the shared AudioEngine SFX bus.
 */

import { AudioEngine } from './AudioEngine';
import { NOTES } from './notes';

function ctx(): AudioContext { return AudioEngine.getInstance().ctx; }
function sfx(): GainNode     { return AudioEngine.getInstance().sfxBus; }

// ─── Helpers ─────────────────────────────────────────────────────────────────

function noiseHit(
  centreFreq: number,
  Q: number,
  peak: number,
  duration: number,
): void {
  const c   = ctx();
  const s   = sfx();
  const src = c.createBufferSource();
  const buf = AudioEngine.getInstance().getNoiseBuffer();
  src.buffer = buf;
  src.loop = false;

  const filt = c.createBiquadFilter();
  filt.type = 'bandpass';
  filt.frequency.value = centreFreq;
  filt.Q.value = Q;

  const env = c.createGain();
  env.gain.setValueAtTime(peak, c.currentTime);
  env.gain.setTargetAtTime(0, c.currentTime, duration / 3);

  src.connect(filt);
  filt.connect(env);
  env.connect(s);
  src.start();
  src.stop(c.currentTime + duration + 0.1);
}

function sineNote(
  freq: number,
  startDelay: number,
  duration: number,
  peak: number,
  type: OscillatorType = 'sine',
): void {
  const c   = ctx();
  const s   = sfx();
  const osc = c.createOscillator();
  const env = c.createGain();
  osc.type = type;
  osc.frequency.value = freq;
  env.gain.setValueAtTime(0, c.currentTime + startDelay);
  env.gain.linearRampToValueAtTime(peak, c.currentTime + startDelay + 0.01);
  env.gain.setTargetAtTime(0, c.currentTime + startDelay + duration * 0.5, duration / 4);
  osc.connect(env);
  env.connect(s);
  osc.start(c.currentTime + startDelay);
  osc.stop(c.currentTime + startDelay + duration + 0.2);
}

function reverbNote(
  freq: number,
  duration: number,
  peak: number,
): void {
  const engine = AudioEngine.getInstance();
  const c      = engine.ctx;
  const s      = engine.sfxBus;
  const reverb = engine.createReverb(0.8, 2.0);
  const reverbBus = c.createGain();
  reverbBus.gain.value = 0.3;
  reverb.connect(s);
  sineNote(freq, 0, duration, peak);
  // Duplicate into reverb
  const osc = c.createOscillator();
  const env = c.createGain();
  osc.type = 'sine';
  osc.frequency.value = freq;
  env.gain.setValueAtTime(peak, c.currentTime);
  env.gain.setTargetAtTime(0, c.currentTime + duration * 0.5, duration / 4);
  osc.connect(env);
  env.connect(reverbBus);
  reverbBus.connect(reverb);
  osc.start();
  osc.stop(c.currentTime + duration + 0.5);
}

// ─── Public SFX ──────────────────────────────────────────────────────────────

export class SoundEffects {

  /** Melee strike: noise burst 50 ms + pitch drop */
  static strike(): void {
    noiseHit(300, 1, 0.4, 0.05);
    const c = ctx(); const s = sfx();
    const osc = c.createOscillator();
    const env = c.createGain();
    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(200, c.currentTime);
    osc.frequency.exponentialRampToValueAtTime(60, c.currentTime + 0.12);
    env.gain.setValueAtTime(0.25, c.currentTime);
    env.gain.setTargetAtTime(0, c.currentTime + 0.02, 0.04);
    osc.connect(env); env.connect(s);
    osc.start(); osc.stop(c.currentTime + 0.2);
  }

  /** Critical hit: louder burst + major chord sting */
  static criticalHit(): void {
    noiseHit(400, 0.8, 0.6, 0.06);
    // Major chord: E4-Ab4-B4
    [NOTES.E4, NOTES.Ab4, NOTES.B4].forEach((f, i) => {
      sineNote(f, i * 0.03, 0.3, 0.20, 'triangle');
    });
  }

  /** Miss: quick whoosh — bandpass noise, short */
  static miss(): void {
    const c = ctx(); const s = sfx();
    const src = c.createBufferSource();
    src.buffer = AudioEngine.getInstance().getNoiseBuffer();
    const filt = c.createBiquadFilter();
    filt.type = 'bandpass';
    filt.frequency.setValueAtTime(800, c.currentTime);
    filt.frequency.exponentialRampToValueAtTime(200, c.currentTime + 0.12);
    filt.Q.value = 2;
    const env = c.createGain();
    env.gain.setValueAtTime(0.15, c.currentTime);
    env.gain.setTargetAtTime(0, c.currentTime + 0.04, 0.04);
    src.connect(filt); filt.connect(env); env.connect(s);
    src.start(); src.stop(c.currentTime + 0.15);
  }

  /** Dodge: fast rising pitch (sine, 100 ms) */
  static dodge(): void {
    const c = ctx(); const s = sfx();
    const osc = c.createOscillator();
    const env = c.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(300, c.currentTime);
    osc.frequency.exponentialRampToValueAtTime(900, c.currentTime + 0.1);
    env.gain.setValueAtTime(0.18, c.currentTime);
    env.gain.setTargetAtTime(0, c.currentTime + 0.06, 0.03);
    osc.connect(env); env.connect(s);
    osc.start(); osc.stop(c.currentTime + 0.15);
  }

  /** Level up: ascending arpeggio C-E-G-C */
  static levelUp(): void {
    [NOTES.C4, NOTES.E4, NOTES.G4, NOTES.C5].forEach((f, i) => {
      sineNote(f, i * 0.12, 0.4, 0.22, 'sine');
    });
  }

  /** Quest complete: 3-note fanfare G-B-D (triangle) */
  static questComplete(): void {
    [NOTES.G4, NOTES.B4, NOTES.D5].forEach((f, i) => {
      sineNote(f, i * 0.15, 0.45, 0.20, 'triangle');
    });
  }

  /** Loot drop: high sine ping with reverb (1000 Hz, 100 ms) */
  static lootDrop(): void {
    reverbNote(1000, 0.12, 0.18);
  }

  /** Rare loot: sparkle — 3 pings at 1000, 1500, 2000 Hz */
  static rareLoot(): void {
    [1000, 1500, 2000].forEach((f, i) => {
      reverbNote(f, 0.1 + i * 0.02, 0.20 - i * 0.02);
    });
    // Small stagger
    const c = ctx(); const s = sfx();
    setTimeout(() => {
      [1000, 1500, 2000].forEach((f, i) => {
        const osc = c.createOscillator();
        const env = c.createGain();
        osc.type = 'sine';
        osc.frequency.value = f;
        const t0 = c.currentTime + i * 0.05;
        env.gain.setValueAtTime(0.12, t0);
        env.gain.setTargetAtTime(0, t0 + 0.05, 0.08);
        osc.connect(env); env.connect(s);
        osc.start(t0); osc.stop(t0 + 0.3);
      });
    }, 300);
  }

  /** Portal: swept bandpass filter on noise (1s) */
  static portal(): void {
    const c = ctx(); const s = sfx();
    const src = c.createBufferSource();
    src.buffer = AudioEngine.getInstance().getNoiseBuffer();
    src.loop = false;
    const filt = c.createBiquadFilter();
    filt.type = 'bandpass';
    filt.frequency.setValueAtTime(200, c.currentTime);
    filt.frequency.exponentialRampToValueAtTime(4000, c.currentTime + 0.5);
    filt.frequency.exponentialRampToValueAtTime(200, c.currentTime + 1.0);
    filt.Q.value = 8;
    const env = c.createGain();
    env.gain.setValueAtTime(0.3, c.currentTime);
    env.gain.setTargetAtTime(0, c.currentTime + 0.7, 0.2);
    src.connect(filt); filt.connect(env); env.connect(s);
    src.start(); src.stop(c.currentTime + 1.2);
  }

  /** Heal: soft rising pad (sine, 500 ms envelope) */
  static heal(): void {
    const freqs = [NOTES.C4, NOTES.E4, NOTES.G4];
    freqs.forEach((f, i) => {
      const c = ctx(); const s = sfx();
      const osc = c.createOscillator();
      const env = c.createGain();
      osc.type = 'sine';
      osc.frequency.value = f;
      const t0 = c.currentTime + i * 0.06;
      env.gain.setValueAtTime(0, t0);
      env.gain.linearRampToValueAtTime(0.14, t0 + 0.2);
      env.gain.setTargetAtTime(0, t0 + 0.3, 0.15);
      osc.connect(env); env.connect(s);
      osc.start(t0); osc.stop(t0 + 0.7);
    });
  }

  /** Defeat: descending minor chord A-C-E dropping in pitch */
  static defeat(): void {
    [[NOTES.A4, NOTES.A3], [NOTES.C4, NOTES.C3], [NOTES.E4, NOTES.E3]].forEach(([start, end], i) => {
      const c = ctx(); const s = sfx();
      const osc = c.createOscillator();
      const env = c.createGain();
      osc.type = 'sine';
      const t0 = c.currentTime + i * 0.08;
      osc.frequency.setValueAtTime(start, t0);
      osc.frequency.exponentialRampToValueAtTime(end, t0 + 0.8);
      env.gain.setValueAtTime(0.20, t0);
      env.gain.setTargetAtTime(0, t0 + 0.3, 0.3);
      osc.connect(env); env.connect(s);
      osc.start(t0); osc.stop(t0 + 1.2);
    });
  }

  /** Capture: triumphant sting + shimmer */
  static capture(): void {
    // Triumphant chord G-B-D
    [NOTES.G4, NOTES.B4, NOTES.D5].forEach((f, i) => {
      sineNote(f, i * 0.06, 0.5, 0.20, 'triangle');
    });
    // Shimmer: 3 quick pings
    [1200, 1800, 2400].forEach((f, i) => {
      sineNote(f, 0.3 + i * 0.08, 0.15, 0.12, 'sine');
    });
  }

  /** Harvest: soft thud + rustle */
  static harvest(): void {
    // Low thud
    const c = ctx(); const s = sfx();
    const osc = c.createOscillator();
    const env = c.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(120, c.currentTime);
    osc.frequency.exponentialRampToValueAtTime(40, c.currentTime + 0.08);
    env.gain.setValueAtTime(0.35, c.currentTime);
    env.gain.setTargetAtTime(0, c.currentTime + 0.02, 0.04);
    osc.connect(env); env.connect(s);
    osc.start(); osc.stop(c.currentTime + 0.15);

    // Rustle: high-freq noise
    noiseHit(2000, 3, 0.12, 0.12);
  }
}
