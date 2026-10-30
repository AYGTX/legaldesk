"""Plain-language explanations of articles, shared by everyone once a lawyer approves them.

The model writes a draft; a lawyer edits and approves it. Only approved explanations leave Harvey (to its API and to
LegalDesk), and an approval holds for the wording it was given on: when the article's text changes, the explanation
drops back to "out of date" until someone reviews it again — the same rule as verifying a law."""

import hashlib
from datetime import datetime, timezone

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.models import Article, ArticleExplanation
from app.services import assist
from app.services.corpus import RuleError
from app.services.llm import LlmProvider

LANGUAGES = ("fr", "en")
DRAFT, APPROVED, OUT_OF_DATE = "draft", "approved", "out_of_date"


def wording_hash(text: str) -> str:
    return hashlib.sha256(" ".join(text.split()).encode("utf-8")).hexdigest()


def status(explanation: ArticleExplanation, article: Article) -> str:
    if explanation.text_hash != wording_hash(article.text):
        return OUT_OF_DATE
    return APPROVED if explanation.approved_by else DRAFT


def get(db: Session, article_id: str, language: str) -> ArticleExplanation | None:
    return db.scalar(select(ArticleExplanation).where(ArticleExplanation.article_id == article_id,
                                                      ArticleExplanation.language == language))


def approved(db: Session, article: Article, language: str) -> ArticleExplanation | None:
    explanation = get(db, article.id, language)
    return explanation if explanation and status(explanation, article) == APPROVED else None


def _check_language(language: str) -> None:
    if language not in LANGUAGES:
        raise RuleError("invalid", "Unknown language.")


def generate(db: Session, llm: LlmProvider, article: Article, language: str, by: str | None) -> ArticleExplanation:
    """Asks the model for a draft (law text only) and stores it, replacing any earlier one for that language."""
    _check_language(language)
    text = article.legal_text
    result = assist.explain_article(llm, text.reference or text.title, article.number, article.text, language)
    explanation = get(db, article.id, language) or ArticleExplanation(article_id=article.id, language=language)
    explanation.summary = result.summary
    explanation.in_practice = result.in_practice or None
    explanation.text_hash = wording_hash(article.text)
    explanation.model = llm.name
    explanation.written_by = by
    _clear_approval(explanation)
    db.add(explanation)
    db.commit()
    return explanation


def save(db: Session, article: Article, language: str, summary: str, in_practice: str, by: str | None,
         approver=None) -> ArticleExplanation:
    """A person's own wording (written or corrected), optionally approved in the same step by a lawyer's account."""
    _check_language(language)
    summary = " ".join(summary.split())
    if len(summary) < 20:
        raise RuleError("explanation_too_short", "An explanation needs at least 20 characters.")
    explanation = get(db, article.id, language) or ArticleExplanation(article_id=article.id, language=language, model=None)
    changed = (explanation.summary != summary or (explanation.in_practice or "") != " ".join(in_practice.split())
               or explanation.text_hash != wording_hash(article.text))
    explanation.summary = summary
    explanation.in_practice = " ".join(in_practice.split()) or None
    explanation.text_hash = wording_hash(article.text)
    if changed:
        explanation.written_by = by
        _clear_approval(explanation)
    if approver is not None:
        _approve(explanation, approver)
    db.add(explanation)
    db.commit()
    return explanation


def approve(db: Session, article: Article, language: str, approver) -> ArticleExplanation:
    explanation = get(db, article.id, language)
    if explanation is None:
        raise RuleError("no_explanation", "There is no explanation to approve.")
    if status(explanation, article) == OUT_OF_DATE:
        raise RuleError("explanation_out_of_date", "The article changed since this explanation was written; review it first.")
    _approve(explanation, approver)
    db.commit()
    return explanation


def withdraw(db: Session, article: Article, language: str) -> None:
    explanation = get(db, article.id, language)
    if explanation is not None:
        _clear_approval(explanation)
        db.commit()


def _approve(explanation: ArticleExplanation, approver) -> None:
    """Records the lawyer's account, not a typed name (R-M11-2)."""
    explanation.approved_by = approver.signature
    explanation.approved_by_user_id = approver.id
    explanation.approved_at = datetime.now(timezone.utc)


def _clear_approval(explanation: ArticleExplanation) -> None:
    explanation.approved_by = None
    explanation.approved_by_user_id = None
    explanation.approved_at = None
