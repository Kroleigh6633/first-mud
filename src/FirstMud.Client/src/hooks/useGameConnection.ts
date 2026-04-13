/**
 * useGameConnection — backwards-compatible shim.
 *
 * The implementation has been split into:
 *   - useHubConnection (game-agnostic SignalR transport)
 *   - useGameState    (game-specific event handlers + React state slices)
 *
 * This file exists so existing imports (`App.tsx`) keep working without
 * modification. New code should import `useGameState` directly.
 */
import { useGameState, type GameStateResult } from './useGameState';

export type GameConnectionResult = GameStateResult;

export function useGameConnection(): GameConnectionResult {
  return useGameState();
}
