"""Harvey accounts (rework spec R-M11-1, R-M11-2): passwords hashed with scrypt, optional TOTP (required for lawyers
by default), lockout after repeated failures. Approvals record the account that gave them, never a typed name.
"""

import base64
import hashlib
import hmac
import secrets
import struct
import time
from datetime import datetime, timedelta, timezone
from urllib.parse import quote

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.models import HarveyUser, Role
from app.services.corpus import RuleError

# scrypt: memory-hard, in the standard library (no extra package). n=2^15, r=8, p=1 is OWASP's recommended minimum.
_N, _R, _P, _DKLEN = 2 ** 15, 8, 1, 64
MAX_FAILED_LOGINS = 5
LOCKOUT = timedelta(minutes=15)
MIN_PASSWORD = 10
TOTP_ISSUER = "Harvey"


def hash_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    digest = hashlib.scrypt(password.encode(), salt=salt, n=_N, r=_R, p=_P, dklen=_DKLEN, maxmem=64 * 1024 * 1024)
    return f"scrypt${_N}${_R}${_P}${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


def verify_password(password: str, stored: str) -> bool:
    try:
        _, n, r, p, salt, digest = stored.split("$")
        expected = base64.b64decode(digest)
        actual = hashlib.scrypt(password.encode(), salt=base64.b64decode(salt), n=int(n), r=int(r), p=int(p),
                                dklen=len(expected), maxmem=64 * 1024 * 1024)
        return hmac.compare_digest(actual, expected)
    except (ValueError, TypeError):
        return False


# Same work for an unknown login as for a known one, so timing does not reveal who has an account.
_DUMMY_HASH = hash_password(secrets.token_hex(16))


def check_password_rules(password: str) -> None:
    if (len(password) < MIN_PASSWORD or not any(c.islower() for c in password) or not any(c.isupper() for c in password)
            or not any(c.isdigit() for c in password) or all(c.isalnum() for c in password)):
        raise RuleError("weak_password", "At least 10 characters, with upper and lower case, a digit and a symbol.")


# --------------------------------------------------------------------------- TOTP (RFC 6238, 30 s, 6 digits)

def new_totp_secret() -> str:
    return base64.b32encode(secrets.token_bytes(20)).decode().rstrip("=")


