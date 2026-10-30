"""False-positive reports from LegalDesk (rework spec R-M8-4). A user who marks a finding "not applicable" gives a
reason; it lands here, on the risk's page, until a lawyer has looked at it (and, usually, added a counter-example)."""
from datetime import datetime, timezone

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.models import ReviewFeedback


def record(db: Session, source_ref: str, rule_key: str, reason: str, rule_version: int | None = None,
           clause_reference: str | None = None, quote: str | None = None) -> ReviewFeedback:
    """Stores one report. A report already received (same LegalDesk id) is returned unchanged, so retries are safe."""
    existing = db.scalar(select(ReviewFeedback).where(ReviewFeedback.source_ref == source_ref))
    if existing is not None:
        return existing
    item = ReviewFeedback(source_ref=source_ref, rule_key=rule_key, rule_version=rule_version,
                          clause_reference=clause_reference, quote=quote, reason=reason)
    db.add(item)
    db.commit()
    return item


def open_for(db: Session, rule_key: str) -> list[ReviewFeedback]:
    return list(db.scalars(select(ReviewFeedback)
                           .where(ReviewFeedback.rule_key == rule_key, ReviewFeedback.handled_at.is_(None))
                           .order_by(ReviewFeedback.received_at.desc())))


def handle(db: Session, feedback_id: int, user_id: str) -> None:
    item = db.get(ReviewFeedback, feedback_id)
    if item is None or item.handled_at is not None:
        return
    item.handled_at = datetime.now(timezone.utc)
    item.handled_by_user_id = user_id
    db.commit()
