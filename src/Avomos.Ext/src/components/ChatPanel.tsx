import { useState, useCallback, useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useResize } from '../hooks/useResize';
import { api } from '../lib/api';
import { config } from '../lib/config';
import { SearchResults } from './SearchResults';
import type { SearchResultsHandle } from './SearchResults';
import { BufferList } from './BufferList';
import { MessageList } from './MessageList';
import { RiderDisplay } from './RiderDisplay';
import type { BufferItem, ChatMessage, ChatResponse, SessionInfo, MatchedRider } from '../lib/types';

interface Props {
  hidden: boolean;
  onToggle: () => void;
}

function detectCreateMode(): string {
  try {
    const tab = document.querySelector('button[role="tab"][aria-selected="true"]');
    return tab ? (tab.getAttribute('aria-label') || '').toLowerCase() : '';
  } catch { return ''; }
}

export function ChatPanel({ hidden, onToggle }: Props) {
  const [panelW, onResizeMouseDown] = useResize('avomos_chat_w', 'right');
  const [buffer, setBuffer] = useState<BufferItem[]>([]);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [waiting, setWaiting] = useState(false);
  const [expanded, setExpanded] = useState<Record<number, boolean>>({});
  const [inputText, setInputText] = useState('');
  const [createMode, setCreateMode] = useState(() => detectCreateMode());
  const [sessionId, setSessionId] = useState('');
  const [sessionName, setSessionName] = useState('New Chat');
  const [historyOpen, setHistoryOpen] = useState(false);
  const [matchedRiders, setMatchedRiders] = useState<MatchedRider[]>([]);
  const [riderCanCreate, setRiderCanCreate] = useState(false);
  const [riderLoading, setRiderLoading] = useState(false);
  const [riderThreshold, setRiderThreshold] = useState(() => {
    const saved = localStorage.getItem('avomos_rider_threshold');
    return saved ? parseFloat(saved) : 0.55;
  });
  const [sessions, setSessions] = useState<SessionInfo[]>([]);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const renameRef = useRef<HTMLInputElement>(null);
  const messagesRef = useRef(messages);
  messagesRef.current = messages;
  const searchRef = useRef<SearchResultsHandle>(null);
  const riderTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const persistSession = useCallback(async (buf: BufferItem[], msgs: ChatMessage[]) => {
    if (!sessionId) return;
    const res = await api.saveSession(sessionId, buf, msgs);
    if (res) {
      const cur = res.sessions.find(s => s.id === sessionId);
      if (cur) setSessionName(cur.name);
    }
  }, [sessionId]);

  const syncRiders = useCallback(async (buf: BufferItem[], thr?: number) => {
    const ids = buf.map(b => b._originId);
    if (ids.length < 3) {
      setMatchedRiders([]);
      setRiderCanCreate(false);
      return;
    }
    const t = thr ?? riderThreshold;
    setRiderLoading(true);
    const matchRes = await api.riderMatch(ids, t);
    if (matchRes) {
      setMatchedRiders(matchRes.riders);
      setRiderCanCreate(matchRes.canCreate);
    }
    setRiderLoading(false);
  }, [riderThreshold]);

  const scheduleRiders = useCallback((buf: BufferItem[], thr: number) => {
    if (riderTimerRef.current) clearTimeout(riderTimerRef.current);
    riderTimerRef.current = setTimeout(() => syncRiders(buf, thr), 300);
  }, [syncRiders]);

  const flushRiders = useCallback((buf: BufferItem[], thr: number) => {
    if (riderTimerRef.current) clearTimeout(riderTimerRef.current);
    riderTimerRef.current = null;
    syncRiders(buf, thr);
  }, [syncRiders]);

  const handleThresholdChange = useCallback((val: number) => {
    setRiderThreshold(val);
    localStorage.setItem('avomos_rider_threshold', String(val));
    setBuffer(prev => {
      scheduleRiders(prev, val);
      return prev;
    });
  }, [scheduleRiders]);

  const addToBuffer = useCallback((originId: string, title: string, data?: { model?: string; style?: string; plays?: number; lyrics?: string; isPublic?: boolean }) => {
    setBuffer(prev => {
      if (prev.some(b => b._originId === originId)) return prev;
      const item: BufferItem = {
        _originId: originId, _title: title, title,
        ...(data?.model ? { model: data.model } : {}),
        ...(data?.style ? { style: data.style } : {}),
        ...(data?.plays ? { plays: data.plays } : {}),
        ...(data?.lyrics ? { lyrics: data.lyrics } : {}),
        ...(data?.isPublic !== undefined ? { isPublic: data.isPublic } : {}),
      };
      const newBuf = [...prev, item];
      persistSession(newBuf, messagesRef.current);
      scheduleRiders(newBuf, riderThreshold);
      return newBuf;
    });
  }, [persistSession, riderThreshold, scheduleRiders]);

  const removeFromBuffer = useCallback((originId: string) => {
    setBuffer(prev => {
      const newBuf = prev.filter(b => b._originId !== originId);
      persistSession(newBuf, messagesRef.current);
      scheduleRiders(newBuf, riderThreshold);
      return newBuf;
    });
  }, [persistSession, riderThreshold, scheduleRiders]);

  const semanticSearch = useCallback((originId: string, title: string) => {
    searchRef.current?.externalSearch(title || originId);
  }, []);

  const sendMessage = useCallback(async (overrideText?: string) => {
    const text = (overrideText ?? inputText).trim();
    if (!text || waiting) return;
    setInputText('');
    setCreateMode(detectCreateMode());

    const userMsg: ChatMessage = { role: 'user', content: text };
    const pendingMsg: ChatMessage = { role: 'assistant', content: '...' };
    const newMsgs = [...messagesRef.current, userMsg, pendingMsg];
    setMessages(newMsgs);
    setWaiting(true);

    const trackIds = buffer.map(b => b._originId);
    const data: ChatResponse | null = await api.chat(trackIds, createMode, newMsgs.slice(0, -1), riderThreshold);

    const finalMsgs = [...newMsgs.slice(0, -1)];
    if (data) {
      finalMsgs.push({
        role: 'assistant',
        content: data.reply || '',
        simple: data.simple || null,
        advanced: data.advanced || null,
        hooks: data.hooks || null,
      });
    } else {
      finalMsgs.push({ role: 'assistant', content: '[Error connecting to chat API]' });
    }
    setMessages(finalMsgs);
    setWaiting(false);
    persistSession(buffer, finalMsgs);
  }, [inputText, waiting, buffer, createMode, persistSession]);

  useEffect(() => {
    api.restoreSession().then(data => {
      if (data) {
        if (data.sessionId) setSessionId(data.sessionId);
        if (data.buffer) {
          const buf = data.buffer as BufferItem[];
          setBuffer(buf);
          if (buf.length >= 3) scheduleRiders(buf, riderThreshold);
        }
        if (data.messages) setMessages(data.messages as ChatMessage[]);
        if (data.sessionId) {
          api.loadSessions().then(sd => {
            if (sd) {
              const cur = sd.sessions.find(s => s.id === data.sessionId);
              if (cur) setSessionName(cur.name);
            }
          });
        }
      }
    });
  }, [scheduleRiders]);

  useEffect(() => () => { if (riderTimerRef.current) clearTimeout(riderTimerRef.current); }, []);

  const deleteSession = useCallback(async (id: string) => {
    const res = await api.deleteSession(id);
    if (res) {
      const data = await api.loadSessions();
      if (data) {
        setSessions(data.sessions);
        if (id === sessionId) {
          const cur = data.sessions.find(s => s.id === data.currentId);
          if (cur) {
            setSessionId(cur.id);
            setSessionName(cur.name);
            const r = await fetch(`${config.API_BASE}/chat/session/${cur.id}`);
            if (r.ok) {
              const sd = await r.json();
              setBuffer(sd.buffer || []);
              setMessages(sd.messages || []);
            }
          } else {
            setSessionId('');
            setSessionName('New Chat');
            setBuffer([]);
            setMessages([]);
          }
        }
      }
    }
  }, [sessionId]);

  const deleteCurrentSession = useCallback(() => {
    if (waiting) return;
    if (!sessionId) return;
    if (!confirm('Delete this chat?')) return;
    deleteSession(sessionId);
  }, [waiting, sessionId, deleteSession]);

  const handleInsert = useCallback((msg: ChatMessage) => {
    const $ = (s: string): Element | null => { try { return document.querySelector(s); } catch { return null; } };
    const setVal = (el: Element | null, val: string) => {
      if (el && val) {
        (el as HTMLInputElement).value = val;
        el.dispatchEvent(new Event('input', { bubbles: true }));
      }
    };
    if (msg.advanced) {
      if (msg.advanced.lyrics) setVal($('textarea[data-testid="lyrics-textarea"]'), msg.advanced.lyrics);
      if (msg.advanced.styles) setVal($('[data-testid="create-form-styles-wrapper"] textarea'), msg.advanced.styles);
      if (msg.advanced.title) setVal($('input[placeholder*="Title" i]'), msg.advanced.title);
    }
  }, []);

  const handleHookClick = useCallback((hook: string) => {
    sendMessage(hook);
  }, [sendMessage]);

  const toggleExpand = useCallback((i: number) => {
    setExpanded(prev => ({ ...prev, [i]: !prev[i] }));
  }, []);

  // --- History modal ---

  const openHistory = useCallback(async () => {
    const data = await api.loadSessions();
    if (data) {
      setSessions(data.sessions);
      setHistoryOpen(true);
    }
  }, []);

  const switchSession = useCallback(async (id: string) => {
    await persistSession(buffer, messages);
    const data = await api.loadSessions();
    if (data) {
      const cur = data.sessions.find(s => s.id === id);
      if (!cur) return;
      const r = await fetch(`${config.API_BASE}/chat/session/${id}`);  
      if (r.ok) {
        const sd = await r.json();
        setSessionId(id);
        setSessionName(cur.name);
        setBuffer(sd.buffer || []);
        setMessages(sd.messages || []);
      }
    }
  }, [buffer, messages, persistSession]);

  const createNewSession = useCallback(async () => {
    await persistSession(buffer, messages);
    const res = await api.createSession();
    if (res) {
      setSessionId(res.id);
      setSessionName('New Chat');
      setBuffer([]);
      setMessages([]);
      searchRef.current?.clearSearch();
      const data = await api.loadSessions();
      if (data) setSessions(data.sessions);
    }
  }, [buffer, messages, persistSession]);

  const startRename = useCallback((id: string, name: string) => {
    setRenamingId(id);
    setRenameValue(name);
    setTimeout(() => renameRef.current?.focus(), 50);
  }, []);

  const submitRename = useCallback(async (id: string) => {
    const val = renameValue.trim();
    if (!val) { setRenamingId(null); return; }
    const res = await api.renameSession(id, val);
    if (res) {
      setSessions(res.sessions);
      if (id === sessionId) setSessionName(val);
    }
    setRenamingId(null);
  }, [renameValue, sessionId]);

  return (
    <>
      <button id="avomos-toggle-right" className="ap-toggle" onClick={onToggle}>☰</button>
      <div
        id="avomos-chat-root"
        style={{ width: `${panelW}px`, transform: hidden ? 'translateX(102%)' : '' }}
        onWheel={e => e.stopPropagation()}
      >
        <div id="ac-resize" onMouseDown={onResizeMouseDown} />
        <div className="ac-panel">
          <div className="ac-header">
            <span className="ac-session-name">{sessionName}</span>
            <div className="ac-header-right">
              <button className="ac-hdr-btn" onClick={createNewSession} title="New session">ADD</button>
              <button className={`ac-hdr-btn${historyOpen ? ' ac-hdr-active' : ''}`} onClick={openHistory} title="Session history">HIS</button>
              <button className="ac-hdr-btn" onClick={deleteCurrentSession} title="Delete current chat">DEL</button>
            </div>
          </div>

          <SearchResults ref={searchRef} buffer={buffer} onAddToBuffer={addToBuffer} chatPanelW={panelW} />
          <BufferList buffer={buffer} onRemove={removeFromBuffer} onSemanticSearch={semanticSearch} chatPanelW={panelW} />
          <RiderDisplay
            riders={matchedRiders}
            canCreate={riderCanCreate}
            loading={riderLoading}
            trackIds={buffer.map(b => b._originId)}
            threshold={riderThreshold}
            onRidersUpdate={setMatchedRiders}
            onThresholdChange={handleThresholdChange}
            onFlushRiders={() => flushRiders(buffer, riderThreshold)}
            onChatMessage={text => setMessages(prev => [...prev, { role: 'assistant', content: text }])}
          />
          <MessageList messages={messages} expanded={expanded} onToggleExpand={toggleExpand} onInsert={handleInsert} onHookClick={handleHookClick} />

          <div className="ac-input-row">
            <textarea
              className="ac-input ac-msg-textarea"
              placeholder="Type a message..."
              rows={1}
              value={inputText}
              onChange={e => {
                setInputText(e.target.value);
                e.target.style.height = 'auto';
                e.target.style.height = Math.min(e.target.scrollHeight, 200) + 'px';
              }}
              onKeyDown={e => {
                if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
              }}
            />
          </div>
          <div className="ac-controls">
            <button
              className="ac-ctl-btn ac-ctl-send"
              disabled={waiting || !inputText.trim()}
              onClick={() => sendMessage()}
            >Send</button>
          </div>
        </div>
      </div>
      {historyOpen && createPortal(
        <div className="ac-modal-overlay" onClick={() => setHistoryOpen(false)}>
          <div className="ac-modal-box ac-history-box" onClick={e => e.stopPropagation()} style={{ right: panelW }}>
            <div className="ac-modal-header">
              <span>HISTORY</span>
              <button className="ac-modal-close" onClick={() => setHistoryOpen(false)}>✕</button>
            </div>
            <div className="ac-modal-body ac-history-body">
              {sessions.map(s => (
                <div key={s.id} className={`ac-history-row${s.id === sessionId ? ' ac-history-cur' : ''}`} onClick={() => !(s.id === sessionId) && switchSession(s.id)}>
                  {renamingId === s.id ? (
                    <input
                      ref={renameRef}
                      className="ac-input ac-rename-input"
                      value={renameValue}
                      onChange={e => setRenameValue(e.target.value)}
                      onKeyDown={e => {
                        if (e.key === 'Enter') submitRename(s.id);
                        if (e.key === 'Escape') setRenamingId(null);
                      }}
                      onBlur={() => submitRename(s.id)}
                    />
                  ) : (
                    <span className="ac-history-name">{s.name}</span>
                  )}
                  <span className="at-style at-style-clickable" onClick={e => { e.stopPropagation(); startRename(s.id, s.name); }}>e</span>
                  <span className="ac-history-del" onClick={e => { e.stopPropagation(); if (confirm('Delete this chat?')) deleteSession(s.id); }}>−</span>
                </div>
              ))}
            </div>
          </div>
        </div>,
        document.body
      )}
    </>
  );
}
