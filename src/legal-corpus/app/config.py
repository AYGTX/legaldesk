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


def load_settings() -> Settings:
    return Settings(
        database_url=os.getenv("CORPUS_DATABASE_URL", f"sqlite:///{BASE_DIR / 'data' / 'corpus.db'}"),
        embedder=os.getenv("CORPUS_EMBEDDER", "auto"),
        embedding_model=os.getenv(
            "CORPUS_EMBEDDING_MODEL", "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
        ),
        api_key=os.getenv("CORPUS_API_KEY") or None,
    )
