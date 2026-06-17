const API_BASE = 'http://localhost:5000';

async function handleRequest(msg: { method: string; path: string; body?: unknown }): Promise<{ status: number; ok: boolean; body: unknown }> {
  try {
    const opts: RequestInit = { method: msg.method, headers: { 'Content-Type': 'application/json' } };
    if (msg.body) opts.body = JSON.stringify(msg.body);
    const r = await fetch(`${API_BASE}${msg.path}`, opts);
    const text = await r.text();
    let body: unknown;
    try { body = JSON.parse(text); } catch { body = text; }
    return { status: r.status, ok: r.ok, body };
  } catch (e) {
    return { status: 0, ok: false, body: String(e) };
  }
}

chrome.runtime.onMessage.addListener((msg) => {
  if (msg && typeof msg === 'object' && msg.type === 'API_REQUEST') {
    return handleRequest(msg as { method: string; path: string; body?: unknown });
  }
});
