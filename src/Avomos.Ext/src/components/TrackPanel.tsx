import { useCallback, useState } from 'react';
import { useUpdateTracks, useHasUpdates, useImportableTracks, useHasImports } from '../hooks/useTrackStore';
import { useResize } from '../hooks/useResize';
import { api } from '../lib/api';
import { trackStore } from '../lib/track-store';
import { TrackRow } from './TrackRow';
import type { Track } from '../lib/types';

interface Props {
  hidden: boolean;
  onToggle: () => void;
  onShowInfo?: (track: Track) => void;
}

export function TrackPanel({ hidden, onToggle, onShowInfo }: Props) {
  const [tab, setTab] = useState<'updates' | 'import'>('updates');
  const updates = useUpdateTracks();
  const hasUpdates = useHasUpdates();
  const imports = useImportableTracks();
  const hasImports = useHasImports();
  const [panelW, onResizeMouseDown] = useResize('avomos_w', 'left');

  const handleReset = useCallback(() => {
    trackStore.resetAll();
  }, []);

  const handleUpdate = useCallback(async () => {
    const btn = document.getElementById('avomos-btn-update') as HTMLButtonElement | null;
    if (btn) { btn.disabled = true; btn.textContent = 'Updating...'; }
    const r = await api.updateAll();
    if (r.ok && btn) {
      btn.textContent = `Updated ${r.data?.updated ?? '?'}`;
    } else if (btn) {
      btn.textContent = 'Error';
    }
  }, []);

  const handleImport = useCallback(async () => {
    const btn = document.getElementById('avomos-btn-import') as HTMLButtonElement | null;
    if (btn) { btn.disabled = true; btn.textContent = 'Importing...'; }
    const r = await api.importAll();
    if (r.ok && btn) {
      const total = (r.data?.imported ?? 0) + (r.data?.updated ?? 0);
      btn.textContent = `Imported ${total}`;
      setTimeout(() => { if (btn) { btn.disabled = false; btn.textContent = 'Import'; } }, 2000);
    } else if (btn) {
      btn.textContent = 'Error';
    }
  }, []);

  return (
    <>
      <button
        id="avomos-toggle-left"
        className="ap-toggle"
        onClick={onToggle}
      >
        ☰
      </button>

      <div
        id="avomos-root"
        style={{
          width: `${panelW}px`,
          transform: hidden ? 'translateX(-102%)' : '',
        }}
      >
        <div
          id="avomos-resize"
          onMouseDown={onResizeMouseDown}
        />
        <div className="ap-panel">
          <div className="ap-tabs">
            <span
              className={`ap-tab${tab === 'updates' ? ' ap-tab-active' : ''}`}
              onClick={() => setTab('updates')}
            >
              UPDATES{updates.length > 0 ? <span className="ap-tab-badge">{updates.length}</span> : null}
            </span>
            <span
              className={`ap-tab${tab === 'import' ? ' ap-tab-active' : ''}`}
              onClick={() => setTab('import')}
            >
              IMPORT{imports.length > 0 ? <span className="ap-tab-badge">{imports.length}</span> : null}
            </span>
          </div>
          {tab === 'updates' ? (
            <>
              <div className="ap-list">
                {updates.length === 0 ? (
                  <div className="ap-empty">No updates</div>
                ) : updates.map(t => (
                  <TrackRow key={t.originId} track={t} onShowInfo={onShowInfo} />
                ))}
              </div>
              <div className="ap-controls">
                <button className="ap-ctl-btn ap-ctl-reset" onClick={handleReset}>
                  Reset
                </button>
                <button
                  id="avomos-btn-update"
                  className="ap-ctl-btn ap-ctl-action"
                  onClick={handleUpdate}
                  disabled={!hasUpdates}
                >
                  Update
                </button>
              </div>
            </>
          ) : (
            <>
              <div className="ap-list">
                {imports.length === 0 ? (
                  <div className="ap-empty">No tracks to import</div>
                ) : imports.map(t => (
                  <TrackRow key={t.originId} track={t} onShowInfo={onShowInfo} />
                ))}
              </div>
              <div className="ap-controls">
                <button className="ap-ctl-btn ap-ctl-reset" onClick={handleReset}>
                  Reset
                </button>
                <button
                  id="avomos-btn-import"
                  className="ap-ctl-btn ap-ctl-action"
                  onClick={handleImport}
                  disabled={!hasImports}
                >
                  Import
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </>
  );
}
