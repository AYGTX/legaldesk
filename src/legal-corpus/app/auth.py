"""Sign-in for the UI. One administrator account, configured by environment variables; the session is a signed,
HTTP-only cookie (user name, expiry and an HMAC), so no session table is needed. The REST API keeps its own API key."""

import hashlib
import hmac
import time

from fastapi import Request

COOKIE = "harvey_session"
LIFETIME_SECONDS = 12 * 60 * 60
# Pages reachable without signing in.
_PUBLIC = ("/login", "/lang/")


class LoginRequired(Exception):
    def __init__(self, next_path: str) -> None:
        super().__init__("Sign-in required.")
        self.next_path = next_path


def _sign(secret: str, payload: str) -> str:
    return hmac.new(secret.encode(), payload.encode(), hashlib.sha256).hexdigest()


def make_session(secret: str, user: str, now: float | None = None) -> str:
    payload = f"{user}|{int((now or time.time()) + LIFETIME_SECONDS)}"
    return f"{payload}|{_sign(secret, payload)}"


def read_session(secret: str, token: str | None, now: float | None = None) -> str | None:
    if not token or token.count("|") != 2:
        return None
    user, expires, signature = token.split("|")
    if not hmac.compare_digest(signature, _sign(secret, f"{user}|{expires}")):
        return None
    if not expires.isdigit() or int(expires) < (now or time.time()):
        return None
    return user


def check_credentials(settings, login: str, password: str) -> bool:
    """The login is the user name or the e-mail address; both comparisons run every time, in constant time."""
    login = login.strip().lower()
    name_ok = hmac.compare_digest(login, settings.admin_user.lower())
    email_ok = hmac.compare_digest(login, settings.admin_email.lower())
    password_ok = hmac.compare_digest(password.encode(), settings.admin_password.encode())
    return (name_ok or email_ok) and password_ok


def current_user(request: Request) -> str | None:
    return read_session(request.app.state.session_secret, request.cookies.get(COOKIE))


def require_login(request: Request) -> None:
    if request.url.path.startswith(_PUBLIC):
        return
    user = current_user(request)
    if user is None:
        raise LoginRequired(request.url.path)
    request.state.user = user
