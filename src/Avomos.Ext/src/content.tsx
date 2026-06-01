import { createRoot } from 'react-dom/client';
import { App } from './App';
import { trackStore } from './lib/track-store';
import './styles/global.scss';

function getWid(): string | null {
  return window.location.search.match(/[?&]wid=([^&]+)/)?.[1] || null;
}

function injectInterceptor() {
  if (document.getElementById('avomos-interceptor')) return;
  const s = document.createElement('script');
  s.id = 'avomos-interceptor';
  s.src = chrome.runtime.getURL('page-interceptor.js');
  (document.head || document.documentElement).appendChild(s);
}

let root: ReturnType<typeof createRoot> | null = null;

function boot() {
  if (document.getElementById('avomos-extension')) return;

  injectInterceptor();

  const container = document.createElement('div');
  container.id = 'avomos-extension';
  document.body.appendChild(container);

  root = createRoot(container);
  root.render(<App />);
}

function unboot() {
  const existing = document.getElementById('avomos-extension');
  if (existing) {
    root?.unmount();
    root = null;
    existing.remove();
  }
  trackStore.resetAll();
}

let currentWid = getWid();

setInterval(() => {
  const wid = getWid();
  if (wid && wid !== currentWid) {
    currentWid = wid;
    unboot();
    boot();
  }
}, 500);

const observer = new MutationObserver(() => {
  if (!document.getElementById('avomos-extension')) {
    root?.unmount();
    root = null;
    boot();
  }
});
observer.observe(document.body, { childList: true });

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot);
} else {
  boot();
}
