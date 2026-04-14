import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AudioEngine } from './audio/AudioEngine'

// ── Bootstrap user-gesture AudioContext resume ─────────────────────────────
// Browsers block AudioContext creation/resume until a user gesture. We attach
// a one-shot handler at the very top of the app so the *first* click/keydown
// anywhere — including interactions with the splash/login screen before the
// useAudio hook mounts — will wake the audio context. Safe to call repeatedly
// (resume() is idempotent), and once fired all subsequent handlers detach.
function bootstrapAudioGesture(): void {
  const wake = () => {
    try {
      const engine = AudioEngine.getInstance();
      console.log('[audio] bootstrap-gesture fired, ctxState=', engine.ctx.state);
      void engine.resume();
    } catch (err) {
      console.warn('[audio] bootstrap-gesture failed', err);
    }
    window.removeEventListener('click',       wake, true);
    window.removeEventListener('keydown',     wake, true);
    window.removeEventListener('pointerdown', wake, true);
    window.removeEventListener('touchstart',  wake, true);
  };
  window.addEventListener('click',       wake, true);
  window.addEventListener('keydown',     wake, true);
  window.addEventListener('pointerdown', wake, true);
  window.addEventListener('touchstart',  wake, true);
  console.log('[audio] bootstrap-gesture listeners attached');
}

bootstrapAudioGesture();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
