import secrets
from urllib.parse import quote, urlencode, urlsplit

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles

from app import api, ui
from app.auth import LoginRequired
from app.config import BASE_DIR, Settings, load_settings
from app.db import make_session_factory
from app.services.corpus import NotFoundError, RuleError


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or load_settings()
    app = FastAPI(
        title="Harvey — verified legal reference",
        version="0.1.0",
        description="Verified, article-level law for LegalDesk: manage texts, search articles, check citations.",
    )
    app.state.settings = settings
    app.state.session_factory = make_session_factory(settings.database_url)
    app.state.embedder = None
    app.state.session_secret = settings.session_secret or secrets.token_hex(32)

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


app = create_app()
