import { createPortal } from 'react-dom';
import { config } from '../lib/config';
import { normStyle } from '../lib/track-store';
import type { Track } from '../lib/types';

interface Props {
  track: Track | null;
  onClose: () => void;
}

function val(v: unknown): string {
  if (v === null || v === undefined) return '∅';
  if (typeof v === 'boolean') return v ? 'true' : 'false';
  if (typeof v === 'object') return JSON.stringify(v, null, 2);
  return String(v);
}

function kv(k: string, v: unknown) {
  return (
    <div className="ac-msg-extra-row">
      <span className="ac-msg-extra-label">{k}</span>
      <span className="ac-msg-extra-val">{val(v)}</span>
    </div>
  );
}

function section(title: string, children: React.ReactNode) {
  return (
    <>
      <div style={{ fontSize: 11, fontWeight: 700, color: '#f0c15c', margin: '8px 0 4px', borderBottom: '1px solid rgba(255,232,178,0.12)', paddingBottom: 2 }}>{title}</div>
      {children}
    </>
  );
}

/** Split text into lines, preserving empty lines. */
function lines(s: string): string[] {
  return s.split(/\r?\n/);
}

/**
 * Simple line-level diff.
 * Returns an array of { type: 'same'|'add'|'del', text } segments.
 */
function lineDiff(a: string, b: string): { type: 'same' | 'add' | 'del'; text: string }[] {
  const la = lines(a);
  const lb = lines(b);

  // common prefix
  let pref = 0;
  while (pref < la.length && pref < lb.length && la[pref] === lb[pref]) pref++;
  // common suffix
  let suffA = la.length - 1;
  let suffB = lb.length - 1;
  while (suffA >= pref && suffB >= pref && la[suffA] === lb[suffB]) { suffA--; suffB--; }

  const out: { type: 'same' | 'add' | 'del'; text: string }[] = [];

  for (let i = 0; i < pref; i++) out.push({ type: 'same', text: la[i] });
  for (let i = pref; i <= suffA; i++) out.push({ type: 'del', text: la[i] });
  for (let i = pref; i <= suffB; i++) out.push({ type: 'add', text: lb[i] });
  for (let i = suffA + 1; i < la.length; i++) out.push({ type: 'same', text: la[i] });

  return out;
}

type Diff = { field: string; current: string; db: string };

function diffs(track: Track): Diff[] {
  const db = track._db;
  if (!db) return [];
  const out: Diff[] = [];
  const push = (field: string, current: unknown, dbVal: unknown) => {
    const cv = val(current);
    const dv = val(dbVal);
    if (cv !== dv) out.push({ field, current: cv, db: dv });
  };
  push('title', track.title, db.title);
  push('model', track.model, db.model);
  push('plays', track.plays, db.plays);
  push('style', normStyle(track.style), normStyle(db.styles));
  push('isPublic', track.isPublic, db.isPublic);
  push('lyrics', track.lyrics, db.lyrics);
  push('imageUrl', track.imageUrl, db.imageUrl);
  return out;
}

function diffRow(label: string, children: React.ReactNode) {
  return (
    <div className="ac-msg-extra-row">
      <span className="ac-msg-extra-label" style={{ color: '#ff9800' }}>{label}</span>
      <span className="ac-msg-extra-val" style={{ display: 'flex', flexDirection: 'column', gap: 1, maxHeight: 300, overflowY: 'auto' }}>
        {children}
      </span>
    </div>
  );
}

