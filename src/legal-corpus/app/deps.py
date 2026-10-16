import hmac
import threading
from collections.abc import Iterator

from fastapi import Header, HTTPException, Request
from sqlalchemy.orm import Session

from app.db import session_scope
from app.services.embeddings import Embedder, build_embedder

_embedder_lock = threading.Lock()


def get_db(request: Request) -> Iterator[Session]:
    yield from session_scope(request.app.state.session_factory)


def get_embedder(request: Request) -> Embedder:
    state = request.app.state
    if getattr(state, "embedder", None) is None:
        with _embedder_lock:
            if getattr(state, "embedder", None) is None:
                state.embedder = build_embedder(state.settings.embedder, state.settings.embedding_model)
    return state.embedder


def require_api_key(request: Request, x_api_key: str | None = Header(default=None)) -> None:
    expected = request.app.state.settings.api_key
    if expected and not (x_api_key and hmac.compare_digest(x_api_key, expected)):
        raise HTTPException(status_code=401, detail="Missing or invalid X-API-Key.")
