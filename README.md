# Avomos v0.3.0

Chrome extension + backend for parsing and managing metadata from Suno AI song feed.

## Architecture

```
Avomos/
├── src/
│   ├── Avomos.Api/       # ASP.NET Core API (C#)
│   │   ├── Features/     # MediatR handlers (lyrics CRUD, chat, search)
│   │   ├── Models/       # Qdrant document model, vectors
│   │   └── Services/     # Mp3Parser, EmbeddingService, LlmCache
│   └── Avomos.Ext/       # Chrome MV3 extension (Preact + TypeScript + SCSS)
│       ├── public/       # Web-accessible resources (page-interceptor.js)
│       ├── src/          # React components, store, API client
│       └── manifest.json
├── docker-compose.yml    # Qdrant + API
├── docker-compose.override.yml  # Local secrets (gitignored)
├── Dockerfile
└── volumes/              # Runtime data (gitignored)
    └── qdrant/           # Qdrant storage
```

### Stack

| Component | Tech |
|-----------|------|
| API | ASP.NET Core 10, MediatR, Semantic Kernel |
| Vector DB | Qdrant (gRPC + REST) |
| Embeddings | OpenRouter (`nvidia/llama-nemotron-embed-vl-1b-v2:free`) |
| Chat LLM | OpenAI-compatible (configurable via `Llm` settings) |
| Extension | Preact, Vite, @crxjs/vite-plugin, SCSS |
| Parser | TagLibSharp (ID3v2) |

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

# 3. Build extension
cd src/Avomos.Ext && npm run build
# Load dist/ as unpacked extension in Chrome
```

## Configuration

### API Keys (required)

Create `docker-compose.override.yml` in the project root:

```yaml
services:
  api:
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - OpenRouter__ApiKey=your-openrouter-key
      - OpenRouter__Endpoint=https://openrouter.ai/api/v1/
      - OpenRouter__Model=nvidia/llama-nemotron-embed-vl-1b-v2:free
      - Llm__ApiKey=your-llm-api-key
      - Llm__Endpoint=https://api.deepseek.com
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

### Riders (v0.3.0)
- `POST /riders/match` — match top 3 riders by buffer tracks + threshold, returns `canCreate` + `similarity`
- `POST /riders/create` — create a new rider via LLM (3 default + 3 matched custom riders, fallback to 6 defaults)
- `DELETE /riders/{id}` — delete a custom rider

## Data Flow

```
Suno feed → page-interceptor.js → CustomEvent → TrackStore → API backend → Qdrant
```

The extension intercepts Suno's API responses, extracts track metadata, and syncs it with the backend. Qdrant stores lyrics as vectors for semantic search.

## Changelog

### v0.3.0 — Riders

- **Rider system**: 6 default riders seeded into Qdrant on startup, matching dynamically by style similarity
- **LLM rider creation**: creates/replaces riders from buffer tracks via `POST /riders/create`
- **Rider deletion**: custom riders can be deleted via hover × in UI
- **Threshold slider**: configurable similarity threshold (0–1, step 0.05), persisted in localStorage
- **Track coherence check**: determines if buffer tracks are coherent enough for rider creation
- **Chat rider injection**: max 3 matched riders injected into LLM prompt context
- **Debounced rider sync**: 300ms debounce on buffer/threshold changes
- **Fixed TDZ bug**: useCallback ordering caused "Cannot access Z before initialization"
- **UI**: all rider controls on one line (names · threshold · +Rider)

## Notes

- Qdrant gRPC `ScrollAsync()` has a bug (ignores `limit`, returns 10). Use REST `/points/scroll` for scrolling.
- Embeddings are cached in `.cache/llm/embedding/` inside the container (lost on restart — temporary cache).
- Chat sessions are persisted in `volumes/chat/` (bind mount, survives restarts).
- Extension version auto-bumps patch on each `npm run build`.

## License

MIT