export function TrackInfoModal({ track, onClose }: Props) {
  if (!track) return null;

  const db = track._db;
  const panelW = parseInt(localStorage.getItem('avomos_w') || '', 10) || config.PANEL_DEF_W;
  const diffList = diffs(track);

  return createPortal(
    <div className="ac-modal-overlay" onClick={onClose}>
      <div className="at-modal-box at-modal-box-right" onClick={e => e.stopPropagation()}
        style={{ left: panelW, top: 0, bottom: 0, width: 360 }}
      >
        <div className="ac-modal-header">
          <span>TRACK INFO</span>
          <button className="ac-modal-close" onClick={onClose}>✕</button>
        </div>
        <div className="ac-modal-body at-modal-body">
          {db ? section('DIFFS', diffList.length === 0 ? (
            <div style={{ fontSize: 14, color: '#4caf50', textAlign: 'center', padding: '6px 0' }}>✓</div>
          ) : diffList.map(d => (
            d.field === 'lyrics' ? (
              diffRow('lyrics', lineDiff(d.db, d.current).map((seg, i) => {
                const color = seg.type === 'add' ? '#64b5f6' : seg.type === 'del' ? '#e57373' : 'rgba(255,235,190,0.85)';
                const prefix = seg.type === 'add' ? '+ ' : seg.type === 'del' ? '- ' : '  ';
                return (
                  <div key={i} style={{ color, fontSize: 10, lineHeight: 1.4, whiteSpace: 'pre-wrap', fontFamily: 'monospace' }}>
                    {prefix}{seg.text || ' '}
                  </div>
                );
              }))
            ) : (
              <div key={d.field} className="ac-msg-extra-row">
                <span className="ac-msg-extra-label" style={{ color: '#ff9800' }}>{d.field}</span>
                <span className="ac-msg-extra-val" style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
                  <span style={{ color: '#64b5f6', fontSize: 10 }}>{d.current}</span>
                  <span style={{ color: '#e57373', fontSize: 10 }}>{d.db}</span>
                </span>
              </div>
            )
          ))) : null}

          {section('SCAN', <>
            {kv('_status', track._status)}
            {kv('_scan', track._scan)}
            {kv('_ready', track._filled.lyrics && track._filled.plays ? 'yes' : 'no')}
            <div className="ac-msg-extra-row">
              <span className="ac-msg-extra-label">_filled</span>
              <span className="ac-msg-extra-val" style={{ fontSize: 10, display: 'flex', gap: 6 }}>
                <span style={{ color: track._filled.lyrics ? '#4caf50' : '#888' }}>lyrics{track._filled.lyrics ? '✓' : '✗'}</span>
                <span style={{ color: track._filled.plays ? '#4caf50' : '#888' }}>plays{track._filled.plays ? '✓' : '✗'}</span>
                <span style={{ color: track._filled.model ? '#4caf50' : '#888' }}>model{track._filled.model ? '✓' : '✗'}</span>
                <span style={{ color: track._filled.style ? '#4caf50' : '#888' }}>style{track._filled.style ? '✓' : '✗'}</span>
              </span>
            </div>
            <div className="ac-msg-extra-row">
              <span className="ac-msg-extra-label">_tried</span>
              <span className="ac-msg-extra-val" style={{ fontSize: 10, display: 'flex', gap: 6 }}>
                <span>lyrics:{track._tried.lyrics}</span>
                <span>plays:{track._tried.plays}</span>
                <span>model:{track._tried.model}</span>
                <span>style:{track._tried.style}</span>
              </span>
            </div>
          </>)}

          {section('FIELDS', <>
            {kv('originId', track.originId)}
            {kv('title', track.title)}
            {track.lyrics ? (
              <div className="ac-msg-extra-row">
                <span className="ac-msg-extra-label">lyrics</span>
                <span className="ac-msg-extra-val" style={{ fontSize: 11, lineHeight: 1.5, color: 'rgba(255,235,190,0.85)', whiteSpace: 'pre-wrap', maxHeight: 300, overflowY: 'auto' }}>{track.lyrics}</span>
              </div>
            ) : null}
            {kv('model', track.model || '∅')}
            {kv('plays', track.plays)}
            {kv('style', track.style || '∅')}
            {kv('isPublic', track.isPublic)}
            {kv('audioUrl', track.audioUrl || '∅')}
            {kv('imageUrl', track.imageUrl || (track._raw?.image_url as string) || (track._raw?.cover_image_url as string) || '∅')}
            {kv('createdAt', (track._raw?.created_at as string) || (track._raw?.createdAt as string) || '∅')}
          </>)}

          {db ? section('DB (Qdrant)', <>
            {kv('originId', (db as Record<string, unknown>).originId)}
            {kv('title', (db as Record<string, unknown>).title)}
            {track._db?.lyrics ? (
              <div className="ac-msg-extra-row">
                <span className="ac-msg-extra-label">lyrics</span>
                <span className="ac-msg-extra-val" style={{ fontSize: 11, lineHeight: 1.5, color: 'rgba(255,235,190,0.85)', whiteSpace: 'pre-wrap', maxHeight: 300, overflowY: 'auto' }}>{(db as Record<string, unknown>).lyrics as string}</span>
              </div>
            ) : kv('lyrics', '∅')}
            {kv('model', (db as Record<string, unknown>).model || '∅')}
            {kv('plays', (db as Record<string, unknown>).plays)}
            {kv('styles', (db as Record<string, unknown>).styles || '∅')}
            {kv('isPublic', (db as Record<string, unknown>).isPublic)}
            {kv('url', (db as Record<string, unknown>).url || '∅')}
            {kv('imageUrl', (db as Record<string, unknown>).imageUrl || '∅')}
            {kv('createdAt', (db as Record<string, unknown>).createdAt || '∅')}
          </>) : null}
        </div>
      </div>
    </div>,
    document.body
  );
}
