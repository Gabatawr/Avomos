# Avomos

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
| Parser | TagLibSharp (ID3v2) |

### Chat Tools

LLM responds via one of four tools (prompt-based JSON):

- **reply** — casual conversation, questions
- **simple** — Simple mode generation prompt (`Idea: ... Styles: ...`)
- **advanced** — Advanced mode (lyrics + styles + title)
- **hooks** — Creative ideas as short clickable buttons (1-3 words)

## Quick Start

```bash
# 1. Start Qdrant
docker compose up -d qdrant

# 2. Set API keys (use appsettings.json or user secrets)
#    - OpenRouter:ApiKey (embeddings)
#    - Llm:ApiKey (chat) — any OpenAI-compatible provider

# 3. Run API
cd src/Avomos.Api && dotnet run

# 4. Build extension
cd src/Avomos.Ext && npm run build
# Load dist/ as unpacked extension in Chrome
```

## Configuration

```json
{
  "Llm": {
    "ApiKey": "your-api-key",
    "Endpoint": "https://api.deepseek.com",
    "ChatModelId": "deepseek-v4-flash"
  },
  "OpenRouter": {
    "ApiKey": "your-openrouter-key",
    "Endpoint": "https://openrouter.ai/api/v1/",
    "Model": "nvidia/llama-nemotron-embed-vl-1b-v2:free"
  }
}
```

The `Llm` section supports any OpenAI-compatible API — just change Endpoint and ChatModelId.

## API Endpoints

### Lyrics
- `GET /tracks/{originId}` — get by OriginId
- `GET /tracks/search?query=...` — semantic search
- `POST /tracks/upsert` — import tracks
- `POST /tracks/metadata` — update metadata
- `DELETE /tracks/{originId}` — delete track

### Chat
- `POST /chat` — LLM chat with buffer context (returns reply/simple/advanced/hooks)
- `GET/POST/DELETE /chat/session` — session management

## Data Flow

```
Suno feed → page-interceptor.js → CustomEvent → TrackStore → API backend → Qdrant
```

The extension intercepts Suno's API responses, extracts track metadata, and syncs it with the backend. Qdrant stores lyrics as vectors for semantic search.

## Notes

- Qdrant gRPC `ScrollAsync()` has a bug (ignores `limit`, returns 10). Use REST `/points/scroll` for scrolling.
- Embeddings are cached in `.cache/llm/embedding/` to avoid redundant API calls.
- Extension version auto-bumps patch on each `npm run build`.

## License

MIT
