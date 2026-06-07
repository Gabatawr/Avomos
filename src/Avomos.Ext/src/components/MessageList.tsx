import { useEffect, useRef } from 'react';
import type { ChatMessage } from '../lib/types';

interface Props {
  messages: ChatMessage[];
  expanded: Record<number, boolean>;
  onToggleExpand: (i: number) => void;
  onInsert: (msg: ChatMessage) => void;
  onHookClick: (hook: string) => void;
}

function esc(s: string): string {
  const d = document.createElement('div');
  d.textContent = s;
  return d.innerHTML;
}

export function MessageList({ messages, expanded, onToggleExpand, onInsert, onHookClick }: Props) {
  const endRef = useRef<HTMLDivElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const userScrolledRef = useRef(false);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const onScroll = () => {
      const nearBottom = el.scrollHeight - el.clientHeight - el.scrollTop < 40;
      userScrolledRef.current = !nearBottom;
    };
    el.addEventListener('scroll', onScroll, { passive: true });
    return () => el.removeEventListener('scroll', onScroll);
  }, []);

  useEffect(() => {
    if (!userScrolledRef.current) {
      endRef.current?.scrollIntoView({ behavior: 'smooth' });
    }
  }, [messages]);

  return (
    <div className="ac-messages" ref={containerRef}>
      {messages.map((m, i) => {
        const isPending = m.content === '...' && i === messages.length - 1;
        const isTool = m.reply || m.simple || m.advanced || m.hooks;

        return (
          <div key={i} className={`ac-msg ac-msg-${m.role}${isPending ? ' ac-msg-pending' : ''}`}>
            {m.role === 'user' && (
              <div className="ac-msg-bubble">{esc(m.content)}</div>
            )}
            {m.reply && (
              <div className="ac-msg-bubble">{esc(m.reply)}</div>
            )}
            {m.simple && (
              <div className="ac-msg-bubble">{esc(m.simple)}</div>
            )}
            {m.advanced && (
              <>
                <div className="ac-msg-bubble">{esc(m.advanced.title || '(no title)')}</div>
                <div className="ac-msg-extra">
                  <button className="ac-msg-toggle" onClick={() => onToggleExpand(i)}>
                    {expanded[i] ? '▲' : '▼'} Lyrics, Styles, Title
                  </button>
                  <div className="ac-msg-extra-body" style={{ display: expanded[i] ? '' : 'none' }}>
                    {m.advanced.lyrics && (
                      <div className="ac-msg-extra-row">
                        <span className="ac-msg-extra-label">Lyrics</span>
                        <span className="ac-msg-extra-val">{esc(m.advanced.lyrics)}</span>
                      </div>
                    )}
                    {m.advanced.styles && (
                      <div className="ac-msg-extra-row">
                        <span className="ac-msg-extra-label">Styles</span>
                        <span className="ac-msg-extra-val">{esc(m.advanced.styles)}</span>
                      </div>
                    )}
                    {m.advanced.title && (
                      <div className="ac-msg-extra-row">
                        <span className="ac-msg-extra-label">Title</span>
                        <span className="ac-msg-extra-val">{esc(m.advanced.title)}</span>
                      </div>
                    )}
                  </div>
                  <button className="ac-msg-insert" onClick={() => onInsert(m)}>Insert</button>
                </div>
              </>
            )}
            {m.hooks && m.hooks.length > 0 && (
              <div className="ac-msg-hooks">
                {m.hooks.map((h, hi) => (
                  <button key={hi} className="ac-hook-btn" onClick={() => onHookClick(h)}>
                    {esc(h)}
                  </button>
                ))}
              </div>
            )}
            {!isTool && !isPending && m.role === 'assistant' && m.content && (
              <div className="ac-msg-bubble">{esc(m.content)}</div>
            )}
          </div>
        );
      })}
      <div ref={endRef} />
    </div>
  );
}
