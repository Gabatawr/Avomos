import { trackStore } from './track-store';
import { bridge } from './bridge';
import type { ChatMessage, ChatResponse, SearchHit, SessionInfo, MatchResponse, CreateRiderResult } from './types';

export const api = {
  sendLog(msg: string) {
    console.log(msg);
    bridge.post('/logs', { message: msg });
  },

  async checkTrack(t: { originId: string }) {
    if (trackStore.checkPending.has(t.originId)) return;
    trackStore.checkPending.add(t.originId);
    try {
      const r = await bridge.fetch(`/tracks/${t.originId}`);
      if (r && r.ok) {
        const body: Record<string, unknown> = await r.json();
        if (body.found === true && body.lyric) {
          trackStore.applyDbData(t.originId, body.lyric as Record<string, unknown>);
        } else {
          trackStore.markAsNew(t.originId);
        }
      } else {
        trackStore.markAsNew(t.originId);
      }
    } catch {
      trackStore.setPendingFailed(t.originId);
    }
  },

  async updateAll(): Promise<{ ok: boolean; data?: { updated?: number } }> {
    const list = trackStore.buildUpdateList();
    if (!list.length) return { ok: false };
    const r = await bridge.post('/tracks/metadata', { tracks: list });
    if (r && r.ok) {
      const data: { updated?: number } = await r.json();
      trackStore.applyUpdateResult(list, data);
      return { ok: true, data };
    }
    return { ok: false };
  },

  async importAll(): Promise<{ ok: boolean; data?: { imported?: number; updated?: number } }> {
    const list = trackStore.buildImportList();
    if (!list.length) return { ok: false };
    const r = await bridge.post('/tracks/upsert', { tracks: list });
    if (r && r.ok) {
      const data: { imported?: number; updated?: number } = await r.json();
      trackStore.applyImportResult(list.map(t => ({ originId: t.originId })));
      return { ok: true, data };
    }
    return { ok: false };
  },

  async search(query: string, limit = 3, titleLyricsWeight = 0.7): Promise<SearchHit[]> {
    try {
      const stylesWeight = 1 - titleLyricsWeight;
      const r = await bridge.fetch(
        `/tracks/search?query=${encodeURIComponent(query)}&limit=${limit}&titleLyricsWeight=${titleLyricsWeight}&stylesWeight=${stylesWeight}`
      );
      if (!r || !r.ok) return [];
      const data = await r.json();
      return data?.hits || [];
    } catch {
      return [];
    }
  },

  async chat(trackIds: string[], createMode: string, messages: ChatMessage[], ridersThreshold?: number): Promise<ChatResponse | null> {
    try {
      const r = await bridge.post('/chat', { trackIds, createMode, messages, ridersThreshold });
      if (!r || !r.ok) return null;
      return await r.json();
    } catch {
      return null;
    }
  },

  async saveSession(sessionId: string, buffer: unknown[], messages: unknown[]): Promise<{ sessionId: string; sessions: SessionInfo[]; currentId: string } | null> {
    const r = await bridge.post('/chat/session', { sessionId, buffer, messages });
    if (!r || !r.ok) return null;
    return await r.json();
  },

  async restoreSession(): Promise<{ sessionId: string; buffer: unknown[]; messages: unknown[] } | null> {
    const r = await bridge.fetch('/chat/session');
    if (!r || !r.ok) return null;
    return await r.json();
  },

  async loadSessions(): Promise<{ sessions: SessionInfo[]; currentId: string } | null> {
    const r = await bridge.fetch('/chat/sessions');
    if (!r || !r.ok) return null;
    return await r.json();
  },

  async createSession(): Promise<{ id: string } | null> {
    const r = await bridge.fetch('/chat/sessions/create', { method: 'POST' });
    if (!r || !r.ok) return null;
    return await r.json();
  },

  async renameSession(id: string, name: string): Promise<{ sessions: SessionInfo[]; currentId: string } | null> {
    const r = await bridge.put(`/chat/session/${id}/rename`, { name });
    if (!r || !r.ok) return null;
    return await r.json();
  },

  async deleteSession(id: string): Promise<{ currentId?: string } | null> {
    const r = await bridge.fetch(`/chat/session/${id}`, { method: 'DELETE' });
    if (!r || !r.ok) return null;
    return await r.json();
  },

  async riderMatch(trackIds: string[], threshold?: number): Promise<MatchResponse | null> {
    try {
      const r = await bridge.post('/riders/match', { trackIds, threshold });
      if (!r || !r.ok) return null;
      return await r.json();
    } catch { return null; }
  },

  async riderCreate(trackIds: string[], threshold?: number): Promise<CreateRiderResult | null> {
    try {
      const r = await bridge.post('/riders/create', { trackIds, threshold });
      if (!r || !r.ok) return null;
      return await r.json();
    } catch { return null; }
  },

  async riderDelete(id: string): Promise<boolean> {
    try {
      const r = await bridge.fetch(`/riders/${id}`, { method: 'DELETE' });
      return r !== null && r.ok;
    } catch { return false; }
  },
};
