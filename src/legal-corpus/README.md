# Legal Corpus Service

Verified, article-level law for LegalDesk. Admin UI (EN/FR) to manage texts and articles, plus a REST API for
search and citation checking. Retrieval approach adapted from Legal-Clause-Analyzer's RAG pipeline.

## Run

```bash
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt
.venv/Scripts/python -m uvicorn app.main:app --host 127.0.0.1 --port 8100
```

UI: http://127.0.0.1:8100 · API docs: http://127.0.0.1:8100/docs

Semantic (multilingual FR/AR/EN) search: `pip install -r requirements-semantic.txt`. Without it the service uses a
built-in lexical embedder. Rebuild the index after switching (`POST /api/v1/index/rebuild?force=true`).

## Configuration

| Variable | Default |
|---|---|
| `CORPUS_DATABASE_URL` | `sqlite:///data/corpus.db` |
| `CORPUS_EMBEDDER` | `auto` (`semantic`, `hashing`) |
| `CORPUS_EMBEDDING_MODEL` | `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` |
| `CORPUS_API_KEY` | unset: `/api/v1` is open. Set it and send `X-API-Key`. |

The UI has no login: bind to `127.0.0.1` only.

## Rules

- A text is law only once someone **verifies** it (needs articles and an official source URL). Editing an article,
  the reference or the source clears the sign-off.
- `/search` returns only verified, published texts unless `include_unverified` is set; results carry `is_verified`.
- `/citations/check` confirms a quote is word for word in the article (typography tolerated, `...` for elisions,
  min 15 characters).

## API

| Method | Path |
|---|---|
| GET/POST | `/api/v1/texts` |
| GET/PUT/DELETE | `/api/v1/texts/{id}` |
| PUT | `/api/v1/texts/{id}/status` |
| POST/DELETE | `/api/v1/texts/{id}/verify` |
| GET/POST | `/api/v1/texts/{id}/articles` |
| POST | `/api/v1/texts/{id}/articles/import` (split pasted law on "Article N" / "Art. N" / "الفصل N") |
| GET/PUT/DELETE | `/api/v1/articles/{id}` |
| POST | `/api/v1/search` |
| POST | `/api/v1/citations/check`, `/api/v1/citations/check-batch` |
| GET/POST | `/api/v1/index/status`, `/api/v1/index/rebuild` |

## Tests

```bash
.venv/Scripts/python -m pip install -r requirements-dev.txt
.venv/Scripts/python -m pytest
```
