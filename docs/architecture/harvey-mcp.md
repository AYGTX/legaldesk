# Harvey as an MCP server — how the legal reference backend was exposed to AI agents

This document records, in full, how Harvey (the Python legal-reference service in `src/legal-corpus`) was turned into
an **MCP server**, so that any AI assistant or agent that speaks the Model Context Protocol — Claude Desktop, IDE
agents, a firm's own agents — can ground its answers in **verified Tunisian law** instead of reciting law from memory.

It covers the design decisions, the code, the safeguards, how it is installed and tested, the problem met during the
Claude Desktop setup and how it was fixed, and how to extend or demo it.

---

## Contents

1. [Why an MCP server](#1-why-an-mcp-server)
2. [What MCP is, in short](#2-what-mcp-is-in-short)
3. [Where it sits in the architecture](#3-where-it-sits-in-the-architecture)
4. [Design decisions](#4-design-decisions)
5. [Step by step: what was built](#5-step-by-step-what-was-built)
6. [The five tools in detail](#6-the-five-tools-in-detail)
7. [Safeguards](#7-safeguards)
8. [The protocol exchange, end to end](#8-the-protocol-exchange-end-to-end)
9. [Installing it in Claude Desktop](#9-installing-it-in-claude-desktop)
10. [The configuration problem, and its fix](#10-the-configuration-problem-and-its-fix)
11. [Testing](#11-testing)
12. [Using it: example prompts and the demo](#12-using-it-example-prompts-and-the-demo)
13. [Troubleshooting](#13-troubleshooting)
14. [Limits and next steps](#14-limits-and-next-steps)
15. [Files and commits](#15-files-and-commits)

---

## 1. Why an MCP server

Legal AI tools are known to invent citations. A Stanford study of leading commercial legal research tools found they
**hallucinate on 17–33% of queries**. Harvey's whole design answers that problem: every text is verified by a lawyer
against its official source, and every quote is checked word for word.

Until this change, only LegalDesk could benefit from that corpus. Exposing it over MCP means **any AI agent** can:

- look an article up instead of remembering it,
- quote the exact wording,
- check a quote before presenting it as law,
- read a lawyer-approved plain-language explanation.

For the product story: *the corpus becomes a trusted source of Tunisian law for every AI tool the client uses, not
only for LegalDesk.*

## 2. What MCP is, in short

The **Model Context Protocol (MCP)** is an open standard for connecting AI assistants to external tools and data.

- The assistant (the **client**, e.g. Claude Desktop) starts or connects to an **MCP server**.
- The server **announces its tools**: a name, a description, and a JSON schema for the inputs.
- The model reads those descriptions and **decides when to call a tool**, with which arguments.
- The server runs the tool and **returns the result**, which the model reads before answering.

Messages are **JSON-RPC 2.0**. The transport used here is **stdio**: the client starts the server as a child process
and the two exchange messages over the process's standard input and output. There is no network port.

The model never sees the database. It can only call the functions the server chooses to expose.

## 3. Where it sits in the architecture

```
 Claude Desktop / any MCP client
        │  starts:  python -m app.mcp_server      (stdio, JSON-RPC)
        ▼
 ┌──────────────────────────────── Harvey (src/legal-corpus) ────────────────────────────────┐
 │  app/mcp_server.py  ── 5 read-only tools                                                  │
 │        │ reuses, without duplicating:                                                       │
 │        ├─ services/search.py        BM25 search, "as of a date"                            │
 │        ├─ services/citations.py     word-for-word quote check (citation guard)             │
 │        ├─ services/explanations.py  lawyer-approved plain explanations                     │
 │        ├─ models.py                 texts, articles, versions (wording_on)                 │
 │        └─ db.py / config.py         same SQLite database and settings as the web app      │
 │                                                                                           │
 │  data/corpus.db  ◄── also used by Harvey's web UI and its REST API (LegalDesk)            │
 └───────────────────────────────────────────────────────────────────────────────────────────┘
```

Three ways into the same verified corpus now exist:

| Entry point | Used by | Transport |
|---|---|---|
| Web UI | Lawyers (import, verify, risk grid) | HTTP, browser, sign-in |
| REST API `/api/v1` | LegalDesk (.NET) | HTTP, API key |
| **MCP server** | **AI assistants and agents** | **stdio, JSON-RPC** |

## 4. Design decisions

| Decision | Choice | Reason |
|---|---|---|
| Library | Official **MCP Python SDK** (`mcp`, v1.30), its `FastMCP` class | Maintained by the protocol's authors; FastMCP generates the tool schemas from Python type hints and docstrings |
| Language | Python, inside Harvey | Reuses Harvey's models and services directly; no second codebase |
| Data access | **Direct database access**, not calls to Harvey's REST API | Works even when Harvey's web app is not running; no API key or cookie to manage; one less moving part |
| Transport | **stdio** | What desktop assistants use to launch local servers; no port opened, nothing exposed on the network — suited to a bank |
| Scope | **Read-only**, verified and published law only | An agent must never change the corpus; drafts and unverified texts are notes, not law |
| Logic | **No duplication**: every tool calls an existing Harvey service | A fix to search or the quote check improves the MCP tools at once |
| Guidance | Server-level `instructions` plus careful tool docstrings | The model reads them: they push it to look up, quote exactly and verify |

## 5. Step by step: what was built

### Step 1 — Install the SDK

Into Harvey's own virtual environment:

```powershell
src\legal-corpus\.venv\Scripts\python.exe -m pip install "mcp>=1.2,<2"
```

Recorded as an optional requirements file, `src/legal-corpus/requirements-mcp.txt`:

```text
-r requirements.txt
# Harvey as an MCP server for AI assistants and agents: python -m app.mcp_server
mcp>=1.2,<2
```

### Step 2 — Create the server and its instructions

`src/legal-corpus/app/mcp_server.py`:

```python
from mcp.server.fastmcp import FastMCP

INSTRUCTIONS = """Verified Tunisian law (codes, laws, decrees), article by article, each text checked by a lawyer
against its official source. Use these tools instead of recalling law from memory: find articles with search_law or
get_article, quote only the text they return, and confirm any quote with check_quote before presenting it as law.
Cite articles with the 'citation' field. An explanation from explain_article is a reading aid approved by a lawyer,
never a quote. If the tools return nothing, say the corpus does not cover it rather than answering from memory."""

mcp = FastMCP("harvey-legal-reference", instructions=INSTRUCTIONS)
```

- `"harvey-legal-reference"` is the server name the client displays.
- `instructions` is sent to the client when it connects; Claude reads it as guidance for using the tools.

### Step 3 — Open the database the way the web app does

```python
from app.config import load_settings
from app.db import make_session_factory

_factory = None


def _db() -> Session:
    global _factory
    if _factory is None:
        _factory = make_session_factory(load_settings().database_url)
    return _factory()
```

- `load_settings()` reads the same settings as the web app (including `data/.env`), so the server uses the same
  database: `src/legal-corpus/data/corpus.db`.
- The session factory is created on first use and reused; each tool opens a session with `with _db() as db:` and
  closes it when done.
- `make_session_factory` also adds any missing columns to an older database, as the web app does on start-up.

### Step 4 — One filter for "verified law"

```python
def _verified():
    return select(LegalText).where(LegalText.verified_at.is_not(None), LegalText.status == TextStatus.Published)
```

Every tool that lists or looks up texts goes through this filter: **verified by a lawyer and published**. A draft,
an unverified import or an archived text is never returned.

### Step 5 — Declare each tool

A tool is a plain function with the `@mcp.tool()` decorator. FastMCP builds the tool from it:

| From the function | Becomes |
|---|---|
| Function name | Tool name (`get_article`) |
| Docstring | Tool description, read by the model to decide when to call it |
| Type hints (`law: str`, `as_of: str \| None = None`) | Input JSON schema, with required and optional parameters |
| Returned `dict` / `list` | The JSON result sent back to the model |

Example:

```python
@mcp.tool()
def get_article(law: str, number: str, as_of: str | None = None) -> dict:
    """One article of a verified law, word for word. law: the reference or title, or a distinctive part of it
    (e.g. "2016-48", "Code des obligations et des contrats"). number: e.g. "81", "2 bis", "premier"."""
```

The schema the client receives (as shown by Claude when it loaded the tool):

```json
{
  "name": "get_article",
  "description": "One article of a verified law, word for word. law: the reference or title, ...",
  "parameters": {
    "type": "object",
    "properties": {
      "law": {"type": "string"},
      "number": {"type": "string"},
      "as_of": {"default": null}
    },
    "required": ["law", "number"]
  }
}
```

Docstrings were written as instructions for the model: what the tool returns, what the arguments look like, with
examples ("2016-48", "2 bis", "premier"), and when to use it ("Call it before presenting any quote as law").

### Step 6 — Start over stdio

```python
if __name__ == "__main__":
    mcp.run()   # stdio transport by default
```

Run from `src/legal-corpus`:

```powershell
.\.venv\Scripts\python.exe -m app.mcp_server
```

Started by hand it waits silently on stdin: that is normal, it expects a client.

### Step 7 — Document, test, install

- `src/legal-corpus/README.md`: a "Harvey for AI agents (MCP)" section with the tools, install and Claude Desktop
  configuration.
- `src/legal-corpus/tests/test_mcp_server.py`: unit tests (see [Testing](#11-testing)).
- `src/legal-corpus/tools/install-claude-desktop-mcp.ps1`: registers the server in Claude Desktop (see
  [Installing](#9-installing-it-in-claude-desktop)).

## 6. The five tools in detail

### `list_laws()`

The verified, published texts available.

- **Does:** queries `_verified()` with articles loaded, ordered by title.
- **Returns:** for each text — `title`, `reference`, `category`, `language`, `articles` (count), `source_url`,
  `verified_by`.
- **Use:** lets the agent know what the corpus covers before searching.

### `search_law(query, top_k=5, as_of=None)`

The articles most relevant to a question or a contract clause.

- **Does:** calls Harvey's existing `search()` service with a `SearchIn(query, top_k, as_of)` — the same BM25 keyword
  ranking (French stop-words, title boost) that LegalDesk uses for "Droit applicable". `top_k` is clamped to 1–20.
- **`as_of` (YYYY-MM-DD):** searches the wording **in force on that day**, using article versions and their "in force
  from" dates — e.g. a contract's signing date.
- **Returns:** for each hit — `citation`, `article_id`, `article_number`, `text` (full article), `excerpt`, `law`,
  `source_url`, `score`.
- **Respects:** verified-and-published only; articles a lawyer excluded from "applicable law" are left out, as in
  LegalDesk.

### `get_article(law, number, as_of=None)`

One article, word for word.

- **`law`:** the reference or title, or a distinctive part of it — matched case-insensitively against
  `reference + title` of verified texts ("2016-48", "Code des obligations et des contrats").
- **`number`:** normalised with Harvey's splitter rules — `"premier"` → `"1"`, `"2 bis"` kept as is.
- **Ambiguity:** if several verified texts match, it returns `found: false` with the `candidates` instead of guessing;
  if none has the article, it returns `found: false` with the candidate texts.
- **`as_of`:** returns the wording that applied that day (`wording_on`), or says the article was not yet in force.
- **Returns:** `found`, `citation`, `article_id`, `text`, `in_force_from`, `source_url`, `verified_by`,
  `plain_explanation_fr` (only a lawyer-approved explanation, else `null`).

Live result from the real corpus:

```json
{
  "found": true,
  "citation": "Loi n° 2016-48 du 11 juillet 2016, art. 81",
  "article_id": "c25c1a3f-3adb-4db1-a74f-1570fae89861",
  "text": "La banque ou l’établissement financier peuvent recourir à l’externalisation de certaines opérations liées à son activité, ...",
  "in_force_from": null,
  "source_url": "https://www.cmf.tn/sites/default/files/pdfs/reglementation/textes-reference/loi2016_48_fr.pdf",
  "verified_by": "H. Aymen",
  "plain_explanation_fr": null
}
```

### `check_quote(quote, article_id)`

Whether a quote appears in the article of verified law.

- **Does:** calls Harvey's existing `check_citation()` — the same citation guard LegalDesk uses. Typography
  differences (apostrophes, spaces) and "[…]" elisions are tolerated; quotes under 15 characters are refused as
  proving nothing.
- **Returns:** `found`, `verified_law`, `citation`, `reason`.
- **Use:** the instructions and the docstring tell the model to call it **before** presenting any quote as law, and
  never to cite a quote that fails.

### `explain_article(article_id, language="fr")`

The plain-language explanation of an article.

- **Does:** returns the explanation **only if a lawyer approved it** for the article's current wording. Drafts and
  out-of-date explanations are never returned.
- **Returns:** `found`, `summary`, `in_practice`, `approved_by`; or `found: false` with a reason.
- **Use:** a reading aid. The instructions state it is never a quote.

## 7. Safeguards

| Risk | Safeguard |
|---|---|
| An agent quotes a draft or unverified text as law | Every lookup goes through `_verified()`: verified **and** published only. Tested |
| An agent changes the corpus | No write tool exists; the server is read-only by construction |
| An agent invents or rephrases a quote | `check_quote` offered as a tool; instructions require it before citing; invented quotes fail. Tested |
| An agent picks the wrong law on a vague reference | `get_article` refuses ambiguity and returns the candidates |
| An agent answers from memory when the corpus has nothing | Instructions: say the corpus does not cover it |
| An unreviewed AI summary is taken as law | `explain_article` returns lawyer-approved explanations only, labelled as reading aids |
| The law changed since the contract was signed | `as_of` returns the wording in force on that date |
| Data leaves the machine | stdio only, no port; the server reads the local database; nothing is sent anywhere |
| A different behaviour from LegalDesk | Same services (search, citation guard, explanations) — one source of truth |

## 8. The protocol exchange, end to end

What happens when a user asks Claude Desktop *« Que dit l'article 81 de la loi 2016-48 ? »*:

1. **Start-up.** Claude Desktop reads its configuration and starts
   `python -m app.mcp_server` as a child process.
2. **Handshake.** The client sends `initialize`; the server answers with its name and the `instructions`.
3. **Discovery.** The client sends `tools/list`; the server returns the five tools with their descriptions and
   input schemas.
4. **Decision.** The model reads the question and the tool descriptions, and chooses
   `get_article(law="2016-48", number="81")`.
5. **Call.** The client sends `tools/call` with those arguments. FastMCP validates them against the schema and calls
   the Python function.
6. **Lookup.** The function opens a session on `corpus.db`, finds the verified text whose reference contains
   "2016-48", and returns article 81's wording, citation and source.
7. **Verification.** If the model quotes a passage, it calls `check_quote(quote, article_id)`; the citation guard
   confirms it appears word for word.
8. **Answer.** The model answers with the exact wording and the citation
   « Loi n° 2016-48 du 11 juillet 2016, art. 81 », from the corpus rather than from memory.

## 9. Installing it in Claude Desktop

### What the configuration needs

Claude Desktop reads MCP servers from `claude_desktop_config.json` under `mcpServers`:

```json
{
  "mcpServers": {
    "harvey": {
      "command": "C:\\Users\\aymen\\Downloads\\formbuilder\\src\\legal-corpus\\.venv\\Scripts\\python.exe",
      "args": ["-m", "app.mcp_server"],
      "cwd": "C:\\Users\\aymen\\Downloads\\formbuilder\\src\\legal-corpus",
      "env": { "PYTHONPATH": "C:\\Users\\aymen\\Downloads\\formbuilder\\src\\legal-corpus" }
    }
  }
}
```

| Field | Why |
|---|---|
| `command` | Harvey's own Python, which has the `mcp` package and Harvey's dependencies |
| `args` | Run the server as a module, so `from app...` imports resolve |
| `cwd` | Start in Harvey's folder |
| `env.PYTHONPATH` | Some Claude Desktop versions ignore `cwd`; this makes the `app` package importable from any folder (checked) |

On this Windows machine Claude Desktop is a packaged app; its configuration lives at
`%APPDATA%\Claude\claude_desktop_config.json` (mirrored under
`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\`).

### The install script

`src/legal-corpus/tools/install-claude-desktop-mcp.ps1`:

- **refuses to run while Claude Desktop is running** (see the next section),
- finds Harvey's Python and folder from its own location,
- updates every configuration file found in the two locations above,
- keeps a backup next to each (`claude_desktop_config.json.bak`),
- touches **nothing but** `mcpServers.harvey`, and checks the result is valid JSON.

Usage:

1. Quit Claude Desktop completely (tray icon → right-click → **Quit**).
2. From a normal PowerShell window:

   ```powershell
   cd C:\Users\aymen\Downloads\formbuilder
   powershell -ExecutionPolicy Bypass -File src\legal-corpus\tools\install-claude-desktop-mcp.ps1
   ```

3. Start Claude Desktop. In a chat's tools menu, **harvey** appears with its five tools.

## 10. The configuration problem, and its fix

**What happened.** The first time, the `harvey` entry was added to `claude_desktop_config.json` **while Claude Desktop
was running**. On quitting, Claude Desktop saved its own in-memory copy of its preferences over the file, which erased
the entry. After the restart, nothing was loaded. Checking the file confirmed `mcpServers` was gone.

**The fix.** The entry has to be written while Claude Desktop is closed. Because the conversation that built Harvey
runs inside Claude Desktop, it could not close the app itself; hence the install script, which the user runs from an
ordinary PowerShell window, and which refuses to run while the app is open so the mistake cannot repeat.

**Before running it, the script was tested** on a copy of the live configuration: `harvey` was added and every other
setting compared identical.

**Result.** After running it and restarting, the five Harvey tools appeared, and a live call to
`get_article("2016-48", "81")` returned the verified wording.

## 11. Testing

### Unit tests — `src/legal-corpus/tests/test_mcp_server.py`

Run against a temporary **in-memory** database seeded with one **verified, published** law and one **draft**:

| Test | Checks |
|---|---|
| `test_only_verified_law_is_listed_and_found` | `list_laws` and `search_law` return the verified law only, never the draft |
| `test_get_article_and_check_quote` | `get_article` returns the exact wording and citation; a real quote passes `check_quote`, an invented one fails |
| `test_unknown_or_unverified_articles_are_not_returned` | a draft's article, a missing article and an unknown id are all refused |
| `test_the_server_exposes_the_tools` | exactly the five expected tools are registered |

The tests replace the module's session factory (`monkeypatch.setattr(mcp_server, "_factory", factory)`), so they
never touch the real database.

```powershell
cd src\legal-corpus
.\.venv\Scripts\python.exe -m pytest -q tests\test_mcp_server.py
```

### End-to-end test over stdio

An MCP **client** from the same SDK started the server as Claude Desktop would and called the tools on the real
corpus:

```python
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

params = StdioServerParameters(command=sys.executable, args=["-m", "app.mcp_server"], cwd=".")
async with stdio_client(params) as (read, write):
    async with ClientSession(read, write) as session:
        await session.initialize()
        await session.list_tools()
        await session.call_tool("get_article", {"law": "2016-48", "number": "81"})
        await session.call_tool("check_quote", {"quote": "...", "article_id": "..."})
        await session.call_tool("search_law", {"query": "transfert des données personnelles à l'étranger"})
```

Observed results:

- tools listed: `list_laws`, `search_law`, `get_article`, `check_quote`, `explain_article`;
- `get_article("2016-48", "81")` → *Loi n° 2016-48 du 11 juillet 2016, art. 81*, word for word;
- `check_quote` on a real passage → `found: true`, `verified_law: true`;
- `search_law` on data transfers abroad → *Loi organique n° 2004-63, art. 52*, *art. 90*, and loi 2000-83.

### Live check in Claude

After installation, the tool was called from a Claude session: the article came back with its CMF source and
`verified_by: "H. Aymen"`.

## 12. Using it: example prompts and the demo

Prompts for a Claude Desktop chat:

- « Que dit l'article 81 de la loi 2016-48 ? Cite le texte exact et vérifie la citation. »
- « Un prestataire peut-il héberger les données des clients d'une banque à l'étranger ? Appuie-toi uniquement sur la
  loi vérifiée et cite les articles exacts. »
- « Quelles lois tunisiennes vérifiées sont disponibles ? »
- « Quelle était la rédaction de l'article X de la loi Y au 1er janvier 2015 ? » (uses `as_of`)

What to point out to a jury:

1. Claude **calls the tools** (visible in the chat) instead of answering from memory.
2. The answer quotes the **exact wording** with the **official citation**.
3. Claude **checks its quotes** with `check_quote`.
4. The law shown was **verified by a lawyer**, with its official source.

One line: *« N'importe quel agent IA peut désormais s'appuyer sur la loi tunisienne vérifiée de Harvey, et chaque
citation est contrôlée mot à mot. »*

## 13. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| **harvey** not listed in Claude Desktop | Entry erased because it was added while Claude Desktop ran | Quit Claude Desktop, run the install script, restart |
| Install script says Claude is still running | The window was closed but the app still runs in the tray | Tray icon → right-click → **Quit** |
| Server listed but fails to start | Wrong path, or `mcp` not installed in Harvey's venv | Check paths in the config; `pip install -r requirements-mcp.txt` |
| `ModuleNotFoundError: app` | `cwd` ignored by the client | `env.PYTHONPATH` must point to `src\legal-corpus` (the script sets it) |
| A law is never returned | It is not verified or not published | In Harvey: verify the text, set status to Published |
| An article is missing from `search_law` but present in `get_article` | A lawyer excluded it from "applicable law" | Intended; include it again on the text page if needed |
| Errors to inspect | — | Claude Desktop → **Settings → Developer** shows MCP servers and their logs |
| Running by hand seems to hang | stdio servers wait for a client | Normal; stop with Ctrl+C |

## 14. Limits and next steps

**Current limits**

- Search ranks by keywords (BM25). The meaning-based matcher used for contract clauses is not exposed as a tool.
- Explanations are served in French by `get_article`; `explain_article` takes `fr` or `en`.
- stdio only: one local client per server process. Several users would need an HTTP transport with authentication.
- Arabic texts are supported by the corpus, but the tools' descriptions are in English and examples in French.

**Possible next steps**

- A `find_cross_references(article_id)` tool, using Harvey's cross-reference detection.
- A `missing_laws()` tool listing texts the corpus cites but does not hold.
- MCP **resources** (read-only documents) for whole laws, alongside the tools.
- An HTTP (streamable) transport with the API key, for agents running on other machines.
- Logging of tool calls for audit ("which agent asked for which article, when").

## 15. Files and commits

| File | Role |
|---|---|
| `src/legal-corpus/app/mcp_server.py` | The MCP server: instructions, five tools, database access |
| `src/legal-corpus/requirements-mcp.txt` | The optional dependency (`mcp`) |
| `src/legal-corpus/tests/test_mcp_server.py` | Unit tests |
| `src/legal-corpus/tools/install-claude-desktop-mcp.ps1` | Registers the server in Claude Desktop safely |
| `src/legal-corpus/README.md` | "Harvey for AI agents (MCP)" section |
| `docs/architecture/harvey-mcp.md` | This document |

Commits:

- `7bac69f` — Harvey: MCP server so AI agents can ground answers in verified law
- `98b31f3` — Harvey: script to register the MCP server in Claude Desktop

Services reused (unchanged) by the server: `app/services/search.py`, `app/services/citations.py`,
`app/services/explanations.py`, `app/models.py` (`wording_on`), `app/db.py`, `app/config.py`.
