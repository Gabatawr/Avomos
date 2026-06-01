import { config } from './config';
import type { Track } from './types';

type Listener = () => void;

export function normStyle(s: unknown): string {
  if (!s) return '';
  const a = typeof s === 'string' ? s.split(/;\s*/) : Array.isArray(s) ? s : [];
  return a.map((x: string) => x.trim().replace(/\.+$/, '')).filter(Boolean).sort().join(';');
}

function createTrack(id: string, title: string): Track {
  return {
    originId: id,
    title: title || '',
    model: '',
    plays: 0,
    style: '',
    audioUrl: '',
    isPublic: true,
    lyrics: '',
    imageUrl: '',
    _filled: { model: false, plays: false, style: false, lyrics: false },
    _tried: { model: 0, plays: 0, style: 0, lyrics: 0 },
    _ready: false,
    _scan: 'waiting',
    _status: 'new',
    _db: null,
    _raw: null,
    _dirty: false,
    _pending: false,
  };
}

class TrackStore {
  tracks = new Map<string, Track>();
  networkTracks = new Map<string, Record<string, unknown>>();
  checkPending = new Set<string>();
  knownMissing = new Set<string>();

  private _listeners = new Set<Listener>();

  subscribe(fn: Listener): () => void {
    this._listeners.add(fn);
    return () => this._listeners.delete(fn);
  }

  notify() { this._notify(); }

  private _notify() {
    this._listeners.forEach(fn => fn());
  }

  private _sortKey(t: Track): number {
    if (t._pending) return -1;
    if (t._scan === 'waiting') return 0;
    if (t._scan === 'paused') return 1;
    if (t._dirty) return 2;
    if (!t._filled.style && t._tried.style >= config.MAX_ATTEMPTS.style) return 3;
    return 4;
  }

  ensureTrack(id: string, title: string): Track | null {
    if (this.knownMissing.has(id)) return null;
    if (this.tracks.has(id)) return this.tracks.get(id)!;
    const t = createTrack(id, title);
    this.tracks.set(id, t);
    this._notify();
    import('./api').then(({ api }) => api.checkTrack(t));
    return t;
  }

  fillFromNetwork() {
    let changed = false;
    for (const [id, nt] of this.networkTracks) {
      if (!nt.title && !this.tracks.has(id)) continue;
      const t = this.ensureTrack(id, nt.title as string);
      if (!t) continue;
      if (nt.audio_url && !t.audioUrl) { t.audioUrl = nt.audio_url as string; t._dirty = true; changed = true; }
      if (nt.model && !t._filled.model) { t.model = nt.model as string; t._filled.model = true; t._dirty = true; changed = true; }
      if (nt.plays && !t._filled.plays) { t.plays = nt.plays as number; t._filled.plays = true; t._dirty = true; changed = true; }
      if ((nt.style as string[] | undefined)?.length && !t._filled.style) {
        t.style = Array.isArray(nt.style) ? (nt.style as string[]).join('; ') : nt.style as string;
        t._filled.style = true;
        t._dirty = true;
        changed = true;
      }
      if (nt.title && !t.title) { t.title = nt.title as string; t._dirty = true; changed = true; }
      if (nt.lyrics && (!t.lyrics || t._db) && nt.lyrics !== t.lyrics) {
        t.lyrics = nt.lyrics as string;
        t._filled.lyrics = true;
        t._dirty = true;
        changed = true;
      }
      const img = (nt.image_url || nt.cover_image_url) as string | undefined;
      if (img && (!t.imageUrl || t._db) && img !== t.imageUrl) {
        t.imageUrl = img;
        t._dirty = true;
        changed = true;
      }
      if (nt.isPublic !== undefined && nt.isPublic !== t.isPublic) {
        t.isPublic = nt.isPublic as boolean;
        t._dirty = true;
        changed = true;
      }
      if (!t._raw) {
        t._raw = nt as Record<string, unknown>;
      }
    }
    if (this.networkTracks.size > 500) {
      const keys = [...this.networkTracks.keys()].slice(0, 100);
      for (const k of keys) this.networkTracks.delete(k);
    }
    if (changed) {
      this._notify();
    }
  }

  refreshStatus(t: Track) {
    if (!t._db || typeof t._db !== 'object') return;
    const db = t._db;
    const modelOk = t._filled.model ? db.model === t.model : true;
    const playsOk = t._filled.plays ? db.plays === t.plays : true;
    const styleOk = t._filled.style ? normStyle(db.styles) === normStyle(t.style) : true;
    const titleOk = db.title === t.title;
    const lyricsOk = t._filled.lyrics ? db.lyrics === t.lyrics : true;
    const isPublicOk = db.isPublic === t.isPublic;
    const imageOk = db.imageUrl === t.imageUrl;
    t._status = modelOk && playsOk && styleOk && titleOk && lyricsOk && isPublicOk && imageOk ? 'ok' : 'diff';
  }

  toggleTrackScan(id: string) {
    const t = this.tracks.get(id);
    if (!t) return;
    t._scan = t._scan === 'waiting' ? 'paused' : 'waiting';
    this._notify();
  }

