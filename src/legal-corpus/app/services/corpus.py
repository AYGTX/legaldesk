"""Corpus rules. The invariant: a sign-off covers exactly the articles, reference and source that were checked, so
changing any of them clears it."""

from datetime import datetime, timezone

from sqlalchemy import func, select
from sqlalchemy.orm import Session, selectinload

from app.models import Article, Category, Jurisdiction, LegalText, TextStatus
from app.schemas import ArticleIn, LegalTextIn
from app.services.splitter import normalize_number, split_articles


class NotFoundError(Exception):
    pass


class RuleError(Exception):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


def list_texts(
    db: Session,
    q: str | None = None,
    jurisdiction: Jurisdiction | None = None,
    category: Category | None = None,
    status: TextStatus | None = None,
    verified: bool | None = None,
) -> list[LegalText]:
    query = select(LegalText)
    if q:
        like = f"%{q.strip()}%"
        query = query.where(
            LegalText.title.ilike(like) | LegalText.reference.ilike(like) | LegalText.keywords.ilike(like)
        )
    if jurisdiction:
        query = query.where(LegalText.jurisdiction == jurisdiction)
    if category:
        query = query.where(LegalText.category == category)
    if status:
        query = query.where(LegalText.status == status)
    if verified is not None:
        query = query.where(LegalText.verified_at.is_not(None) if verified else LegalText.verified_at.is_(None))
    return list(db.scalars(query.order_by(LegalText.updated_at.desc())).all())


def article_counts(db: Session) -> dict[str, int]:
    rows = db.execute(select(Article.legal_text_id, func.count()).group_by(Article.legal_text_id)).all()
    return {text_id: count for text_id, count in rows}


def get_text(db: Session, text_id: str) -> LegalText:
    text = db.scalar(select(LegalText).options(selectinload(LegalText.articles)).where(LegalText.id == text_id))
    if text is None:
        raise NotFoundError(f"Legal text {text_id} not found.")
    return text


def get_article(db: Session, article_id: str) -> Article:
    article = db.get(Article, article_id)
    if article is None:
        raise NotFoundError(f"Article {article_id} not found.")
    return article


def create_text(db: Session, data: LegalTextIn) -> LegalText:
    text = LegalText(**data.model_dump())
    db.add(text)
    db.commit()
    return get_text(db, text.id)


def update_text(db: Session, text_id: str, data: LegalTextIn) -> LegalText:
    text = get_text(db, text_id)
    if text.reference != data.reference or text.source_url != data.source_url:
        text.clear_verification()
    for field, value in data.model_dump().items():
        setattr(text, field, value)
    db.commit()
    return get_text(db, text_id)


def delete_text(db: Session, text_id: str) -> None:
    db.delete(get_text(db, text_id))
    db.commit()


def set_status(db: Session, text_id: str, status: TextStatus) -> LegalText:
    text = get_text(db, text_id)
    text.status = status
    db.commit()
    return text


def verify(db: Session, text_id: str, verified_by: str) -> LegalText:
    text = get_text(db, text_id)
    if not text.articles:
        raise RuleError("no_articles", "A text without articles has nothing to verify.")
    if not text.source_url:
        raise RuleError("no_source", "Add the official source before verifying: the sign-off is checked against it.")
    text.verified_by = verified_by.strip()
    text.verified_at = datetime.now(timezone.utc)
    db.commit()
    return text


def unverify(db: Session, text_id: str) -> LegalText:
    text = get_text(db, text_id)
    text.clear_verification()
    db.commit()
    return text


def _ensure_unique_number(text: LegalText, number: str, except_id: str | None = None) -> None:
    if any(a.number == number and a.id != except_id for a in text.articles):
        raise RuleError("duplicate_number", f"Article {number} already exists in this text.")


def add_article(db: Session, text_id: str, data: ArticleIn) -> Article:
    text = get_text(db, text_id)
    number = normalize_number(data.number)
    _ensure_unique_number(text, number)
    order = data.order if data.order is not None else max((a.order for a in text.articles), default=0) + 1
    article = Article(legal_text_id=text.id, number=number, text=data.text.strip(), order=order)
    db.add(article)
    text.clear_verification()
    db.commit()
    return article


def update_article(db: Session, article_id: str, data: ArticleIn) -> Article:
    article = get_article(db, article_id)
    text = get_text(db, article.legal_text_id)
    number = normalize_number(data.number)
    _ensure_unique_number(text, number, except_id=article.id)
    new_text = data.text.strip()
    if article.text != new_text or article.number != number:
        text.clear_verification()
    if article.text != new_text:
        article.embedding = None
        article.embedding_model = None
    article.number = number
    article.text = new_text
    if data.order is not None:
        article.order = data.order
    db.commit()
    return article


def delete_article(db: Session, article_id: str) -> None:
    article = get_article(db, article_id)
    text = get_text(db, article.legal_text_id)
    db.delete(article)
    text.clear_verification()
    db.commit()


def import_articles(db: Session, text_id: str, raw_text: str, replace: bool) -> tuple[list[Article], str, list[str]]:
    text = get_text(db, text_id)
    result = split_articles(raw_text)
    if not result.articles:
        raise RuleError("no_articles_found", "No article headings found (expected 'Article 1', 'Art. 2', 'الفصل 3').")

    numbers = [a.number for a in result.articles]
    duplicates = sorted({n for n in numbers if numbers.count(n) > 1})
    if duplicates:
        raise RuleError("duplicate_number", f"The pasted text repeats article(s): {', '.join(duplicates)}.")

    if replace:
        text.articles.clear()
        db.flush()
        start = 0
    else:
        existing = {a.number for a in text.articles}
        clashes = sorted(existing.intersection(numbers))
        if clashes:
            raise RuleError("duplicate_number", f"Article(s) already exist: {', '.join(clashes)}. Use replace.")
        start = max((a.order for a in text.articles), default=0)

    created = [
        Article(legal_text_id=text.id, number=a.number, text=a.text, order=start + i + 1)
        for i, a in enumerate(result.articles)
    ]
    db.add_all(created)
    text.clear_verification()
    db.commit()
    return created, result.preamble, result.removed_headings
