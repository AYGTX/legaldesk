from fastapi import APIRouter, Depends, Query, Request, Response
from sqlalchemy.orm import Session

from app.deps import get_db, get_embedder, get_matcher, require_api_key
from datetime import date

from app.models import Category, Jurisdiction, TextStatus, wording_on
from app.schemas import (
    ClassifyIn,
    ClassifyOut,
    ClauseMatchOut,
    ArticleSearchIn,
    ArticleVersionOut,
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
def get_text(text_id: str, as_of: date | None = None, db: Session = Depends(get_db)):
    """With ?as_of=YYYY-MM-DD, each article carries the wording that applied on that day, and articles that did
    not exist yet are left out."""
    text = corpus.get_text(db, text_id)
    if as_of is None:
        return text
    detail = LegalTextDetail.model_validate(text)
    articles = []
    for article, out in zip(text.articles, detail.articles):
        wording = wording_on(article, as_of)
        if wording is not None:
            articles.append(out.model_copy(update={"number": wording[0], "text": wording[1]}))
    return detail.model_copy(update={"articles": articles})


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
    return corpus.update_article(db, article_id, body, changed_by="API")


@router.post("/classify", response_model=ClassifyOut, tags=["retrieval"])
def classify(body: ClassifyIn, request: Request, db: Session = Depends(get_db), matcher=Depends(get_matcher)):
    """Matches contract clauses to confirmed playbook rules by meaning, against each rule's example clauses. Runs on
    this server; nothing is sent anywhere. Returns enabled=false when no semantic model is installed."""
    from app.services.matching import RuleExamples

    if matcher is None:
        return ClassifyOut(enabled=False, model=None, matches=[])
    served = playbook.resolve(db, request.app.state.settings.playbook_path)["rules"]
    rules = [RuleExamples(k, r.get("label") or k, r.get("examples", []), r.get("counter_examples", []))
             for k, r in served.items()]
    matches = matcher.classify([(c.reference, c.text) for c in body.clauses], rules, body.threshold)
    return ClassifyOut(enabled=True, model=matcher.embedder.name, matches=[ClauseMatchOut(**m.__dict__) for m in matches])


@router.get("/articles/{article_id}/versions", response_model=list[ArticleVersionOut], tags=["articles"])
def article_versions(article_id: str, db: Session = Depends(get_db)):
    """Previous wordings, newest first."""
    return corpus.get_article(db, article_id).versions


@router.put("/articles/{article_id}/search", response_model=ArticleOut, tags=["articles"])
def article_search(article_id: str, body: ArticleSearchIn, db: Session = Depends(get_db)):
    """Exclude an article from (or return it to) "applicable law" search; it stays in the text and stays quotable."""
    return corpus.set_excluded(db, article_id, body.excluded)


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
