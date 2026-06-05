import { useState } from 'react';
import { createPortal } from 'react-dom';
import type { BufferItem } from '../lib/types';

interface Props {
  buffer: BufferItem[];
  onRemove: (originId: string) => void;
  onSemanticSearch: (originId: string, title: string) => void;
  chatPanelW?: number;
  outlierTrackId?: string | null;
  canCreate?: boolean;
}

export function BufferList({ buffer, onRemove, onSemanticSearch, chatPanelW = 400, outlierTrackId, canCreate }: Props) {
  const [modalLyrics, setModalLyrics] = useState<{ title: string; content: string } | null>(null);

  if (!buffer.length) return null;

  return (
    <>
      <div className="ac-buffer">
        {buffer.map(b => {
          const hasStyle = !!(b.style || '').trim();
          const hasContent = !!(b.lyrics || '').trim();
          return (
            <div key={b._originId} className={`ac-buf-item${b._originId === outlierTrackId ? (canCreate ? ' ac-buf-low' : ' ac-buf-outlier') : ''}`}>
              <button
                className="ac-buf-search" title="Find similar"
                onClick={() => onSemanticSearch(b._originId, b._title)}
              >✦</button>
              <span className="ac-buf-model">{b.model || '?'}</span>
              <span className="ac-buf-title">{(b._title || '?').slice(0, 40)}</span>
              <span className="ac-buf-plays">
                {b.plays ? <><span className="ac-buf-plays-icon">▶</span>{b.plays}</> : ''}
              </span>
              <span className={`at-style ${hasStyle ? '' : 'at-style-muted'}`} title={b.style || ''}>s</span>
              <span className="at-scan">
                <button className="at-scan-btn" title="Remove" onClick={() => onRemove(b._originId)}>✕</button>
              </span>
              <span
                className={`at-style ${hasContent ? 'at-style-clickable' : 'at-style-muted'}`}
                onClick={() => hasContent && setModalLyrics({ title: b._title || 'Track', content: b.lyrics || '' })}
              >l</span>
            </div>
          );
        })}
      </div>
      {modalLyrics && createPortal(
        <div className="ac-modal-overlay" onClick={() => setModalLyrics(null)}>
          <div className="ac-modal-box" onClick={e => e.stopPropagation()} style={{ right: chatPanelW }}>
            <div className="ac-modal-header">
              <span>{modalLyrics.title}</span>
              <button className="ac-modal-close" onClick={() => setModalLyrics(null)}>✕</button>
            </div>
            <div className="ac-modal-body">{modalLyrics.content}</div>
          </div>
        </div>,
        document.body
      )}
    </>
  );
}
