"""Harvey as an MCP server: any AI assistant or agent (Claude, an IDE agent, a firm's own agent) can ground its answers
in verified Tunisian law through these tools.

Read-only, and only verified, published law is returned — the same rule LegalDesk follows. The point is the opposite
of a chatbot that recites law from memory: the agent looks the article up here, quotes what the tools return, and
checks its quotes with check_quote before relying on them.

Run it over stdio (how desktop assistants launch MCP servers):

    python -m app.mcp_server            # from src/legal-corpus
"""

from datetime import date

from mcp.server.fastmcp import FastMCP
from sqlalchemy import func, select
from sqlalchemy.orm import Session, selectinload

from app.config import load_settings
from app.db import make_session_factory
from app.models import Article, LegalText, TextStatus, wording_on
from app.schemas import CitationIn, SearchIn
from app.services import explanations
from app.services.citations import check_citation, citation_label
from app.services.embeddings import HashingEmbedder
from app.services.search import search
from app.services.splitter import normalize_number

INSTRUCTIONS = """Verified Tunisian law (codes, laws, decrees), article by article, each text checked by a lawyer
against its official source. Use these tools instead of recalling law from memory: find articles with search_law or
get_article, quote only the text they return, and confirm any quote with check_quote before presenting it as law.
Cite articles with the 'citation' field. An explanation from explain_article is a reading aid approved by a lawyer,
never a quote. If the tools return nothing, say the corpus does not cover it rather than answering from memory."""

mcp = FastMCP("harvey-legal-reference", instructions=INSTRUCTIONS)
_factory = None


def _db() -> Session:
    global _factory
    if _factory is None:
        _factory = make_session_factory(load_settings().database_url)
    return _factory()


def _verified():
    return select(LegalText).where(LegalText.verified_at.is_not(None), LegalText.status == TextStatus.Published)


def _day(as_of: str | None) -> date | None:
    if not as_of:
        return None
    try:
        return date.fromisoformat(as_of)
    except ValueError as error:
        raise ValueError("as_of must be a date written YYYY-MM-DD.") from error


@mcp.tool()
def list_laws() -> list[dict]:
    """The verified legal texts available, with their official reference, number of articles and source."""
    with _db() as db:
        texts = db.scalars(_verified().options(selectinload(LegalText.articles)).order_by(LegalText.title)).all()
        return [{"title": t.title, "reference": t.reference, "category": t.category.value, "language": t.language,
                 "articles": len(t.articles), "source_url": t.source_url, "verified_by": t.verified_by}
                for t in texts]


@mcp.tool()
def search_law(query: str, top_k: int = 5, as_of: str | None = None) -> list[dict]:
    """Finds the articles of verified law most relevant to a question or a contract clause (keyword ranking).
    as_of (YYYY-MM-DD) searches the wording in force on that day, e.g. a contract's signing date."""
    with _db() as db:
        hits = search(db, HashingEmbedder(), SearchIn(query=query, top_k=max(1, min(top_k, 20)), as_of=_day(as_of)))
        return [{"citation": h.citation, "article_id": h.article_id, "article_number": h.article_number,
                 "text": h.article_text, "excerpt": h.excerpt, "law": h.legal_text_title, "source_url": h.source_url,
                 "score": h.score} for h in hits]


@mcp.tool()
def get_article(law: str, number: str, as_of: str | None = None) -> dict:
    """One article of a verified law, word for word. law: the reference or title, or a distinctive part of it
    (e.g. "2016-48", "Code des obligations et des contrats"). number: e.g. "81", "2 bis", "premier"."""
    day = _day(as_of)
    with _db() as db:
        texts = db.scalars(_verified().where(
            func.lower(func.coalesce(LegalText.reference, "") + " " + LegalText.title).contains(law.strip().lower()))
            .options(selectinload(LegalText.articles))).all()
        wanted = normalize_number(number)
        matches = [(t, a) for t in texts for a in t.articles if a.number == wanted]
        if not matches:
            return {"found": False, "reason": f"No article {number} in a verified text matching '{law}'.",
                    "candidates": [t.reference or t.title for t in texts][:10]}
        if len(matches) > 1:
            return {"found": False, "reason": "Several verified texts match; be more specific.",
                    "candidates": [t.reference or t.title for t, _ in matches]}
        text, article = matches[0]
        wording = wording_on(article, day) if day else (article.number, article.text)
        if wording is None:
            return {"found": False, "reason": f"Article {number} was not yet in force on {as_of}."}
        approved = explanations.approved(db, article, "fr")
        return {"found": True, "citation": citation_label(text, wording[0]), "article_id": article.id,
                "text": wording[1], "in_force_from": article.applies_from.isoformat() if article.applies_from else None,
                "source_url": text.source_url, "verified_by": text.verified_by,
                "plain_explanation_fr": approved.summary if approved else None}


@mcp.tool()
def check_quote(quote: str, article_id: str) -> dict:
    """Checks that a quote appears word for word (typography and "[…]" elisions tolerated) in the given article of
    verified law. Call it before presenting any quote as law; a quote that fails must not be cited."""
    with _db() as db:
        result = check_citation(db, CitationIn(quote=quote, article_id=article_id))
        return {"found": result.found, "verified_law": result.is_verified, "citation": result.citation,
                "reason": result.reason}


@mcp.tool()
def explain_article(article_id: str, language: str = "fr") -> dict:
    """The lawyer-approved plain-language explanation of an article, if one exists (fr or en). A reading aid only:
    the article's text stays the reference."""
    with _db() as db:
        article = db.get(Article, article_id)
        if article is None or not article.legal_text.is_verified:
            return {"found": False, "reason": "No such article of verified law."}
        approved = explanations.approved(db, article, language if language in ("fr", "en") else "fr")
        if approved is None:
            return {"found": False, "reason": "No lawyer-approved explanation for this article yet."}
        return {"found": True, "summary": approved.summary, "in_practice": approved.in_practice,
                "approved_by": approved.approved_by}


if __name__ == "__main__":
    mcp.run()
