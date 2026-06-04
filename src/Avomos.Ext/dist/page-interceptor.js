(function() {
  if (window.__AVOMOS_INTERCEPTOR__) return;
  window.__AVOMOS_INTERCEPTOR__ = true;

  const EVENT_NAME = 'avomos:network-data';

  function shouldTrack(url) {
    return /api|graphql|clip|song|track|workspace|create|query/i.test(String(url || ''));
  }

  function isTrack(raw) {
    if (!raw || typeof raw !== 'object') return false;
    const id = raw.id || raw.clip_id || raw.clipId || raw.song_id || raw.songId || raw.track_id || raw.trackId;
    const title = raw.title || raw.name;
    if (!id || !title) return false;
    const hasAudio = !!(raw.audio_url || raw.audioUrl);
    const hasMedia = Array.isArray(raw.media_urls) && raw.media_urls.length > 0;
    const isEntity = /song|clip/i.test(String(raw.entity_type || raw.entityType || ''));
    const hasMeta = !!(raw.metadata?.prompt || raw.prompt || raw.lyrics || raw.tags);
    const hasImage = !!(raw.image_url || raw.cover_image_url);
    return hasAudio || hasMedia || isEntity || hasMeta || hasImage;
  }

  function extractRaw(url, payload) {
    const raw = [];
    const urlStr = String(url || '');

    if (/\/api\/feed\//i.test(urlStr) && Array.isArray(payload?.clips)) {
      raw.push(...payload.clips);
    }
    if (/\/api\/project\//i.test(urlStr) && Array.isArray(payload?.project_clips)) {
      for (const pc of payload.project_clips) raw.push(pc?.clip || pc);
    }
    if (/\/api\/(song|songs|clip|clips)\//i.test(urlStr)) {
      if (payload?.clip) raw.push(payload.clip);
      if (payload?.song) raw.push(payload.song);
      if (Array.isArray(payload?.clips)) raw.push(...payload.clips);
      if (Array.isArray(payload?.songs)) raw.push(...payload.songs);
    }
    if (Array.isArray(payload?.data)) raw.push(...payload.data);

    if (!raw.length) {
      search(payload, raw);
    }

    return raw.filter(isTrack).slice(0, 100);
  }

  function search(node, results, depth = 0) {
    if (!node || depth > 8 || results.length >= 100) return;
    if (Array.isArray(node)) { node.forEach(v => search(v, results, depth + 1)); return; }
    if (typeof node !== 'object') return;
    if (isTrack(node)) results.push(node);
    for (const v of Object.values(node)) search(v, results, depth + 1);
  }

  function convert(raw) {
    const id = raw.id || raw.clip_id || raw.clipId || raw.song_id || raw.songId || raw.track_id || raw.trackId;
    if (!id) return null;

    var audioUrl = raw.audio_url || raw.audioUrl || raw.stream_url || raw.streamUrl || '';
    if (!audioUrl && Array.isArray(raw.media_urls)) {
      var candidates = raw.media_urls.map(function(v) {
        if (typeof v === 'string') return v;
        if (v && typeof v === 'object') return v.url || v.src || v.audio_url || v.audioUrl || null;
        return null;
      }).filter(Boolean);
      var candidate = candidates.find(function(v) { return /\.mp3(\?|$)/i.test(String(v)); }) || candidates[0];
      if (candidate) audioUrl = String(candidate);
    }

    var lyrics = raw.lyrics || raw.text || raw.metadata?.prompt || '';
    if (typeof lyrics !== 'string') lyrics = '';

    return {
      id: String(id),
      title: (raw.title || raw.name || '').trim().slice(0, 200),
      plays: raw.plays || raw.play_count || raw.plays_count || 0,
      audio_url: audioUrl,
      style: raw.metadata?.tags ? raw.metadata.tags.split(/;\s*/).filter(Boolean)
        : raw.tags ? (Array.isArray(raw.tags) ? raw.tags : [raw.tags])
        : [],
      model: raw.model || raw.metadata?.model || raw.metadata?.model_version || raw.audio_model_type || raw.metadata?.audio_model_type || '',
      isPublic: raw.is_public ?? (raw.visibility === 'public' ? true : raw.visibility === 'private' ? false : true),
      lyrics: lyrics,
      image_url: raw.image_url || raw.cover_image_url || '',
      created_at: raw.created_at || raw.createdAt || ''
    };
  }

  function process(url, text) {
    if (!shouldTrack(url)) return;
    let parsed;
    try { parsed = JSON.parse(text); } catch { return; }
    const raws = extractRaw(url, parsed);
    if (!raws.length) return;
    const tracks = raws.map(convert).filter(Boolean);
    if (!tracks.length) return;
    window.dispatchEvent(new CustomEvent(EVENT_NAME, { detail: tracks }));
  }

  const origFetch = window.fetch;
  window.fetch = function(input, init) {
    const url = typeof input === 'string' ? input : input instanceof Request ? input.url : '';
    return origFetch.call(this, input, init).then(async r => {
      if (r.ok && url && shouldTrack(url)) {
        const ct = r.headers.get('content-type') || '';
        if (/json|text/i.test(ct)) {
          r.clone().text().then(t => process(url, t)).catch(function() {});
        }
      }
      return r;
    });
  };

  const origOpen = XMLHttpRequest.prototype.open;
  const origSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open = function(method, url) {
    this._avUrl = typeof url === 'string' ? url : '';
    return origOpen.apply(this, arguments);
  };
  XMLHttpRequest.prototype.send = function(body) {
    if (this._avUrl && shouldTrack(this._avUrl)) {
      this.addEventListener('load', () => {
        if (this.status >= 200 && this.status < 300 && this.responseText) {
          process(this._avUrl, this.responseText);
        }
      });
    }
    return origSend.apply(this, arguments);
  };
})();
