import { useState, useEffect, useCallback } from 'react';

export type KeyAction =
  | { type: 'move'; dx: number; dy: number }
  | { type: 'interact' }
  | { type: 'inventory' }
  | { type: 'character' }
  | { type: 'quest' }
  | { type: 'help' }
  | { type: 'escape' }
  | { type: 'pass' }
  | { type: 'portal' }
  | { type: 'harvest' }
  | { type: 'autofarm' }
  | { type: 'storage' }
  | { type: 'companions' }
  | { type: 'crafting' }
  | { type: 'navigate' }
  | { type: 'autoquest' }
  | null;

export function useKeyboard(): KeyAction {
  const [action, setAction] = useState<KeyAction>(null);

  const clearAction = useCallback(() => setAction(null), []);

  useEffect(() => {
    let debounceTimer: ReturnType<typeof setTimeout> | null = null;

    const handleKeyDown = (e: KeyboardEvent) => {
      let next: KeyAction = null;

      switch (e.key) {
        case 'ArrowUp':
        case 'w':
        case 'W':
          next = { type: 'move', dx: 0, dy: -1 };
          break;
        case 'ArrowDown':
        case 's':
        case 'S':
          next = { type: 'move', dx: 0, dy: 1 };
          break;
        case 'ArrowLeft':
        case 'a':
        case 'A':
          next = { type: 'move', dx: -1, dy: 0 };
          break;
        case 'ArrowRight':
        case 'd':
        case 'D':
          next = { type: 'move', dx: 1, dy: 0 };
          break;
        case 'Enter':
          next = { type: 'interact' };
          break;
        case 'i':
        case 'I':
          next = { type: 'inventory' };
          break;
        case 'c':
        case 'C':
          next = { type: 'character' };
          break;
        case 'q':
        case 'Q':
          next = { type: 'quest' };
          break;
        case ' ':
          next = { type: 'pass' };
          e.preventDefault();
          break;
        case '?':
        case 'h':
        case 'H':
          next = { type: 'help' };
          break;
        case 'p':
        case 'P':
          next = { type: 'portal' };
          break;
        case 'e':
        case 'E':
          next = { type: 'harvest' };
          break;
        case 'f':
        case 'F':
          next = { type: 'autofarm' };
          break;
        case 'v':
        case 'V':
          next = { type: 'storage' };
          break;
        case 'b':
        case 'B':
          next = { type: 'companions' };
          break;
        case 'r':
        case 'R':
          next = { type: 'crafting' };
          break;
        case 'n':
        case 'N':
          next = { type: 'navigate' };
          break;
        case 'l':
        case 'L':
          next = { type: 'autoquest' };
          break;
        case 'Escape':
          next = { type: 'escape' };
          break;
        default:
          return;
      }

      if (next) {
        e.preventDefault();
        setAction(next);
        if (debounceTimer !== null) clearTimeout(debounceTimer);
        debounceTimer = setTimeout(clearAction, 150);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      if (debounceTimer !== null) clearTimeout(debounceTimer);
    };
  }, [clearAction]);

  return action;
}
