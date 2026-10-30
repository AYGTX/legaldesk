import hmac
import threading
from collections.abc import Iterator

from fastapi import Header, HTTPException, Request
from sqlalchemy.orm import Session

from app.db import session_scope
from app.services.embeddings import Embedder, build_embedder

_embedder_lock = threading.Lock()


def get_db(request: Request) -> Iterator[Session]:
    for session in session_scope(request.app.state.session_factory):
        # Who is editing, read by the risk grid to record each rule's last editor (four-eyes rule, R-M11-2).
        account = getattr(request.state, "account", None)
        session.info["editor_id"] = account.id if account is not None else None
        yield session


def get_embedder(request: Request) -> Embedder:
    state = request.app.state
    if getattr(state, "embedder", None) is None:
        with _embedder_lock:
            if getattr(state, "embedder", None) is None:
                state.embedder = build_embedder(state.settings.embedder, state.settings.embedding_model)
    return state.embedder


def get_matcher(request: Request):
    """The clause matcher, built on first use; None when no semantic model is installed."""
    from app.services.matching import ExampleMatcher, load_semantic

    state = request.app.state
    if not hasattr(state, "matcher"):
        with _embedder_lock:
            if not hasattr(state, "matcher"):
                embedder = getattr(state, "matcher_embedder", None) or load_semantic(state.settings.embedding_model)
                state.matcher = (ExampleMatcher(embedder, state.settings.match_threshold, state.settings.match_margin)
                                 if embedder else None)
    return state.matcher


def require_api_key(request: Request, x_api_key: str | None = Header(default=None)) -> None:
    expected = request.app.state.settings.api_key
    if expected and not (x_api_key and hmac.compare_digest(x_api_key, expected)):
        raise HTTPException(status_code=401, detail="Missing or invalid X-API-Key.")
