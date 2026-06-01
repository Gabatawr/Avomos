import { useState, useEffect } from 'react';
import { trackStore } from '../lib/track-store';
import type { Track } from '../lib/types';

function useSubscription<T>(get: () => T): T {
  const [, setTick] = useState(0);
  useEffect(() => trackStore.subscribe(() => setTick(t => t + 1)), []);
  return get();
}

export function useUpdateTracks(): Track[] {
  return useSubscription(() => trackStore.getUpdates());
}

export function useHasUpdates(): boolean {
  return useSubscription(() => trackStore.hasUpdates());
}

export function useImportableTracks(): Track[] {
  return useSubscription(() => trackStore.getImportable());
}

export function useHasImports(): boolean {
  return useSubscription(() => trackStore.hasImports());
}
