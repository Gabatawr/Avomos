import { useState, useCallback, useRef } from 'react';
import { config } from '../lib/config';

export function useResize(
  storageKey: string,
  side: 'left' | 'right',
): [number, (e: React.MouseEvent) => void] {
  const [width, setWidth] = useState(() => {
    const saved = localStorage.getItem(storageKey);
    return saved ? parseInt(saved, 10) : config.PANEL_DEF_W;
  });

  const widthRef = useRef(width);
  widthRef.current = width;

  const onMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    const startX = e.clientX;
    const startW = widthRef.current;

    const onMouseMove = (ev: MouseEvent) => {
      const delta = side === 'left'
        ? ev.clientX - startX
        : startX - ev.clientX;
      const newW = Math.max(config.PANEL_MIN_W, startW + delta);
      widthRef.current = newW;
      setWidth(newW);
    };

    const onMouseUp = () => {
      localStorage.setItem(storageKey, String(widthRef.current));
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
    };

    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
  }, [storageKey, side]);

  return [width, onMouseDown];
}
