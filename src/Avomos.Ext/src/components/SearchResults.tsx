import { useState, useCallback, useEffect, useRef, forwardRef, useImperativeHandle } from 'react';
import { createPortal } from 'react-dom';
import { api } from '../lib/api';
import type { BufferItem, SearchHit } from '../lib/types';

interface Props {
  buffer: BufferItem[];
  onAddToBuffer: (originId: string, title: string, data?: { model?: string; style?: string; plays?: number; lyrics?: string; isPublic?: boolean }) => void;
  chatPanelW?: number;
}

export interface SearchResultsHandle {
  externalSearch: (query: string) => void;
  clearSearch: () => void;
}

function loadNum(key: string, def: number): number {
  try { const v = localStorage.getItem(key); return v ? parseFloat(v) : def; } catch { return def; }
}
function saveNum(key: string, v: number) {
  try { localStorage.setItem(key, String(v)); } catch { /* ignore */ }
}

const CACHE_TTL = 3600_000;

interface CacheEntry { hits: SearchHit[]; time: number }

export const SearchResults = forwardRef<SearchResultsHandle, Props>(({ buffer, onAddToBuffer, chatPanelW = 400 }, ref) => {
  const [query, setQuery] = useState('');
  const [semanticLabel, setSemanticLabel] = useState('');
  const [results, setResults] = useState<SearchHit[]>([]);
  const [limit, setLimit] = useState(() => loadNum('avomos_search_limit', 3));
  const [weight, setWeight] = useState(() => loadNum('avomos_search_weight', 0.7));
  const [lyricsModal, setLyricsModal] = useState<{ title: string; content: string } | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cache = useRef<Record<string, CacheEntry>>({});
  const limitRef = useRef(limit);
  const weightRef = useRef(weight);
  const lastQueryRef = useRef('');

  limitRef.current = limit;
  weightRef.current = weight;

  const doSearch = useCallback(async (q: string) => {
    lastQueryRef.current = q;
    const key = `${q}|${limitRef.current}|${weightRef.current}`;
    const cached = cache.current[key];
    if (cached && Date.now() - cached.time < CACHE_TTL) { setResults(cached.hits); return; }
    const hits = await api.search(q, limitRef.current, weightRef.current);
    hits.sort((a, b) => (b.score ?? 0) - (a.score ?? 0));
    cache.current[key] = { hits, time: Date.now() };
    setResults(hits);
  }, []);

  useImperativeHandle(ref, () => ({
    externalSearch(q: string) {
      setQuery('');
      setSemanticLabel(q);
      doSearch(q);
    },
    clearSearch() {
      setQuery('');
      setSemanticLabel('');
      setResults([]);
      cache.current = {};
    },
  }), [doSearch]);

  const handleInput = useCallback((value: string) => {
    setSemanticLabel('');
    const q = value.trim();
    setQuery(value);
    if (!q) { setResults([]); return; }
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => doSearch(q), 300);
  }, [doSearch]);

  const clear = useCallback(() => {
    setQuery('');
    setSemanticLabel('');
    setResults([]);
    inputRef.current?.focus();
  }, []);

  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);

  const redoSearch = useCallback(() => {
    const q = lastQueryRef.current;
    if (q) doSearch(q);
  }, [doSearch]);

  return (
    <div className="ac-search">
      <div className="ac-search-row">
        <input
          ref={inputRef}
          className={`ac-input${semanticLabel ? ' ac-input-semantic' : ''}`}
          placeholder={semanticLabel ? `✦ ${semanticLabel}` : 'Search tracks...'}
          value={query}
          onChange={e => handleInput(e.target.value)}
        />
        {(query || semanticLabel || results.length > 0) && (
          <button className="ac-search-clear" onClick={clear}>✕</button>
        )}
      </div>
      <div className="ac-opts">
        <span>Limit</span>
        <input
          type="range" className="ac-range" min={3} max={10} step={1}
          value={limit}
          onChange={e => {
            const v = +e.target.value;
            setLimit(v);
            saveNum('avomos_search_limit', v);
            redoSearch();
          }}
        />
        <span className="ac-opts-val">{limit}</span>
        <span className="ac-opts-sep" />
        <span>Lyrics</span>
        <input
          type="range" className="ac-range" min={0} max={1} step={0.05}
          value={weight}
          onChange={e => {
            const v = +e.target.value;
            setWeight(v);
            saveNum('avomos_search_weight', v);
            redoSearch();
          }}
        />
        <span>Styles</span>
      </div>
      <div className="ac-results">
        {results.map(t => {
          const inBuf = buffer.some(b => b._originId === t.originId);
          const hasStyle = !!(t.styles || '').trim();
          const hasContent = !!(t.lyrics || '').trim();
          return (
            <div
              key={t.originId}
              className={`ac-sr-item${inBuf ? ' ac-sr-selected' : ''}`}
              onClick={() => !inBuf && onAddToBuffer(t.originId, t.title, {
                model: t.model,
                style: t.styles,
                plays: t.plays,
                lyrics: t.lyrics,
                isPublic: t.isPublic,
              })}
            >
              <span className="ac-sr-plus">{inBuf ? '✓' : '+'}</span>
              <span className="ac-buf-model" title={t.model || ''}>{t.model || '?'}</span>
              <span className="ac-buf-title">{(t.title || '?').slice(0, 40)}</span>
              <span className="ac-buf-plays">
                {t.plays ? <><span className="ac-buf-plays-icon">▶</span>{t.plays}</> : ''}
              </span>
              <span className={`at-style ${hasStyle ? '' : 'at-style-muted'}`} title={t.styles || ''}>
                s
              </span>
              <span className="ac-sr-spacer">{t.score != null ? `${Math.round(t.score * 100)}%` : ''}</span>
              <span
                className={`at-style ${hasContent ? 'at-style-clickable' : 'at-style-muted'}`}
                  onClick={e => {
                    e.stopPropagation();
                    if (hasContent) setLyricsModal({ title: t.title || t._title || 'Track', content: t.lyrics || '' });
                  }}
              >l</span>
            </div>
          );
        })}
      </div>
      {lyricsModal && createPortal(
        <div className="ac-modal-overlay" onClick={() => setLyricsModal(null)}>
          <div className="ac-modal-box" onClick={e => e.stopPropagation()} style={{ right: chatPanelW }}>
            <div className="ac-modal-header">
              <span>{lyricsModal.title}</span>
              <button className="ac-modal-close" onClick={() => setLyricsModal(null)}>✕</button>
            </div>
            <div className="ac-modal-body">{lyricsModal.content}</div>
          </div>
        </div>,
        document.body
      )}
    </div>
  );
});
