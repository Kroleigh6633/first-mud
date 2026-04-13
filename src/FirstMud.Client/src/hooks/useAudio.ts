/**
 * useAudio — React hook that manages the procedural audio engine.
 *
 * Initialises AudioEngine on the first user interaction (browser policy).
 * Crossfades between biome music when the biome changes (2-second fade).
 * Exposes `playSound` for one-shot SFX and volume controls.
 */

import { useState, useEffect, useRef, useCallback } from 'react';
import { AudioEngine } from '../audio/AudioEngine';
import { startBiomeMusic, resolveBiomeKey, type BiomeMusicHandle, type BiomeKey } from '../audio/BiomeMusic';
import { SoundEffects } from '../audio/SoundEffects';

export type SoundName =
  | 'strike'
  | 'crit'
  | 'miss'
  | 'dodge'
  | 'levelUp'
  | 'questComplete'
  | 'loot'
  | 'rareLoot'
  | 'portal'
  | 'heal'
  | 'defeat'
  | 'capture'
  | 'harvest';

export interface AudioControls {
  playSound: (name: SoundName) => void;
  setMusicVolume: (v: number) => void;
  setSfxVolume: (v: number) => void;
  musicVolume: number;
  sfxVolume: number;
  isMuted: boolean;
  toggleMute: () => void;
}

const CROSSFADE_DURATION = 2; // seconds

export function useAudio(
  biomeType: string,
  inCombat: boolean,
  atHomestead: boolean,
): AudioControls {
  const [initialised, setInitialised] = useState(false);
  const [isMuted, setIsMuted] = useState(false);
  const [musicVolume, setMusicVolumeState] = useState(0.3);
  const [sfxVolume, setSfxVolumeState]   = useState(0.5);

  const currentHandle  = useRef<BiomeMusicHandle | null>(null);
  const currentBiomeKey = useRef<BiomeKey | null>(null);
  const initialisedRef = useRef(false);

  // ── Initialise on first user interaction ─────────────────────────────────
  useEffect(() => {
    if (initialisedRef.current) return;

    const init = async () => {
      if (initialisedRef.current) return;
      initialisedRef.current = true;
      const engine = AudioEngine.getInstance();
      await engine.resume();
      setInitialised(true);
    };

    const handleInteraction = () => {
      void init();
      // Remove after first interaction
      window.removeEventListener('keydown', handleInteraction);
      window.removeEventListener('click',   handleInteraction);
      window.removeEventListener('pointerdown', handleInteraction);
    };

    window.addEventListener('keydown',     handleInteraction, { once: true });
    window.addEventListener('click',       handleInteraction, { once: true });
    window.addEventListener('pointerdown', handleInteraction, { once: true });

    return () => {
      window.removeEventListener('keydown',     handleInteraction);
      window.removeEventListener('click',       handleInteraction);
      window.removeEventListener('pointerdown', handleInteraction);
    };
  }, []);

  // ── Switch biome music on change, with 2-second crossfade ────────────────
  useEffect(() => {
    if (!initialised) return;

    const targetKey = resolveBiomeKey(biomeType, inCombat, atHomestead);
    if (targetKey === currentBiomeKey.current) return;

    // Fade out old track
    if (currentHandle.current) {
      const oldHandle = currentHandle.current;
      oldHandle.fadeOut(CROSSFADE_DURATION);
      setTimeout(() => oldHandle.stop(), CROSSFADE_DURATION * 1000 + 200);
    }

    // Start new track
    const newHandle = startBiomeMusic(targetKey);
    currentHandle.current   = newHandle;
    currentBiomeKey.current = targetKey;
  }, [initialised, biomeType, inCombat, atHomestead]);

  // ── Stop music on unmount ─────────────────────────────────────────────────
  useEffect(() => {
    return () => {
      if (currentHandle.current) {
        currentHandle.current.stop();
        currentHandle.current = null;
      }
    };
  }, []);

  // ── Sound playback ────────────────────────────────────────────────────────
  const playSound = useCallback((name: SoundName) => {
    if (!initialisedRef.current) return;
    switch (name) {
      case 'strike':       SoundEffects.strike();       break;
      case 'crit':         SoundEffects.criticalHit();  break;
      case 'miss':         SoundEffects.miss();         break;
      case 'dodge':        SoundEffects.dodge();        break;
      case 'levelUp':      SoundEffects.levelUp();      break;
      case 'questComplete': SoundEffects.questComplete(); break;
      case 'loot':         SoundEffects.lootDrop();     break;
      case 'rareLoot':     SoundEffects.rareLoot();     break;
      case 'portal':       SoundEffects.portal();       break;
      case 'heal':         SoundEffects.heal();         break;
      case 'defeat':       SoundEffects.defeat();       break;
      case 'capture':      SoundEffects.capture();      break;
      case 'harvest':      SoundEffects.harvest();      break;
    }
  }, []);

  // ── Volume controls ───────────────────────────────────────────────────────
  const setMusicVolume = useCallback((v: number) => {
    setMusicVolumeState(v);
    if (initialisedRef.current) AudioEngine.getInstance().setMusicVolume(v);
  }, []);

  const setSfxVolume = useCallback((v: number) => {
    setSfxVolumeState(v);
    if (initialisedRef.current) AudioEngine.getInstance().setSfxVolume(v);
  }, []);

  const toggleMute = useCallback(() => {
    if (!initialisedRef.current) return;
    AudioEngine.getInstance().toggleMute();
    setIsMuted(prev => !prev);
  }, []);

  return {
    playSound,
    setMusicVolume,
    setSfxVolume,
    musicVolume,
    sfxVolume,
    isMuted,
    toggleMute,
  };
}
