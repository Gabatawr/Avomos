# Avomos v0.5.1

Browser extension + backend for parsing and managing metadata from Suno AI song feed.

## Architecture

```
Avomos/
├── src/
│   ├── Avomos.Api/              # ASP.NET Core API (C#)
│   │   ├── Data/                # Static data files (default-riders.json)
│   │   ├── Features/            # Chat, Riders, Tracks endpoints
│   │   ├── Infrastructure/      # Shared DTOs, Qdrant HTTP helpers
│   │   ├── Models/              # Qdrant document model, vectors
│   │   ├── Pipelines/           # MediatR pipeline behaviors
│   │   ├── Prompts/             # LLM prompts as Markdown (.md)
│   │   └── Services/            # RiderService, EmbeddingService, LlmCache, ChatSessionService
│   └── Avomos.Ext/              # MV3 extension (Preact + TypeScript + SCSS)
│       ├── dist/                # Prebuilt Chrome extension (load unpacked)
│       ├── scripts/             # Build helpers (zip-firefox.mjs)
│       ├── public/              # Web-accessible resources (page-interceptor.js)
│       ├── src/
│       │   ├── background.ts    # Service worker — proxies API requests to bypass PNA
│       │   ├── lib/
│       │   │   ├── bridge.ts    # Content ↔ service worker messaging
│       │   │   ├── api.ts       # API client (uses bridge)
│       │   │   ├── config.ts
│       │   │   ├── types.ts
│       │   │   └── track-store.ts
│       │   ├── components/
│       │   ├── hooks/
│       │   ├── styles/
│       │   └── App.tsx
│       ├── manifest.json        # Chrome manifest
│       ├── manifest.firefox.json
│       └── avomos-firefox.xpi   # Prebuilt Firefox extension
├── volumes/                     # Runtime data (preserved via .gitkeep)
│   ├── chat/                    # Chat session persistence
│   └── qdrant/                  # Qdrant storage
├── docker-compose.yml           # Qdrant + API
├── docker-compose.override.yml  # Local secrets (gitignored)
└── Dockerfile
```

### Stack

| Component | Tech |
|-----------|------|
| API | ASP.NET Core 10, MediatR, Semantic Kernel |
| Vector DB | Qdrant (gRPC + REST) |
| Embeddings | OpenRouter (`nvidia/llama-nemotron-embed-vl-1b-v2:free`) |
| Chat LLM | OpenAI-compatible (configurable via `Llm` settings) |
| Extension | Preact, Vite, @crxjs/vite-plugin, SCSS |

### Network Architecture

API calls from the extension content script are proxied through a background service worker to bypass Chrome's Private Network Access (PNA) restrictions. The content script sends requests via `chrome.runtime.sendMessage` — the service worker performs the actual `fetch()` to `http://localhost:5000` and returns the response.

```
Suno feed → page-interceptor.js → content script
  → chrome.runtime.sendMessage → service worker → fetch → API backend → Qdrant
```

### Chat Tools

LLM responds via one of four tools (prompt-based JSON):

- **reply** — casual conversation, questions
- **simple** — Simple mode generation prompt (`Idea: ... Styles: ...`)
- **advanced** — Advanced mode (lyrics + styles + title)
- **hooks** — Creative ideas as short clickable buttons (1-3 words)

## Quick Start

```bash
# 1. Create docker-compose.override.yml with your API keys (see Configuration)
# 2. Start all services
docker compose up -d

# 3. Load the extension
#
# Chrome:
#   chrome://extensions → Load unpacked → select src/Avomos.Ext/dist/
#
# Firefox:
#   about:debugging#/runtime/this-firefox → Load Temporary Add-on → select
#   src/Avomos.Ext/avomos-firefox.xpi (or manifest.json from dist-firefox/)

# To build from source instead of using prebuilt artifacts:
cd src/Avomos.Ext
npm run build          # Chrome + Firefox + .xpi
npm run build:chrome   # Chrome only → dist/
npm run build:firefox  # Firefox only → dist-firefox/ + .xpi
```

## Configuration

### API Keys (required)

Create `docker-compose.override.yml` in the project root:

```yaml
services:
  api:
    environment:
      - OpenRouter__ApiKey=your-openrouter-key
      - OpenRouter__Endpoint=https://openrouter.ai/api/v1/
      - OpenRouter__Model=nvidia/llama-nemotron-embed-vl-1b-v2:free
      - Llm__ApiKey=your-llm-api-key
      - Llm__Endpoint=https://api.deepseek.com/v1/
      - Llm__ChatModelId=deepseek-v4-flash
```

