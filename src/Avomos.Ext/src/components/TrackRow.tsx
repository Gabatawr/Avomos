import { config } from '../lib/config';
import { trackStore, normStyle } from '../lib/track-store';
import type { Track } from '../lib/types';

interface Props {
  track: Track;
  onShowInfo?: (track: Track) => void;
}

function rowClass(t: Track): string {
  if (t._pending) return 'at-row at-pending';
  if (t._scan === 'waiting') return 'at-row at-waiting';
  if (t._scan === 'paused') return 'at-row at-paused';
  if (t._status === 'new') {
    const ready = (t._filled.model && t._filled.plays && t._filled.style) || t._tried.style >= config.MAX_ATTEMPTS.style;
    return `at-row at-checked-new${ready ? ' at-ready' : ''}`;
  }
  if (t._dirty) return 'at-row at-checked-diff';
  return 'at-row at-checked-ok';
}

function diffText(t: Track): string {
  const db = t._db as Record<string, unknown> | null;
  if (!db || typeof db !== 'object') return '';
  const parts: string[] = [];
  const dbTitle = db.title as string | undefined;
  if (dbTitle !== t.title) parts.push(`title: ${(dbTitle || '').slice(0, 20)}→${(t.title || '').slice(0, 20)}`);
  const dbModel = db.model as string | undefined;
  if (dbModel !== t.model && t.model) parts.push(`model: ${dbModel || '∅'}→${t.model}`);
  const dbPlays = db.plays as number | undefined;
  if (dbPlays !== t.plays && t.plays) parts.push(`plays: ${dbPlays}→${t.plays}`);
  if (t._filled.style) {
    const dbStyles = normStyle(db.styles);
    if (dbStyles !== normStyle(t.style)) {
      const dbS = (dbStyles || '').slice(0, 20);
      const domS = (t.style || '').split('; ').slice(0, 3).join(';').slice(0, 20);
      parts.push(`styles: ${dbS || '∅'}→${domS || '∅'}`);
    }
  }
  const dbPub = db.isPublic as boolean | undefined;
  if (dbPub !== t.isPublic) parts.push(`public: ${dbPub}→${t.isPublic}`);
  return parts.join(' ');
}

export function TrackRow({ track, onShowInfo }: Props) {
  const dt = diffText(track);
  const playCount = track._filled.plays ? track.plays : null;
  const hasStyles = !!(track.style || '').trim();
  const scanBtn = track._pending ? (
    <span className="at-spinner" />
  ) : track._scan === 'done' ? null : (
    <button
      className="at-scan-btn"
      onClick={() => trackStore.toggleTrackScan(track.originId)}
    >
      {track._scan === 'waiting' ? '⏸' : '▶'}
    </button>
  );

  return (
    <div className={rowClass(track)}>
      <span className={`at-dot ${track._scan === 'waiting' ? 'at-dot-active' : ''}`} />
      <span
        className={`at-model ${track._filled.model ? '' : 'at-model-unk'}`}
        onClick={() => {
          if (track._filled.model && track.model === 'edit') {
            track.model = '';
            track._filled.model = false;
            track._tried.model = 0;
            track._scan = 'waiting';
          } else if (!track._filled.model) {
            track.model = 'edit';
            track._filled.model = true;
            track._scan = 'done';
            track._ready = true;
          }
          trackStore.notify();
        }}
      >{track._filled.model && track.model === 'edit' ? 'edit' : track._filled.model ? track.model : '?'}</span>
      <span className="at-title">{(track.title || '?').slice(0, 40)}</span>
      <span className="at-plays">
        {playCount !== null && (
          <><span className="at-plays-icon">▶</span>{playCount}</>
        )}
        {track._db && track._filled.plays && track.plays > (track._db.plays as number || 0) && (
          <span className="at-plays-diff">+{track.plays - (track._db.plays as number || 0)}</span>
        )}
      </span>
      <span className={`at-style ${hasStyles ? '' : 'at-style-muted'}`} title={track.style || ''}>
        s
      </span>
      <span className="at-scan">{scanBtn}</span>
      <span
        className={`at-diff at-diff-clickable ${(dt || (track._status === 'new' && (track._filled.model && track._filled.plays && track._filled.style || track._tried.style >= config.MAX_ATTEMPTS.style))) ? '' : 'at-diff-muted'}`}
        title={dt || (track._db ? 'ok' : 'new')}
        onClick={() => onShowInfo?.(track)}
      >
        {track._db ? 'd' : 'n'}
      </span>
    </div>
  );
}
