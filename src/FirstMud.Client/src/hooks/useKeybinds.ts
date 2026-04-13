import { useEffect, useRef } from 'react';

/**
 * useKeybinds — generic keybinding-table → handler dispatch.
 *
 * Consumers pass a list of `KeyBinding` entries. This hook installs a single
 * `keydown` listener on `window` for the lifetime of the hook and dispatches
 * the first binding whose `keys` contains `e.key` and whose modifiers (if any)
 * match. A binding may specify `when` to guard activation at dispatch time.
 *
 * Safeguards (preserved from the previous `useKeyboard` implementation):
 *   - Keypresses while a text `<input>`, `<textarea>`, or `contentEditable`
 *     element is focused are ignored so the terminal shortcuts don't eat
 *     typing in PlayerCreation etc.
 *   - Debounce: after a binding fires, identical re-dispatches within 150 ms
 *     are suppressed. This matches the previous behaviour where the
 *     `KeyAction` state was cleared on a 150 ms timer.
 *   - `e.preventDefault()` is called for every matched binding (Space would
 *     otherwise scroll the page, arrow keys ditto).
 *
 * The hook is intentionally returnless. Consumers that want to render a help
 * overlay from the bindings should keep their own reference to the array they
 * passed in (it's theirs — we don't own it).
 */

export interface KeyBindingModifiers {
  ctrl?: boolean;
  shift?: boolean;
  alt?: boolean;
}

export interface KeyBinding {
  /** Keys that trigger this binding. Case-sensitive — include both 'w' and 'W' for letter keys. */
  keys: string[];
  /** Human-readable label; reserved for future HelpOverlay integration. */
  description?: string;
  /** Called on a matching keypress after modifier/focus/when gates pass. */
  handler: (event: KeyboardEvent) => void;
  /** Required modifier state. Missing entries are treated as "must be false". */
  modifiers?: KeyBindingModifiers;
  /** Optional guard evaluated at dispatch time; return false to suppress. */
  when?: () => boolean;
}

function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
  if (target.isContentEditable) return true;
  return false;
}

function modifiersMatch(e: KeyboardEvent, mods?: KeyBindingModifiers): boolean {
  const want = { ctrl: !!mods?.ctrl, shift: !!mods?.shift, alt: !!mods?.alt };
  return e.ctrlKey === want.ctrl && e.shiftKey === want.shift && e.altKey === want.alt;
}

export function useKeybinds(bindings: KeyBinding[]): void {
  // Keep the latest bindings in a ref so the listener doesn't need to be
  // re-attached on every render when the caller passes a fresh array literal.
  const bindingsRef = useRef(bindings);
  bindingsRef.current = bindings;

  useEffect(() => {
    let debounceUntil = 0;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (isTypingTarget(e.target)) return;

      for (const binding of bindingsRef.current) {
        if (!binding.keys.includes(e.key)) continue;
        if (!modifiersMatch(e, binding.modifiers)) continue;
        if (binding.when && !binding.when()) continue;

        const now = Date.now();
        if (now < debounceUntil) {
          e.preventDefault();
          return;
        }
        debounceUntil = now + 150;
        e.preventDefault();
        binding.handler(e);
        return;
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);
}
