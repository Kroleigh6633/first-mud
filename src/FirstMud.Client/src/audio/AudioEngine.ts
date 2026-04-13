/**
 * AudioEngine — singleton wrapper around the Web Audio API context.
 *
 * Browser policy: AudioContext must be created (or resumed) in response to
 * a user gesture. Call `resume()` from a click/keydown handler before
 * attempting to play anything.
 */

let instance: AudioEngine | null = null;

export class AudioEngine {
  readonly ctx: AudioContext;
  private readonly masterGain: GainNode;
  private readonly musicGain: GainNode;
  private readonly sfxGain: GainNode;

  private _muted = false;
  private _musicVolume = 0.3;
  private _sfxVolume = 0.5;

  private constructor() {
    this.ctx = new AudioContext();
    this.masterGain = this.ctx.createGain();
    this.musicGain  = this.ctx.createGain();
    this.sfxGain    = this.ctx.createGain();

    this.masterGain.gain.value = 1.0;
    this.musicGain.gain.value  = this._musicVolume;
    this.sfxGain.gain.value    = this._sfxVolume;

    this.musicGain.connect(this.masterGain);
    this.sfxGain.connect(this.masterGain);
    this.masterGain.connect(this.ctx.destination);
  }

  static getInstance(): AudioEngine {
    if (!instance) instance = new AudioEngine();
    return instance;
  }

  /** Call this on the first user interaction to un-suspend the context. */
  async resume(): Promise<void> {
    if (this.ctx.state === 'suspended') {
      await this.ctx.resume();
    }
  }

  get musicBus(): GainNode { return this.musicGain; }
  get sfxBus(): GainNode   { return this.sfxGain; }

  setMusicVolume(v: number): void {
    this._musicVolume = Math.max(0, Math.min(1, v));
    if (!this._muted) {
      this.musicGain.gain.setTargetAtTime(this._musicVolume, this.ctx.currentTime, 0.05);
    }
  }

  setSfxVolume(v: number): void {
    this._sfxVolume = Math.max(0, Math.min(1, v));
    if (!this._muted) {
      this.sfxGain.gain.setTargetAtTime(this._sfxVolume, this.ctx.currentTime, 0.05);
    }
  }

  getMusicVolume(): number { return this._musicVolume; }
  getSfxVolume(): number   { return this._sfxVolume; }
  isMuted(): boolean       { return this._muted; }

  mute(): void {
    this._muted = true;
    this.masterGain.gain.setTargetAtTime(0, this.ctx.currentTime, 0.05);
  }

  unmute(): void {
    this._muted = false;
    this.masterGain.gain.setTargetAtTime(1.0, this.ctx.currentTime, 0.05);
  }

  toggleMute(): void {
    if (this._muted) this.unmute();
    else this.mute();
  }

  /** Create a short reverb impulse via a noise-filled ConvolverNode. */
  createReverb(duration = 1.5, decay = 2.0): ConvolverNode {
    const sampleRate = this.ctx.sampleRate;
    const length = Math.floor(sampleRate * duration);
    const impulse = this.ctx.createBuffer(2, length, sampleRate);
    for (let ch = 0; ch < 2; ch++) {
      const channel = impulse.getChannelData(ch);
      for (let i = 0; i < length; i++) {
        channel[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / length, decay);
      }
    }
    const conv = this.ctx.createConvolver();
    conv.buffer = impulse;
    return conv;
  }

  /**
   * Fill a mono AudioBuffer with white noise and return it.
   * Reuse the same buffer across calls for efficiency.
   */
  private _noiseBuffer: AudioBuffer | null = null;
  getNoiseBuffer(): AudioBuffer {
    if (this._noiseBuffer) return this._noiseBuffer;
    const length = this.ctx.sampleRate * 2; // 2 s loopable
    const buf = this.ctx.createBuffer(1, length, this.ctx.sampleRate);
    const data = buf.getChannelData(0);
    for (let i = 0; i < length; i++) data[i] = Math.random() * 2 - 1;
    this._noiseBuffer = buf;
    return buf;
  }
}
