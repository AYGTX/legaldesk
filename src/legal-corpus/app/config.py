import os
from dataclasses import dataclass
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent


@dataclass(frozen=True)
class Settings:
    database_url: str
    # "auto" uses sentence-transformers when installed and falls back to the hashing embedder otherwise.
    embedder: str
    embedding_model: str
    api_key: str | None
    # The risk grid lives in the database. A playbook.json from an older Harvey is imported once into an empty grid.
    legacy_playbook_file: Path | None = BASE_DIR / "data" / "playbook.json"
    # On an empty grid, create one empty risk per risk LegalDesk detects.
    seed_risks: bool = True
    # The UI's administrator account. Change the defaults with CORPUS_ADMIN_* before any shared deployment.
    admin_user: str = "admin"
    admin_email: str = "aymen.hamadi.tn@gmail.com"
    admin_password: str = "admin"
    # Signs the session cookie. Without one, a key is generated once and kept in session_secret_file, so restarts
    # keep people signed in; with neither (tests), a random key is drawn at start-up.
    session_secret: str | None = None
    session_secret_file: Path | None = None
    # Optional language model, used only on law texts (never contracts): "none" (default), "groq" or "local".
    ai_provider: str = "none"
    groq_api_key: str | None = None
    groq_model: str = "openai/gpt-oss-120b"
    local_llm_url: str = "http://localhost:11434"
    local_llm_model: str = "qwen2.5:7b"
    # Meaning-based clause matching (needs sentence-transformers; off without it).
    match_threshold: float = 0.60
    match_margin: float = 0.03
    # Lawyers must sign in with an authenticator code (R-M11-1). Tests turn it off.
    require_totp: bool = True
    # Whoever last edited a rule cannot approve it (R-M11-2). A practice with a single lawyer may turn it off.
    four_eyes: bool = True


ENV_FILE = BASE_DIR / "data" / ".env"


def _load_env_file(path: Path = ENV_FILE) -> None:
    """KEY=value lines from the git-ignored data/.env, for secrets such as CORPUS_GROQ_API_KEY. A variable already set
    in the environment wins, so a shell can still override the file."""
    if not path.exists():
        return
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


def load_settings() -> Settings:
    _load_env_file()
    return Settings(
        database_url=os.getenv("CORPUS_DATABASE_URL", f"sqlite:///{BASE_DIR / 'data' / 'corpus.db'}"),
        # Search ranks by keywords (BM25) unless asked otherwise: on this corpus it beat hashed vectors, and switching
        # it silently when the semantic model is installed for clause matching would change every search result.
        embedder=os.getenv("CORPUS_EMBEDDER", "hashing"),
        embedding_model=os.getenv(
            "CORPUS_EMBEDDING_MODEL", "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
        ),
        api_key=os.getenv("CORPUS_API_KEY") or None,
        admin_user=os.getenv("CORPUS_ADMIN_USER", "admin"),
        admin_email=os.getenv("CORPUS_ADMIN_EMAIL", "aymen.hamadi.tn@gmail.com"),
        admin_password=os.getenv("CORPUS_ADMIN_PASSWORD", "admin"),
        session_secret=os.getenv("CORPUS_SESSION_SECRET") or None,
        session_secret_file=BASE_DIR / "data" / ".session_secret",
        # Off unless asked for (rework spec R-M11-13): the model only drafts from law text, for a lawyer to approve.
        ai_provider=os.getenv("CORPUS_AI_PROVIDER", "none"),
        groq_api_key=os.getenv("CORPUS_GROQ_API_KEY") or None,
        groq_model=os.getenv("CORPUS_GROQ_MODEL", "openai/gpt-oss-120b"),
        local_llm_url=os.getenv("CORPUS_LOCAL_LLM_URL", "http://localhost:11434"),
        local_llm_model=os.getenv("CORPUS_LOCAL_LLM_MODEL", "qwen2.5:7b"),
        match_threshold=float(os.getenv("CORPUS_MATCH_THRESHOLD", "0.60")),
        match_margin=float(os.getenv("CORPUS_MATCH_MARGIN", "0.03")),
        require_totp=os.getenv("CORPUS_REQUIRE_TOTP", "true").lower() != "false",
        four_eyes=os.getenv("CORPUS_FOUR_EYES", "true").lower() != "false",
    )


MIN_API_KEY = 24


def check_service_settings(settings: Settings) -> None:
    """What the running service needs before it starts (R-M11-9): every API caller must present a key."""
    if not settings.api_key or len(settings.api_key) < MIN_API_KEY:
        raise RuntimeError(
            f"CORPUS_API_KEY is missing or shorter than {MIN_API_KEY} characters. Harvey refuses to start with an open "
            "API: set it in the environment or in data/.env, and give LegalDesk the same value (LegalCorpus:ApiKey).")
