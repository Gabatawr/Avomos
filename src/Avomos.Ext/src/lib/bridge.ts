const API_BASE = 'http://localhost:5000';

function apiUrl(path: string): string {
  return `${API_BASE}${path}`;
}

async function bgFetch(path: string, options?: RequestInit): Promise<Response | null> {
  try {
    const body = options?.body ? JSON.parse(options.body as string) : undefined;
    const msg = {
      type: 'API_REQUEST',
      method: options?.method || 'GET',
      path,
      body,
    };
    const reply: { status: number; ok: boolean; body: unknown } = await chrome.runtime.sendMessage(msg);
    if (!reply) return null;
    const bodyStr = typeof reply.body === 'string' ? reply.body : JSON.stringify(reply.body);
    return new Response(bodyStr, {
      status: reply.status,
      headers: { 'Content-Type': 'application/json' },
    });
  } catch {
    return null;
  }
}

async function bgPost(path: string, body: unknown): Promise<Response | null> {
  return bgFetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

async function bgPut(path: string, body: unknown): Promise<Response | null> {
  return bgFetch(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export const bridge = {
  fetch: bgFetch,
  post: bgPost,
  put: bgPut,
  apiUrl,
};
