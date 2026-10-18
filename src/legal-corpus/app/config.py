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
    playbook_path: Path = BASE_DIR / "playbook.json"
    # The UI's administrator account. Change the defaults with CORPUS_ADMIN_* before any shared deployment.
    admin_user: str = "admin"
    admin_email: str = "aymen.hamadi.tn@gmail.com"
    admin_password: str = "admin"
    # Signs the session cookie. Without one, a random key is drawn at start-up and restarts sign everyone out.
    session_secret: str | None = None


def load_settings() -> Settings:
    return Settings(
        database_url=os.getenv("CORPUS_DATABASE_URL", f"sqlite:///{BASE_DIR / 'data' / 'corpus.db'}"),
        embedder=os.getenv("CORPUS_EMBEDDER", "auto"),
        embedding_model=os.getenv(
            "CORPUS_EMBEDDING_MODEL", "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
        ),
        api_key=os.getenv("CORPUS_API_KEY") or None,
        playbook_path=Path(os.getenv("CORPUS_PLAYBOOK", str(BASE_DIR / "playbook.json"))),
        admin_user=os.getenv("CORPUS_ADMIN_USER", "admin"),
        admin_email=os.getenv("CORPUS_ADMIN_EMAIL", "aymen.hamadi.tn@gmail.com"),
        admin_password=os.getenv("CORPUS_ADMIN_PASSWORD", "admin"),
        session_secret=os.getenv("CORPUS_SESSION_SECRET") or None,
    )
