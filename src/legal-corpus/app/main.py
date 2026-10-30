import secrets
from urllib.parse import quote, urlencode, urlsplit

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles

from app import api, ui
from app.services import playbook
from app.auth import AccountSetupRequired, LoginRequired
from app.services.llm import make_provider
from app.config import BASE_DIR, Settings, check_service_settings, load_settings
from app.db import make_session_factory
from app.services import accounts
from app.services.corpus import NotFoundError, RuleError


def _stored_secret(path) -> str:
    """Reads the session key kept next to the database, creating it on first start. The data folder is git-ignored."""
    if path is None:
        return secrets.token_hex(32)
    try:
        existing = path.read_text(encoding="utf-8").strip()
        if len(existing) >= 32:
            return existing
    except FileNotFoundError:
        pass
    key = secrets.token_hex(32)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(key, encoding="utf-8")
    return key


def create_app(settings: Settings | None = None) -> FastAPI:
    """Run with `uvicorn app.main:create_app --factory`. Settings passed in (tests) are taken as they are; settings
    read from the environment must pass check_service_settings."""
    if settings is None:
        settings = load_settings()
        check_service_settings(settings)
    app = FastAPI(
        title="Harvey — verified legal reference",
        version="0.1.0",
        description="Verified, article-level law for LegalDesk: manage texts, search articles, check citations.",
    )
    app.state.settings = settings
    app.state.session_factory = make_session_factory(settings.database_url)
    with app.state.session_factory() as db:
        playbook.bootstrap(db, settings.legacy_playbook_file, settings.seed_risks)
        if settings.seed_risks:
            playbook.seed_examples(db)
        accounts.bootstrap_admin(db, settings)
    app.state.embedder = None
    app.state.session_secret = settings.session_secret or _stored_secret(settings.session_secret_file)
    app.state.llm = make_provider(settings)

    app.include_router(api.router)
    app.include_router(ui.router)
    app.mount("/static", StaticFiles(directory=str(BASE_DIR / "app" / "static")), name="static")

    def _is_api(request: Request) -> bool:
        return request.url.path.startswith("/api/")

    def _back(request: Request, code: str) -> RedirectResponse:
        referer = request.headers.get("referer")
        path = urlsplit(referer).path if referer else "/"
        return RedirectResponse(f"{path or '/'}?{urlencode({'flash': code, 'kind': 'error'})}", status_code=303)

    @app.exception_handler(LoginRequired)
    async def login_required(request: Request, exc: LoginRequired):
        return RedirectResponse(f"/login?next={quote(exc.next_path)}", status_code=303)

    @app.exception_handler(AccountSetupRequired)
    async def account_setup_required(request: Request, exc: AccountSetupRequired):
        return RedirectResponse("/account", status_code=303)

    @app.exception_handler(NotFoundError)
    async def not_found(request: Request, exc: NotFoundError):
        if _is_api(request):
            return JSONResponse(status_code=404, content={"detail": str(exc)})
        return RedirectResponse("/", status_code=303)

    @app.exception_handler(RuleError)
    async def rule_error(request: Request, exc: RuleError):
        if _is_api(request):
            return JSONResponse(status_code=409, content={"code": exc.code, "detail": str(exc)})
        return _back(request, f"err_{exc.code}")

    @app.get("/health", tags=["health"])
    def health():
        return {"status": "ok"}

    return app
