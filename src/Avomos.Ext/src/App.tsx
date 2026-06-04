import { useState, useEffect } from 'react';
import { config } from './lib/config';
import { trackStore } from './lib/track-store';
import { api } from './lib/api';
import { domParser } from './lib/dom-parser';
import { TrackPanel } from './components/TrackPanel';
import { ChatPanel } from './components/ChatPanel';
import { ErrorBoundary } from './components/ErrorBoundary';
import { TrackInfoModal } from './components/TrackInfoModal';
import type { Track } from './lib/types';

function scanDOM() {
  trackStore.fillFromNetwork();
  for (const card of domParser.collectCards()) {
    const title = domParser.getTitle(card);
    if (!title) continue;
    const id = domParser.getOriginId(card);
    if (!id) continue;
    const t = trackStore.ensureTrack(id, title);
    if (!t || t._pending) continue;
    let changed = false;

    if (t.title !== title) {
      t.title = title;
      t._dirty = true;
      changed = true;
    }

    if (t._scan !== 'waiting') {
      if (changed) trackStore.refreshStatus(t);
      continue;
    }

    if (!t._filled.model) {
      const m = domParser.getModel(card);
      if (m) { t.model = m; t._filled.model = true; t._dirty = true; changed = true; }
    }
    if (!t._filled.plays) {
      const p = domParser.getPlays(card);
      if (p) { t.plays = p; t._filled.plays = true; t._dirty = true; changed = true; }
    }
    if (!t._filled.style) {
      const s = domParser.getStyles(card);
      if (s.length) {
        t.style = Array.isArray(s) ? s.join('; ') : s;
        t._filled.style = true;
        t._dirty = true;
        changed = true;
      } else {
        t._tried.style++;
      }
    }
    if (!t._filled.lyrics || t._db) {
      const c = domParser.getLyrics();
      if (c && /\[(Intro|Verse|Chorus|Bridge|Outro|Instrumental)/i.test(c) && c !== t.lyrics) {
        t.lyrics = c;
        t._filled.lyrics = true;
        t._dirty = true;
        changed = true;
      } else if (!t._db) {
        t._tried.lyrics++;
      }
    }

    if (changed) {
      trackStore.refreshStatus(t);
      if (t._status === 'ok') t._dirty = false;
    }

    if ((t._filled.lyrics || t._tried.lyrics >= config.MAX_ATTEMPTS.lyrics) && t._filled.plays &&
        (t._status === 'ok' || t._status === 'diff' || t._status === 'new')) {
      t._ready = true;
      t._scan = 'done';
    }
  }
  for (const t of trackStore.tracks.values()) {
    if (!t._db || !t._dirty) continue;
    trackStore.refreshStatus(t);
    if (t._status === 'ok') t._dirty = false;
  }

  // Try to extract audio URL from page elements
  var audioEls = document.querySelectorAll('audio[src], audio source[src], [data-audio-src], [data-url*=".mp3"]');
  for (var i = 0; i < audioEls.length; i++) {
    var src = (audioEls[i] as HTMLAudioElement).src || (audioEls[i] as HTMLSourceElement).src || (audioEls[i] as HTMLElement).getAttribute('data-audio-src') || (audioEls[i] as HTMLElement).getAttribute('data-url') || '';
    if (!src) continue;
    for (const t of trackStore.tracks.values()) {
      if (t.audioUrl) continue;
      if (src.includes(t.originId) || t.originId.includes(src.slice(-36))) {
        t.audioUrl = src;
        t._dirty = true;
      }
    }
  }

  trackStore.notify();
}

export function App() {
  const [panelHidden, setPanelHidden] = useState(() => localStorage.getItem('avomos_panel_hidden') === '1');
  const [chatHidden, setChatHidden] = useState(() => localStorage.getItem('avomos_chat_hidden') === '1');
  const [detailTrack, setDetailTrack] = useState<Track | null>(null);

  useEffect(() => { localStorage.setItem('avomos_panel_hidden', panelHidden ? '1' : '0'); }, [panelHidden]);
  useEffect(() => { localStorage.setItem('avomos_chat_hidden', chatHidden ? '1' : '0'); }, [chatHidden]);

  useEffect(() => {
    scanDOM();
    const interval = setInterval(scanDOM, 1000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    const handler = (e: Event) => {
      if (e instanceof CustomEvent) {
        trackStore.handleNetwork(e.detail as Record<string, unknown>[]);
      }
    };
    window.addEventListener(config.INTERCEPT_EVENT, handler);
    return () => window.removeEventListener(config.INTERCEPT_EVENT, handler);
  }, []);

  return (
    <ErrorBoundary>
      <TrackPanel hidden={panelHidden} onToggle={() => setPanelHidden(v => !v)} onShowInfo={setDetailTrack} />
      <ChatPanel hidden={chatHidden} onToggle={() => setChatHidden(v => !v)} />
      <TrackInfoModal track={detailTrack} onClose={() => setDetailTrack(null)} />
    </ErrorBoundary>
  );
}
