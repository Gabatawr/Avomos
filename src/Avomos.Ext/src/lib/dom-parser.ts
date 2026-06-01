import { config } from './config';

const $ = (sel: string, ctx?: Element): Element | null =>
  (ctx || document).querySelector(sel);

const $$ = (sel: string, ctx?: Element): Element[] =>
  [...(ctx || document).querySelectorAll(sel)];

function findLyricsFromPage(): string {
  var btn = document.querySelector('button[title*="Copy lyrics" i], button[aria-label*="Copy lyrics" i]');
  if (btn && btn.parentElement) {
    var prev = btn.previousElementSibling;
    if (prev) {
      var t = prev.textContent?.trim() || '';
      if (t.length > 50) return t;
    }
    var pp = btn.parentElement.querySelector('div, pre');
    if (pp) {
      var t2 = pp.textContent?.trim() || '';
      if (t2.length > 50) return t2;
    }
  }

  var all = document.querySelectorAll('div, pre');
  var best = '';
  for (var i = 0; i < all.length; i++) {
    var t = all[i].textContent?.trim() || '';
    if (t.length > 80 && /\[(Intro|Verse|Chorus|Bridge|Outro|Instrumental|Pre-)/i.test(t)) {
      if (t.length > best.length) best = t;
    }
  }
  return best;
}

export const domParser = {
  getOriginId(el: Element): string | null {
    const href = el.matches('a[href]')
      ? el.getAttribute('href')
      : el.querySelector('a[href*="/song/"]')?.getAttribute('href');
    if (href) {
      const m = href.match(/\/(?:song|track|clip)s?\/([a-f0-9-]+)/i);
      if (m?.[1]) return m[1];
    }
    const d = (el as HTMLElement).dataset.trackId || (el as HTMLElement).dataset.clipId || el.getAttribute('data-id');
    if (d && /^[a-f0-9-]+$/i.test(d)) return d;
    return null;
  },

  getTitle(el: Element): string {
    const sel = 'h1, h2, h3, h4, [data-testid*="title"], [data-testid*="name"], [class*="title"], [class*="name"], a[href*="/song/"] strong, a[href*="/song/"], [class*="track"] a, strong';
    const n = el.querySelector(sel);
    let t = n?.textContent?.trim()?.slice(0, 200) || '';
    if (!t) {
      const links = el.querySelectorAll('a[href*="/song/"]');
      for (const a of links) { t = a.textContent?.trim() || ''; if (t) break; }
    }
    return t;
  },

  getPlays(el: Element): number {
    const txt = el.textContent?.replace(/\s+/g, ' ') || '';
    let m = txt.match(/(\d[\d,.]*\d)\s*(?:plays?|listens?|прослуш|play)/i);
    if (m) return parseInt(m[1].replace(/[,.]/g, ''), 10) || 0;
    m = txt.match(/^(\d+[KMB]?)$/m);
    if (m) {
      const v = m[1];
      if (v.endsWith('K')) return Math.round(parseFloat(v) * 1000);
      if (v.endsWith('M')) return Math.round(parseFloat(v) * 1e6);
      if (v.endsWith('B')) return Math.round(parseFloat(v) * 1e9);
      return parseInt(v) || 0;
    }
    return 0;
  },

  getStyles(el: Element): string[] {
    const tags = $$('[class*="tag"], [class*="style"], [class*="genre"]', el)
      .map(e => e.textContent?.trim()).filter(Boolean) as string[];
    if (tags.length) return tags;
    const txt = el.textContent || '';
    const m = txt.match(/(?:style|genre|стиль|жанр)[:\s]+([^\n.]+)/i);
    if (m) return m[1].split(/[,/]/).map(s => s.trim()).filter(Boolean);
    return [];
  },

  getModel(el: Element): string {
    const root = el.matches('[data-testid="clip-row"], [class*="clip-row"]')
      ? el
      : el.closest('[data-testid="clip-row"], [class*="clip-row"]');
    const spans = root ? $$('span', root) : $$('span', el);
    for (const s of spans) {
      const t = (s.textContent || '').trim();
      if (/^v\d/i.test(t) && t.length < 10) return t;
    }
    return '';
  },

  getLyrics(): string {
    return findLyricsFromPage();
  },

  collectCards(): Element[] {
    const found = new Set<Element>();
    for (const sel of config.CARD_SELECTORS) {
      for (const el of document.querySelectorAll(sel)) {
        if (!(el instanceof HTMLElement)) continue;
        if (el.matches('a[href*="/song/"], a[href*="/track/"], a[href*="/clip/"]')) {
          const c = el.closest('article, li, [class*="card"], [class*="track"], [class*="flex"]') || el.parentElement || el;
          found.add(c);
        } else {
          found.add(el);
        }
      }
    }
    return [...found];
  },
};
