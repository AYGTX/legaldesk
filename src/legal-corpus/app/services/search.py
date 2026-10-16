import json

import numpy as np
from sqlalchemy import func, select
from sqlalchemy.orm import Session, selectinload

from app.models import Article, LegalText, TextStatus
from app.schemas import IndexStatus, SearchHit, SearchIn
from app.services.citations import citation_label
from app.services.embeddings import Embedder, HashingEmbedder, chunk_text
from app.services.lexical import index_for, tokenize


def _needs_index(article: Article, embedder: Embedder) -> bool:
    return article.embedding is None or article.embedding_model != embedder.name


def index_articles(db: Session, embedder: Embedder, articles: list[Article]) -> int:
    pending = [a for a in articles if _needs_index(a, embedder)]
    if not pending:
        return 0
    chunks = [chunk_text(a.text) or [a.number] for a in pending]
    vectors = embedder.embed([c for group in chunks for c in group])
    offset = 0
    for article, group in zip(pending, chunks):
        article.embedding = json.dumps(vectors[offset: offset + len(group)].round(6).tolist())
        article.embedding_model = embedder.name
        offset += len(group)
    db.commit()
    return len(pending)


def rebuild_index(db: Session, embedder: Embedder, force: bool = False) -> int:
    articles = list(db.scalars(select(Article)).all())
    if force:
        for article in articles:
            article.embedding = None
    return index_articles(db, embedder, articles)


def index_status(db: Session, embedder: Embedder) -> IndexStatus:
    total = db.scalar(select(func.count()).select_from(Article)) or 0
    indexed = db.scalar(
        select(func.count()).select_from(Article).where(Article.embedding_model == embedder.name)
    ) or 0
    return IndexStatus(
        embedder=embedder.name, total_articles=total, indexed_articles=indexed, stale_articles=total - indexed
    )


def search(db: Session, embedder: Embedder, request: SearchIn) -> list[SearchHit]:
    query = (
        select(Article)
        .join(LegalText)
        .options(selectinload(Article.legal_text))
        .where(LegalText.status != TextStatus.Archived)
    )
    if not request.include_unverified:
        query = query.where(LegalText.verified_at.is_not(None), LegalText.status == TextStatus.Published)
    if request.jurisdiction:
        query = query.where(LegalText.jurisdiction == request.jurisdiction)
    if request.category:
        query = query.where(LegalText.category == request.category)
    if request.language:
        query = query.where(LegalText.language == request.language)

    articles = list(db.scalars(query).all())
    if not articles:
        return []

    scored: list[tuple[float, str, Article]] = []
    if isinstance(embedder, HashingEmbedder):
        # Without a semantic model, keyword ranking beats hashed vectors by a wide margin.
        by_id = {a.id: a for a in articles}
        query_terms = set(tokenize(request.query))
        for article_id, score in index_for(articles).rank(request.query)[: request.top_k]:
            article = by_id[article_id]
            chunks = chunk_text(article.text) or [article.text]
            excerpt = max(chunks, key=lambda c: len(query_terms.intersection(tokenize(c))))
            scored.append((score, excerpt, article))
    else:
        index_articles(db, embedder, articles)
        query_vector = embedder.embed([request.query])[0]
        for article in articles:
            vectors = np.asarray(json.loads(article.embedding), dtype=np.float32)
            similarities = vectors @ query_vector
            best = int(np.argmax(similarities))
            chunk = (chunk_text(article.text) or [article.text])[best]
            scored.append((float(similarities[best]), chunk, article))

    scored.sort(key=lambda item: item[0], reverse=True)
    hits = []
    for score, excerpt, article in scored[: request.top_k]:
        if score <= 0:
            break
        text = article.legal_text
        hits.append(
            SearchHit(
                article_id=article.id,
                article_number=article.number,
                article_text=article.text,
                excerpt=excerpt,
                score=round(score, 4),
                legal_text_id=text.id,
                legal_text_title=text.title,
                reference=text.reference,
                source_url=text.source_url,
                jurisdiction=text.jurisdiction,
                category=text.category,
                language=text.language,
                is_verified=text.is_verified,
                citation=citation_label(text, article.number),
            )
        )
    return hits
