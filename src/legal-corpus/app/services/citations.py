"""Checks that a quote attributed to the law appears, word for word, in a stored article."""

import re

from sqlalchemy import select
from sqlalchemy.orm import Session, selectinload

from app.models import Article, LegalText
from app.schemas import CitationIn, CitationOut
from app.services.splitter import normalize_number
from app.services.textnorm import normalize_for_match

# Shorter quotes ("le contrat", "la loi") match almost anything and prove nothing.
MIN_QUOTE_CHARS = 15

_ELISION = re.compile(r"\s*(?:\[\s*(?:\.\.\.|…)\s*\]|\(\s*(?:\.\.\.|…)\s*\)|\.\.\.|…)\s*")


def citation_label(text: LegalText, article_number: str) -> str:
    return f"{text.reference or text.title}, art. {article_number}"


def _segments(quote: str) -> list[str]:
    cleaned = normalize_for_match(quote).strip('"\' ')
    return [s for s in (normalize_for_match(p) for p in _ELISION.split(cleaned)) if s]


def _contains_in_order(haystack: str, segments: list[str]) -> bool:
    position = 0
    for segment in segments:
        found = haystack.find(segment, position)
        if found < 0:
            return False
        position = found + len(segment)
    return True


def check_citation(db: Session, citation: CitationIn) -> CitationOut:
    segments = _segments(citation.quote)
    if sum(len(s) for s in segments) < MIN_QUOTE_CHARS:
        return CitationOut(found=False, is_verified=False, reason="quote_too_short")

    query = select(Article).options(selectinload(Article.legal_text))
    if citation.article_id:
        query = query.where(Article.id == citation.article_id)
    elif citation.legal_text_id:
        query = query.where(Article.legal_text_id == citation.legal_text_id)
        if citation.article_number:
            query = query.where(Article.number == normalize_number(citation.article_number))
    else:
        return CitationOut(found=False, is_verified=False, reason="no_target")

    articles = db.scalars(query.order_by(Article.order)).all()
    if not articles:
        return CitationOut(found=False, is_verified=False, reason="article_not_found")

    for article in articles:
        if _contains_in_order(normalize_for_match(article.text), segments):
            text = article.legal_text
            return CitationOut(
                found=True,
                is_verified=text.is_verified,
                article_id=article.id,
                article_number=article.number,
                legal_text_id=text.id,
                legal_text_title=text.title,
                citation=citation_label(text, article.number),
                reason=None if text.is_verified else "text_not_verified",
            )

    return CitationOut(found=False, is_verified=False, reason="quote_not_in_article")
