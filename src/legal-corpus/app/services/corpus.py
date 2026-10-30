"""Corpus rules. The invariant: a sign-off covers exactly the articles, reference and source that were checked, so
changing any of them clears it."""

from datetime import date, datetime, timezone

from sqlalchemy import func, select
from sqlalchemy.orm import Session, selectinload

from app.models import Article, ArticleVersion, Category, Jurisdiction, LegalText, TextStatus
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


def verify(db: Session, text_id: str, approver, source_url: str | None = None) -> LegalText:
    """A lawyer's account signs the text off (R-M11-2). A source given here (when the text has none yet) is saved in
    the same step."""
    text = get_text(db, text_id)
    source_url = (source_url or "").strip()
    if source_url and not text.source_url:
        if not source_url.startswith(("http://", "https://")):
            raise RuleError("invalid_source", "The official source must be a web address (http or https).")
        text.source_url = source_url
    if not text.articles:
        raise RuleError("no_articles", "A text without articles has nothing to verify.")
    if not text.source_url:
        raise RuleError("no_source", "Add the official source before verifying: the sign-off is checked against it.")
    text.verified_by = approver.signature
    text.verified_by_user_id = approver.id
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


def _rewrite(article: Article, number: str, new_text: str, by: str | None, note: str | None,
             applies_from: date | None = None) -> bool:
    """Keeps the current wording as a version, then applies the new one. False when nothing changed."""
    if article.text == new_text and article.number == number:
        if applies_from is not None:
            article.applies_from = applies_from
        return False
    if applies_from is not None and article.applies_from is not None and applies_from < article.applies_from:
        raise RuleError("applies_before_current",
                        f"The new wording cannot take effect before the current one ({article.applies_from}).")
    article.versions.append(ArticleVersion(number=article.number, text=article.text, replaced_by=by,
                                           note=(note or "").strip() or None, applies_from=article.applies_from))
    article.applies_from = applies_from
    if article.text != new_text:
        article.embedding = None
        article.embedding_model = None
    article.number = number
    article.text = new_text
    return True


def update_article(db: Session, article_id: str, data: ArticleIn, changed_by: str | None = None) -> Article:
    article = get_article(db, article_id)
    text = get_text(db, article.legal_text_id)
    number = normalize_number(data.number)
    _ensure_unique_number(text, number, except_id=article.id)
    if _rewrite(article, number, data.text.strip(), changed_by, data.note, data.applies_from):
        text.clear_verification()
    if data.order is not None:
        article.order = data.order
    db.commit()
    return article


def restore_version(db: Session, article_id: str, version_id: str, changed_by: str | None = None) -> Article:
    article = get_article(db, article_id)
    version = next((v for v in article.versions if v.id == version_id), None)
    if version is None:
        raise NotFoundError(f"Version {version_id} not found.")
    text = get_text(db, article.legal_text_id)
    _ensure_unique_number(text, version.number, except_id=article.id)
    when = version.replaced_at.strftime("%Y-%m-%d %H:%M")
    if _rewrite(article, version.number, version.text, changed_by, f"Restored the wording replaced on {when}",
                version.applies_from):
        text.clear_verification()
    db.commit()
    return article


REPEALED_TEXT = "(Abrogé)"


def apply_amendment(db: Session, text_id: str, changes: list[dict], applies_from: date | None, note: str | None,
                    changed_by: str | None) -> int:
    """Applies reviewed changes from an amending law as new wordings: replaced and repealed articles keep their
    previous wording in their history, added articles start at applies_from. Returns how many were applied."""
    text = get_text(db, text_id)
    by_number = {a.number: a for a in text.articles}
    applied = 0
    for change in changes:
        number = normalize_number(change["number"])
        article = by_number.get(number)
        if change["action"] == "add":
            if article is not None:
                raise RuleError("duplicate_number", f"Article {number} already exists in this text.")
            order = max((a.order for a in text.articles), default=0) + 1
            article = Article(legal_text_id=text.id, number=number, text=change["new_text"], order=order,
                              applies_from=applies_from)
            text.articles.append(article)
            by_number[number] = article
            applied += 1
            continue
        if article is None:
            raise RuleError("article_not_found", f"{text.reference or text.title} has no article {number}.")
        new_text = REPEALED_TEXT if change["action"] == "repeal" else change["new_text"]
        if _rewrite(article, number, new_text, changed_by, note, applies_from):
            applied += 1
    if applied:
        text.clear_verification()
    db.commit()
    return applied


def set_excluded(db: Session, article_id: str, excluded: bool) -> Article:
    """Not a change to the law's text, so the verification stands."""
    article = get_article(db, article_id)
    article.excluded_from_search = excluded
    db.commit()
    return article


def delete_article(db: Session, article_id: str) -> None:
    article = get_article(db, article_id)
    text = get_text(db, article.legal_text_id)
    db.delete(article)
    text.clear_verification()
    db.commit()


def import_articles(db: Session, text_id: str, raw_text: str, replace: bool, changed_by: str | None = None,
                    note: str | None = None, applies_from: date | None = None) -> tuple[list[Article], str, list[str]]:
    text = get_text(db, text_id)
    result = split_articles(raw_text)
    if not result.articles:
        raise RuleError("no_articles_found", "No article headings found (expected 'Article 1', 'Art. 2', 'الفصل 3').")

    numbers = [a.number for a in result.articles]
    duplicates = sorted({n for n in numbers if numbers.count(n) > 1})
    if duplicates:
        raise RuleError("duplicate_number", f"The pasted text repeats article(s): {', '.join(duplicates)}.")

    if replace:
        # Re-importing an updated law: an article that keeps its number is rewritten in place, so its id, its
        # exclusion flag and its history survive, and its previous wording becomes a version.
        by_number = {a.number: a for a in text.articles}
        imported = []
        for i, a in enumerate(result.articles):
            article = by_number.pop(a.number, None)
            if article is None:
                article = Article(legal_text_id=text.id, number=a.number, text=a.text, applies_from=applies_from)
                db.add(article)
            elif article.text != a.text:
                _rewrite(article, a.number, a.text, changed_by, note or "Re-imported text", applies_from)
            article.order = i + 1
            imported.append(article)
        for gone in by_number.values():
            db.delete(gone)
        text.clear_verification()
        db.commit()
        return imported, result.preamble, result.removed_headings

    existing = {a.number for a in text.articles}
    clashes = sorted(existing.intersection(numbers))
    if clashes:
        raise RuleError("duplicate_number", f"Article(s) already exist: {', '.join(clashes)}. Use replace.")
    start = max((a.order for a in text.articles), default=0)

    created = [
        Article(legal_text_id=text.id, number=a.number, text=a.text, order=start + i + 1, applies_from=applies_from)
        for i, a in enumerate(result.articles)
    ]
    db.add_all(created)
    text.clear_verification()
    db.commit()
    return created, result.preamble, result.removed_headings