  applyDbData(originId: string, data: Record<string, unknown>) {
    const t = this.tracks.get(originId);
    if (!t) return;
    t._db = data;
    if (data.lyrics) { t.lyrics = data.lyrics as string; t._filled.lyrics = true; }
    if (data.model) { t.model = data.model as string; t._filled.model = true; t._tried.model = config.MAX_ATTEMPTS.model; }
    if (data.styles) { t.style = data.styles as string; t._filled.style = true; t._tried.style = config.MAX_ATTEMPTS.style; }
    if (data.isPublic !== undefined) t.isPublic = data.isPublic as boolean;
    if (data.imageUrl) t.imageUrl = data.imageUrl as string;
    const modelOk = t._filled.model ? data.model === t.model : true;
    const playsOk = t._filled.plays ? data.plays === t.plays : true;
    const styleOk = t._filled.style ? normStyle(data.styles) === normStyle(t.style) : true;
    const titleOk = data.title === t.title;
    const lyricsOk = data.lyrics === t.lyrics;
    const isPublicOk = data.isPublic === t.isPublic;
    const imageOk = data.imageUrl === t.imageUrl;
    t._status = modelOk && playsOk && styleOk && titleOk && lyricsOk && isPublicOk && imageOk ? 'ok' : 'diff';
    t._dirty = t._status === 'diff';
    t._pending = false;
    this._notify();
  }

  setPendingFailed(originId: string) {
    const t = this.tracks.get(originId);
    if (!t) return;
    t._status = 'diff';
    t._dirty = true;
    t._pending = false;
    this._notify();
  }

  markAsNew(originId: string) {
    const t = this.tracks.get(originId);
    if (!t) return;
    t._status = 'new';
    t._pending = false;
    t._dirty = false;
    t._scan = 'waiting';
    this._notify();
  }

  getImportable(): Track[] {
    return [...this.tracks.values()]
      .filter(t => !t._db && !t._pending && t._status !== 'missing');
  }

  getUpdates(): Track[] {
    return [...this.tracks.values()]
      .filter(t => t._db && t._status === 'diff');
  }

  hasUpdates(): boolean {
    return [...this.tracks.values()].some(t => t._db && t._status === 'diff');
  }

  hasImports(): boolean {
    return [...this.tracks.values()].some(t => !t._db && !t._pending && t._status !== 'missing');
  }

  buildImportList(): { originId: string; [k: string]: unknown }[] {
    const list: { originId: string; [k: string]: unknown }[] = [];
    for (const t of this.tracks.values()) {
      if (t._db || t._pending || t._status === 'missing') continue;
      list.push({
        originId: t.originId,
        ...(t.title ? { title: t.title } : {}),
        ...(t.lyrics ? { lyrics: t.lyrics } : {}),
        ...(t.plays ? { plays: t.plays } : {}),
        ...(t.model ? { model: t.model } : {}),
        ...(t.style ? { styles: t.style } : {}),
        ...(t.isPublic !== undefined ? { isPublic: t.isPublic } : {}),
        ...(t._raw?.created_at ? { createdAt: t._raw.created_at as string } : {}),
        ...(t._raw?.image_url ? { imageUrl: t._raw.image_url as string } : {}),
        ...(t._raw?.cover_image_url ? { imageUrl: t._raw.cover_image_url as string } : {}),
      });
    }
    return list;
  }

  applyImportResult(list: { originId: string }[]) {
    for (const item of list) {
      const t = this.tracks.get(item.originId);
      if (!t) continue;
      t._db = { originId: item.originId };
      t._status = 'ok';
      t._scan = 'done';
      t._ready = true;
      t._dirty = false;
    }
    this._notify();
  }

  buildUpdateList(): { originId: string; [k: string]: unknown }[] {
    const list: { originId: string; [k: string]: unknown }[] = [];
    for (const t of this.tracks.values()) {
      if (!t._db) continue;
      const item: { originId: string; [k: string]: unknown } = { originId: t.originId };
      if (t._db.title !== t.title) item.title = t.title;
      if (t._db.plays !== t.plays && t.plays) item.plays = t.plays;
      if (t._db.isPublic !== t.isPublic) item.isPublic = t.isPublic;
      if (t._db.lyrics !== t.lyrics && t.lyrics) item.lyrics = t.lyrics;
      if (t._db.imageUrl !== t.imageUrl && t.imageUrl) item.imageUrl = t.imageUrl;
      if (Object.keys(item).length > 1) { list.push(item); t._dirty = true; }
      else t._dirty = false;
    }
    return list;
  }

  applyUpdateResult(list: { originId: string; [k: string]: unknown }[], _data: { updated?: number }) {
    for (const item of list) {
      const t = this.tracks.get(item.originId);
      if (!t || !t._db) continue;
      if (item.title !== undefined) t._db.title = item.title;
      if (item.plays !== undefined) t._db.plays = item.plays;
      if (item.isPublic !== undefined) t._db.isPublic = item.isPublic;
      if (item.lyrics !== undefined) t._db.lyrics = item.lyrics;
      if (item.imageUrl !== undefined) t._db.imageUrl = item.imageUrl;
      t._status = 'ok';
      t._scan = 'done';
      t._ready = true;
      t._dirty = false;
    }
    this._notify();
  }

  handleNetwork(tracksArr: Record<string, unknown>[]) {
    if (!tracksArr?.length) return;
    for (const nt of tracksArr) {
      if (nt.id) this.networkTracks.set(nt.id as string, nt);
    }
    this.fillFromNetwork();
  }

  resetAll() {
    this.tracks.clear();
    this.networkTracks.clear();
    this.checkPending.clear();
    this.knownMissing.clear();
    this._notify();
  }
}

export const trackStore = new TrackStore();