The `Llm` section supports any OpenAI-compatible API — just change Endpoint and ChatModelId.

> `docker-compose.override.yml` is gitignored and won't be committed.

## API Endpoints

### Lyrics
- `GET /tracks/{originId}` — get by OriginId
- `GET /tracks/search?query=...` — semantic search
- `POST /tracks/upsert` — import tracks
- `POST /tracks/metadata` — update metadata
- `DELETE /tracks/{originId}` — delete track

### Chat
- `POST /chat` — LLM chat with buffer context (returns reply/simple/advanced/hooks), accepts `ridersThreshold`
- `GET/POST/DELETE /chat/session` — session management

### Riders
- `POST /riders/match` — match top 3 riders by buffer tracks + threshold, returns `canCreate` + `similarity`
- `POST /riders/create` — create a new rider via LLM (3 default + 3 matched custom riders, fallback to 6 defaults). Prompt: `Prompts/CreateRider.md`
- `DELETE /riders/{id}` — delete a custom rider

## Data Flow

```
Suno feed → page-interceptor.js → CustomEvent → TrackStore → API backend → Qdrant
```

The extension intercepts Suno's API responses, extracts track metadata, and syncs it with the backend. Qdrant stores lyrics as vectors for semantic search.

## Changelog

### v0.5.1 — Background service worker, PNA bypass, Windows build fix

- **Background service worker**: API requests are now proxied through a service worker (`src/background.ts`) to bypass Chrome's Private Network Access (PNA) checks. Content script communicates via `chrome.runtime.sendMessage` — the worker performs the actual `fetch()`.
- **Bridge module**: New `src/lib/bridge.ts` provides a unified interface for content ↔ service worker messaging.
- **Windows build fix**: Firefox build now works on Windows via `cross-env` and a Node.js zip script (`scripts/zip-firefox.mjs`) instead of Unix-specific `FIREFOX=1` / `python3`.
- **CORS cleanup**: Removed `Access-Control-Allow-Private-Network` middleware from `Program.cs` — no longer needed with the service worker proxy.

### v0.4.2 — Centroid coherence, detailed_style embedding, outlier highlight

- **Coherence rework**: tracks compared pairwise via centroid (not vs DB). Returns `outlierTrackId`
- **Rider embedding**: `short_style` → `detailed_style` (concrete description over tags, 120-180 chars)
- **Outlier highlight**: extension shows least coherent track — orange (canCreate=false) / yellow (canCreate=true)

### v0.4 — Project restructure

- `RiderSeeder.cs` hardcoded riders → `Data/default-riders.json`
- Prompts: `.txt` + inline C# → `Prompts/*.md` (markdown)
- Qdrant REST DTOs → `Infrastructure/QdrantHttp.cs`
- Chat sessions → `Services/ChatSessionService.cs`
- `LyricTag.cs` removed, `Lyric` model `set` → `init`

### v0.3.4 — Build artifacts, Firefox support, .gitkeep

- Firefox support: `.xpi`, `npm run build:firefox`
- Chrome + Firefox prebuilt in repo — no build needed
- volumes/ dir tracked via `.gitkeep`

### v0.3.0 — Riders

- 6 default riders seeded into Qdrant, matched by style similarity
- LLM rider creation via `/riders/create`, deletion via `/riders/{id}`

## Notes

- Qdrant gRPC `ScrollAsync()` has a bug (ignores `limit`, returns 10). Use REST `/points/scroll` for scrolling.
- Embeddings are cached in `.cache/llm/embedding/` inside the container (lost on restart — temporary cache).
- `docker-compose.override.yml` and `appsettings.*.local.json` are gitignored — keep secrets there.
- Extension version auto-bumps patch on each `npm run build` (via `prebuild` script). Prebuilt artifacts in repo may lag behind source.
- A background service worker is required to bypass Chrome's Private Network Access checks. The service worker is registered in the manifest and built via `vite.config.ts`. On first load, Chrome may take a few seconds to start the worker.
- Firefox build on Windows uses `cross-env` for env vars and a Node.js script for `.xpi` packaging instead of Unix-specific `FIREFOX=1` / `python3`. `cross-env` is automatically installed via `npm install` in the extension directory.

## License

MIT
