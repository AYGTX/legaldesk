"""Sign-in for the UI (rework spec R-M11-1). Each person has an account (app.services.accounts); the session is a signed,
HTTP-only cookie holding the account id, the sign-in stage and an expiry, so no session table is needed. The REST API
keeps its own API key; the few API calls that approve something also need a signed-in lawyer.

Stages: "password" (password right, the authenticator code still due) and "full". A full session may still be held to
a single page: a password that must be changed, or a lawyer who has not set up two-factor sign-in.
"""

import hashlib
import hmac
import time

from fastapi import HTTPException, Request

from app.models import HarveyUser

COOKIE = "harvey_session"
LIFETIME_SECONDS = 12 * 60 * 60
PENDING_SECONDS = 5 * 60
STAGE_PASSWORD, STAGE_FULL = "password", "full"

# Pages reachable without signing in.
_PUBLIC = ("/login", "/lang/", "/static/", "/health")
# Pages reachable while the password must change or two-factor must be set up.
_ACCOUNT = ("/account", "/logout")


class LoginRequired(Exception):
    def __init__(self, next_path: str) -> None:
        super().__init__("Sign-in required.")
        self.next_path = next_path


class AccountSetupRequired(Exception):
    """Redirects to /account until the password is changed or two-factor is set up."""


def _sign(secret: str, payload: str) -> str:
    return hmac.new(secret.encode(), payload.encode(), hashlib.sha256).hexdigest()


def make_session(secret: str, user_id: str, stage: str = STAGE_FULL, now: float | None = None) -> str:
    lifetime = LIFETIME_SECONDS if stage == STAGE_FULL else PENDING_SECONDS
    payload = f"{user_id}|{stage}|{int((now or time.time()) + lifetime)}"
    return f"{payload}|{_sign(secret, payload)}"


def read_session(secret: str, token: str | None, now: float | None = None) -> tuple[str, str] | None:
    """(user id, stage), or None when the cookie is missing, forged or expired."""
    if not token or token.count("|") != 3:
        return None
    user_id, stage, expires, signature = token.split("|")
    if not hmac.compare_digest(signature, _sign(secret, f"{user_id}|{stage}|{expires}")):
        return None
    if not expires.isdigit() or int(expires) < (now or time.time()):
        return None
    return user_id, stage


def _session(request: Request) -> tuple[str, str] | None:
    return read_session(request.app.state.session_secret, request.cookies.get(COOKIE))


def _load(request: Request, user_id: str) -> HarveyUser | None:
    with request.app.state.session_factory() as db:
        user = db.get(HarveyUser, user_id)
        if user is not None:
            db.expunge(user)
        return user


def current_user(request: Request) -> HarveyUser | None:
    """The fully signed-in, active account, or None."""
    session = _session(request)
    if session is None or session[1] != STAGE_FULL:
        return None
    user = _load(request, session[0])
    return user if user is not None and user.active else None


def pending_user(request: Request) -> HarveyUser | None:
    """The account whose password was right and whose authenticator code is still due."""
    session = _session(request)
    if session is None or session[1] != STAGE_PASSWORD:
        return None
    user = _load(request, session[0])
    return user if user is not None and user.active else None


def needs_setup(request: Request, user: HarveyUser) -> bool:
    settings = request.app.state.settings
    return user.must_change_password or (settings.require_totp and user.can_approve and not user.totp_enabled)


def require_login(request: Request) -> None:
    path = request.url.path
    if path.startswith(_PUBLIC):
        return
    user = current_user(request)
    if user is None:
        raise LoginRequired(path)
    request.state.account = user
    request.state.user = user.name  # templates and older code show the name
    if needs_setup(request, user) and not path.startswith(_ACCOUNT):
        raise AccountSetupRequired()
    # A reader looks; changing anything needs an editor or a lawyer. Approvals check for a lawyer themselves.
    if request.method == "POST" and not path.startswith(_ACCOUNT) and not user.can_edit:
        raise HTTPException(status_code=403, detail="Your account can read but not change anything.")


def require_lawyer(request: Request) -> HarveyUser:
    """The signed-in lawyer giving an approval (R-M11-2). Works for UI pages and for API calls made from a browser
    session; an API key alone never approves anything."""
    user = getattr(request.state, "account", None) or current_user(request)
    if user is None or not user.can_approve:
        raise HTTPException(status_code=403, detail="Only a lawyer's account can approve.")
    if needs_setup(request, user):
        raise HTTPException(status_code=403, detail="Set up two-factor sign-in before approving.")
    return user


def require_admin(request: Request) -> HarveyUser:
    user = getattr(request.state, "account", None)
    if user is None or not user.is_admin:
        raise HTTPException(status_code=403, detail="Only an administrator manages accounts.")
    return user
