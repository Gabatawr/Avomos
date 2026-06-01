export interface Track {
  originId: string;
  title: string;
  model: string;
  plays: number;
  style: string;
  audioUrl: string;
  isPublic: boolean;
  lyrics: string;
  imageUrl: string;
  _filled: { model: boolean; plays: boolean; style: boolean; lyrics: boolean };
  _tried: { model: number; plays: number; style: number; lyrics: number };
  _ready: boolean;
  _scan: 'waiting' | 'paused' | 'done';
  _status: 'new' | 'ok' | 'diff' | 'missing';
  _db: Record<string, unknown> | null;
  _raw: Record<string, unknown> | null;
  _dirty: boolean;
  _pending: boolean;
}

export interface AdvancedData {
  lyrics: string;
  styles: string;
  title: string;
}

export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  simple?: string | null;
  advanced?: AdvancedData | null;
  hooks?: string[] | null;
  reply?: string | null;
}

export interface BufferItem {
  _originId: string;
  _title: string;
  title?: string;
  model?: string;
  style?: string;
  plays?: number;
  isPublic?: boolean;
  lyrics?: string;
}

export interface SearchHit {
  originId: string;
  title: string;
  model?: string;
  plays?: number;
  lyrics?: string;
  isPublic?: boolean;
  styles?: string;
}

export interface SessionInfo {
  id: string;
  name: string;
}

export interface ChatResponse {
  reply?: string;
  simple?: string;
  advanced?: AdvancedData;
  hooks?: string[];
}
