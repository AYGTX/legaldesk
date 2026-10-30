# Legal Corpus Service

Verified, article-level law for LegalDesk. Admin UI (EN/FR) to manage texts and articles, plus a REST API for
search and citation checking. Retrieval approach adapted from Legal-Clause-Analyzer's RAG pipeline.

## Run

```bash
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt
.venv/Scripts/python -m uvicorn app.main:create_app --factory --host 127.0.0.1 --port 8100
```

Harvey refuses to start without `CORPUS_API_KEY` (24+ characters), in the environment or in the git-ignored
`data/.env`; give LegalDesk the same value as `LegalCorpus:ApiKey`. The database is migrated at start-up (Alembic,
`app/migrations`); an older database keeps its data and is upgraded.

UI: http://127.0.0.1:8100 · API docs: http://127.0.0.1:8100/docs

Semantic (multilingual FR/AR/EN) search: `pip install -r requirements-semantic.txt`. Without it the service uses a
built-in lexical embedder. Rebuild the index after switching (`POST /api/v1/index/rebuild?force=true`).

Contract field detection (`POST /api/v1/forms/detect`, used by the template designer's "Detect fields" button): fields already fillable in a PDF are always found; `pip install -r requirements-forms.txt` (CommonForms) adds a vision model (FFDNet-L) for static PDFs. Check the licences in that file before commercial use. A language model only tidies labels when `CORPUS_AI_PROVIDER=local`: contracts are never sent to Groq.

## Configuration

| Variable | Default |
|---|---|
| `CORPUS_DATABASE_URL` | `sqlite:///data/corpus.db` |
| `CORPUS_EMBEDDER` | `auto` (`semantic`, `hashing`) |
| `CORPUS_EMBEDDING_MODEL` | `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` |
| `CORPUS_API_KEY` | required. Every `/api/v1` call sends it as `X-API-Key`. |
| `CORPUS_ADMIN_EMAIL`, `CORPUS_ADMIN_USER`, `CORPUS_ADMIN_PASSWORD` | the first administrator, created on the first start only. The default password `admin` must be changed at first sign-in. |
| `CORPUS_SESSION_SECRET` | signs the session cookie; generated into `data/.session_secret` when unset. |
| `CORPUS_REQUIRE_TOTP` | `true`: lawyers sign in with an authenticator code. |
| `CORPUS_FOUR_EYES` | `true`: whoever last edited a risk cannot approve it. |
| `CORPUS_AI_PROVIDER` | `none` (`groq`, `local`): drafting help on law text only. |

## Accounts

Each person signs in with their own account (Accounts page, administrators only). **Readers** look; **editors**
import and edit law and risks; **lawyers** also verify texts and approve risks, model clauses and explanations. Every
approval records the lawyer's account, never a typed name.

## Rules

- A text is law only once a lawyer **verifies** it (needs articles and an official source URL). Editing an article,
  the reference or the source clears the sign-off.
- The risk grid is a working copy. Approving a risk freezes it as a numbered version; reviews and LegalDesk use each
  risk's latest approved version only, and every finding names it (`rule_version`). Editing a risk does not change
  reviews until a lawyer approves the change. Severities are Critical, High, Medium and Low; Critical blocks a
  contract in LegalDesk.
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

## Harvey for AI agents (MCP)

Harvey can be plugged into any assistant or agent that speaks the Model Context Protocol (Claude Desktop, IDE agents,
a firm's own agents). The agent then looks law up in Harvey instead of reciting it from memory, and checks its quotes.

Tools (read-only, verified and published law only):

| Tool | What it returns |
|---|---|
| `list_laws` | The verified texts, with reference, number of articles and official source |
| `search_law(query, top_k, as_of)` | The most relevant articles; `as_of` (YYYY-MM-DD) searches the wording in force that day |
| `get_article(law, number, as_of)` | One article word for word, with its citation, source and approved plain explanation |
| `check_quote(quote, article_id)` | Whether a quote appears word for word in that article of verified law |
| `explain_article(article_id, language)` | The lawyer-approved plain-language explanation, if any |

Install and run (stdio):

    pip install -r requirements-mcp.txt
    python -m app.mcp_server

Claude Desktop (`claude_desktop_config.json`), adjusting the paths:

```json
{
  "mcpServers": {
    "harvey": {
      "command": "C:\path\to\formbuilder\src\legal-corpus\.venv\Scripts\python.exe",
      "args": ["-m", "app.mcp_server"],
      "cwd": "C:\path\to\formbuilder\src\legal-corpus"
    }
  }
}
```