def totp_code(secret: str, step: int) -> str:
    key = base64.b32decode(secret + "=" * (-len(secret) % 8))
    digest = hmac.new(key, struct.pack(">Q", step), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    value = struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF
    return f"{value % 1_000_000:06d}"


def verify_totp(secret: str | None, code: str, now: float | None = None) -> bool:
    """Accepts the current 30-second step and one either side, for clock drift."""
    code = code.replace(" ", "").strip()
    if not secret or len(code) != 6 or not code.isdigit():
        return False
    step = int((now or time.time()) // 30)
    return any(hmac.compare_digest(totp_code(secret, step + d), code) for d in (-1, 0, 1))


def totp_uri(user: HarveyUser) -> str:
    label = quote(f"{TOTP_ISSUER}:{user.email}")
    return f"otpauth://totp/{label}?secret={user.totp_secret}&issuer={TOTP_ISSUER}&digits=6&period=30"


# --------------------------------------------------------------------------- accounts

def count(db: Session) -> int:
    return db.scalar(select(func.count()).select_from(HarveyUser)) or 0


def by_email(db: Session, email: str) -> HarveyUser | None:
    return db.scalar(select(HarveyUser).where(func.lower(HarveyUser.email) == email.strip().lower()))


def create(db: Session, email: str, name: str, role: Role, password: str, *, is_admin: bool = False,
           bar_reference: str | None = None, must_change_password: bool = True) -> HarveyUser:
    email = email.strip()
    if "@" not in email or not name.strip():
        raise RuleError("invalid", "An account needs a name and an e-mail address.")
    if by_email(db, email) is not None:
        raise RuleError("duplicate_user", "An account with this e-mail already exists.")
    check_password_rules(password)
    user = HarveyUser(email=email, name=name.strip(), role=role, is_admin=is_admin,
                      bar_reference=(bar_reference or "").strip() or None, password_hash=hash_password(password),
                      must_change_password=must_change_password)
    db.add(user)
    db.commit()
    return user


def bootstrap_admin(db: Session, settings) -> None:
    """The first start creates one administrator lawyer from CORPUS_ADMIN_*; later starts ignore those variables.
    The default password only works once: it must be changed at first sign-in."""
    if count(db):
        return
    password = settings.admin_password
    weak_default = password == "admin"
    user = HarveyUser(email=settings.admin_email, name=settings.admin_user or "Administrator", role=Role.Lawyer,
                      is_admin=True, password_hash=hash_password(password), must_change_password=weak_default)
    db.add(user)
    db.commit()


def authenticate(db: Session, login: str, password: str) -> HarveyUser | None:
    """The user, or None: unknown login, wrong password, deactivated and locked accounts look the same."""
    now = datetime.now(timezone.utc)
    user = by_email(db, login)
    if user is None:
        verify_password(password, _DUMMY_HASH)
        return None
    locked = user.locked_until is not None and _aware(user.locked_until) > now
    ok = verify_password(password, user.password_hash)
    if locked or not user.active or not ok:
        if not locked and not ok:
            user.failed_logins += 1
            if user.failed_logins >= MAX_FAILED_LOGINS:
                user.locked_until, user.failed_logins = now + LOCKOUT, 0
            db.commit()
        return None
    user.failed_logins, user.locked_until, user.last_login_at = 0, None, now
    db.commit()
    return user


def change_password(db: Session, user: HarveyUser, current: str, new: str) -> None:
    if not verify_password(current, user.password_hash):
        raise RuleError("wrong_password", "The current password is not right.")
    if current == new:
        raise RuleError("same_password", "Choose a password different from the current one.")
    check_password_rules(new)
    user.password_hash, user.must_change_password = hash_password(new), False
    db.commit()


def start_totp(db: Session, user: HarveyUser) -> HarveyUser:
    if user.totp_enabled:
        raise RuleError("totp_on", "Two-factor sign-in is already on.")
    user.totp_secret = new_totp_secret()
    db.commit()
    return user


def confirm_totp(db: Session, user: HarveyUser, code: str) -> None:
    if not verify_totp(user.totp_secret, code):
        raise RuleError("bad_code", "The code does not match. Check the time on your phone and try again.")
    user.totp_enabled = True
    db.commit()


def update(db: Session, actor: HarveyUser, user_id: str, *, role: Role, is_admin: bool, active: bool,
           bar_reference: str | None) -> HarveyUser:
    user = db.get(HarveyUser, user_id)
    if user is None:
        raise RuleError("unknown_user", "No such account.")
    if user.id == actor.id and (not is_admin or not active):
        raise RuleError("self_lockout", "You cannot remove your own administrator rights or deactivate yourself.")
    if user.is_admin and (not is_admin or not active):
        admins = db.scalar(select(func.count()).select_from(HarveyUser)
                           .where(HarveyUser.is_admin.is_(True), HarveyUser.active.is_(True))) or 0
        if admins <= 1:
            raise RuleError("last_admin", "Harvey must keep at least one active administrator.")
    user.role, user.is_admin, user.active = role, is_admin, active
    user.bar_reference = (bar_reference or "").strip() or None
    db.commit()
    return user


def reset(db: Session, user_id: str, temporary_password: str) -> None:
    """An administrator sets a temporary password; the person must change it, and set up two-factor again."""
    user = db.get(HarveyUser, user_id)
    if user is None:
        raise RuleError("unknown_user", "No such account.")
    check_password_rules(temporary_password)
    user.password_hash, user.must_change_password = hash_password(temporary_password), True
    user.totp_secret, user.totp_enabled, user.failed_logins, user.locked_until = None, False, 0, None
    db.commit()


def _aware(moment: datetime) -> datetime:
    return moment if moment.tzinfo else moment.replace(tzinfo=timezone.utc)
