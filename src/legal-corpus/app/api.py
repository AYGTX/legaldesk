from fastapi import APIRouter, Depends, Query, Request, Response
from sqlalchemy.orm import Session

from app.deps import get_db, get_embedder, require_api_key
from app.models import Category, Jurisdiction, TextStatus
from app.schemas import (
    ArticleIn,
    ArticleOut,
    BulkArticlesIn,
    CitationBatchIn,
    CitationIn,
    CitationOut,
    IndexStatus,
    LegalTextDetail,
    LegalTextIn,
    LegalTextOut,
    SearchHit,
    SearchIn,
    StatusIn,
    VerifyIn,
)
from app.services import corpus, playbook
from app.services.citations import check_citation
from app.services.embeddings import Embedder
from app.services.search import index_status, rebuild_index, search

router = APIRouter(prefix="/api/v1", dependencies=[Depends(require_api_key)])


@router.get("/texts", response_model=list[LegalTextOut], tags=["texts"])
def list_texts(
    q: str | None = None,
    jurisdiction: Jurisdiction | None = None,
    category: Category | None = None,
    status: TextStatus | None = None,
    verified: bool | None = None,
    db: Session = Depends(get_db),
):
    return corpus.list_texts(db, q, jurisdiction, category, status, verified)


@router.post("/texts", response_model=LegalTextDetail, status_code=201, tags=["texts"])
def create_text(body: LegalTextIn, db: Session = Depends(get_db)):
    return corpus.create_text(db, body)


@router.get("/texts/{text_id}", response_model=LegalTextDetail, tags=["texts"])
def get_text(text_id: str, db: Session = Depends(get_db)):
    return corpus.get_text(db, text_id)


@router.put("/texts/{text_id}", response_model=LegalTextDetail, tags=["texts"])
def update_text(text_id: str, body: LegalTextIn, db: Session = Depends(get_db)):
    return corpus.update_text(db, text_id, body)


@router.delete("/texts/{text_id}", status_code=204, tags=["texts"])
def delete_text(text_id: str, db: Session = Depends(get_db)):
    corpus.delete_text(db, text_id)
    return Response(status_code=204)


@router.put("/texts/{text_id}/status", response_model=LegalTextOut, tags=["texts"])
def set_status(text_id: str, body: StatusIn, db: Session = Depends(get_db)):
    return corpus.set_status(db, text_id, body.status)


@router.post("/texts/{text_id}/verify", response_model=LegalTextOut, tags=["verification"])
def verify(text_id: str, body: VerifyIn, db: Session = Depends(get_db)):
    return corpus.verify(db, text_id, body.verified_by)


@router.delete("/texts/{text_id}/verify", response_model=LegalTextOut, tags=["verification"])
def unverify(text_id: str, db: Session = Depends(get_db)):
    return corpus.unverify(db, text_id)


@router.get("/texts/{text_id}/articles", response_model=list[ArticleOut], tags=["articles"])
def list_articles(text_id: str, db: Session = Depends(get_db)):
    return corpus.get_text(db, text_id).articles


@router.post("/texts/{text_id}/articles", response_model=ArticleOut, status_code=201, tags=["articles"])
def add_article(text_id: str, body: ArticleIn, db: Session = Depends(get_db)):
    return corpus.add_article(db, text_id, body)


@router.post("/texts/{text_id}/articles/import", tags=["articles"])
def import_articles(text_id: str, body: BulkArticlesIn, db: Session = Depends(get_db)):
    created, preamble, removed = corpus.import_articles(db, text_id, body.raw_text, body.replace)
    return {"imported": len(created), "preamble": preamble, "removed_headings": removed, "articles":[ArticleOut.model_validate(a) for a in created]}


@router.get("/articles/{article_id}", response_model=ArticleOut, tags=["articles"])
def get_article(article_id: str, db: Session = Depends(get_db)):
    return corpus.get_article(db, article_id)


@router.put("/articles/{article_id}", response_model=ArticleOut, tags=["articles"])
def update_article(article_id: str, body: ArticleIn, db: Session = Depends(get_db)):
    return corpus.update_article(db, article_id, body)


@router.delete("/articles/{article_id}", status_code=204, tags=["articles"])
def delete_article(article_id: str, db: Session = Depends(get_db)):
    corpus.delete_article(db, article_id)
    return Response(status_code=204)


@router.post("/search", response_model=list[SearchHit], tags=["retrieval"])
def search_articles(body: SearchIn, db: Session = Depends(get_db), embedder: Embedder = Depends(get_embedder)):
    return search(db, embedder, body)


@router.post("/citations/check", response_model=CitationOut, tags=["retrieval"])
def check_one(body: CitationIn, db: Session = Depends(get_db)):
    return check_citation(db, body)


@router.post("/citations/check-batch", response_model=list[CitationOut], tags=["retrieval"])
def check_batch(body: CitationBatchIn, db: Session = Depends(get_db)):
    return [check_citation(db, c) for c in body.citations]


@router.get("/playbook", tags=["retrieval"])
def get_playbook(request: Request, include_unconfirmed: bool = False, db: Session = Depends(get_db)):
    return playbook.resolve(db, request.app.state.settings.playbook_path, include_unconfirmed)


@router.get("/index/status", response_model=IndexStatus, tags=["index"])
def get_index_status(db: Session = Depends(get_db), embedder: Embedder = Depends(get_embedder)):
    return index_status(db, embedder)


@router.post("/index/rebuild", response_model=IndexStatus, tags=["index"])
def post_rebuild(
    force: bool = Query(default=False), db: Session = Depends(get_db), embedder: Embedder = Depends(get_embedder)
):
    rebuild_index(db, embedder, force)
    return index_status(db, embedder)
