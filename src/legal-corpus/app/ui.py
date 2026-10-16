from datetime import date
from urllib.parse import urlencode, urlsplit

from fastapi import APIRouter, Depends, Form, HTTPException, Request
from fastapi.responses import HTMLResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from pydantic import ValidationError
from sqlalchemy.orm import Session

from app.config import BASE_DIR
from app.deps import get_db, get_embedder
from app.i18n import LANGUAGES, is_known, pick_language, translator
from app.models import Category, Jurisdiction, TextStatus
from app.schemas import ArticleIn, CitationIn, LegalTextIn, SearchIn
from app.services import corpus
from app.services.citations import check_citation
from app.services.embeddings import Embedder
from app.services.search import index_status, rebuild_index, search

templates = Jinja2Templates(directory=str(BASE_DIR / "app" / "templates"))


def same_origin(request: Request) -> None:
    """The UI has no login, so refuse form posts that a page on another site could make the browser send."""
    if request.method != "POST":
        return
    origin = request.headers.get("origin") or request.headers.get("referer")
    if origin and urlsplit(origin).netloc != request.headers.get("host"):
        raise HTTPException(status_code=403, detail="Cross-origin form post refused.")


router = APIRouter(include_in_schema=False, dependencies=[Depends(same_origin)])


def _render(request: Request, template: str, **context) -> HTMLResponse:
    lang = pick_language(request.cookies.get("lang"), request.headers.get("accept-language"))
    flash = request.query_params.get("flash")
    flash = flash if is_known(flash) else None
    kind = "error" if request.query_params.get("kind") == "error" else "ok"
    return templates.TemplateResponse(
        request,
        template,
        {
            "t": translator(lang),
            "lang": lang,
            "flash": flash,
            "flash_kind": kind,
            "flash_args": {"count": request.query_params.get("count", "")},
            "jurisdictions": list(Jurisdiction),
            "categories": list(Category),
            "statuses": list(TextStatus),
            **context,
        },
    )


def _redirect(path: str, flash: str | None = None, kind: str = "ok", **extra) -> RedirectResponse:
    params = {"flash": flash, "kind": kind, **extra} if flash else {}
    return RedirectResponse(f"{path}?{urlencode(params)}" if params else path, status_code=303)


def _text_form(
    title: str, reference: str, summary: str, keywords: str, language: str, jurisdiction: str, category: str,
    effective_date: str, source_url: str,
) -> LegalTextIn:
    return LegalTextIn(
        title=title.strip(),
        reference=reference.strip() or None,
        summary=summary.strip(),
        keywords=keywords.strip(),
        language=language,
        jurisdiction=jurisdiction,
        category=category,
        effective_date=date.fromisoformat(effective_date) if effective_date else None,
        source_url=source_url.strip() or None,
    )


_TEXT_FIELDS = dict(
    title=Form(""), reference=Form(""), summary=Form(""), keywords=Form(""), language=Form("fr"),
    jurisdiction=Form(""), category=Form(""), effective_date=Form(""), source_url=Form(""),
)


@router.get("/lang/{lang}")
def set_language(lang: str, request: Request):
    referer = request.headers.get("referer")
    target = urlsplit(referer).path if referer and urlsplit(referer).netloc == request.headers.get("host") else "/"
    response = RedirectResponse(target or "/", status_code=303)
    if lang in LANGUAGES:
        response.set_cookie("lang", lang, max_age=60 * 60 * 24 * 365, samesite="lax")
    return response


@router.get("/", response_class=HTMLResponse)
def texts_page(
    request: Request,
    q: str = "",
    jurisdiction: str = "",
    category: str = "",
    status: str = "",
    verified: str = "",
    db: Session = Depends(get_db),
):
    texts = corpus.list_texts(
        db,
        q or None,
        Jurisdiction(jurisdiction) if jurisdiction in Jurisdiction.__members__ else None,
        Category(category) if category in Category.__members__ else None,
        TextStatus(status) if status in TextStatus.__members__ else None,
        {"yes": True, "no": False}.get(verified),
    )
    return _render(
        request, "texts.html", texts=texts, counts=corpus.article_counts(db),
        filters={"q": q, "jurisdiction": jurisdiction, "category": category, "status": status, "verified": verified},
    )


@router.get("/texts/new", response_class=HTMLResponse)
def new_text_page(request: Request):
    return _render(request, "text_new.html", text=None)


@router.post("/texts/new")
def create_text(
    title: str = _TEXT_FIELDS["title"], reference: str = _TEXT_FIELDS["reference"],
    summary: str = _TEXT_FIELDS["summary"], keywords: str = _TEXT_FIELDS["keywords"],
    language: str = _TEXT_FIELDS["language"], jurisdiction: str = _TEXT_FIELDS["jurisdiction"],
    category: str = _TEXT_FIELDS["category"], effective_date: str = _TEXT_FIELDS["effective_date"],
    source_url: str = _TEXT_FIELDS["source_url"], db: Session = Depends(get_db),
):
    try:
        data = _text_form(title, reference, summary, keywords, language, jurisdiction, category, effective_date, source_url)
    except (ValidationError, ValueError):
        return _redirect("/texts/new", "err_invalid", "error")
    text = corpus.create_text(db, data)
    return _redirect(f"/texts/{text.id}", "saved")


@router.get("/texts/{text_id}", response_class=HTMLResponse)
def text_page(text_id: str, request: Request, db: Session = Depends(get_db)):
    return _render(request, "text_detail.html", text=corpus.get_text(db, text_id))


@router.post("/texts/{text_id}")
def update_text(
    text_id: str,
    title: str = _TEXT_FIELDS["title"], reference: str = _TEXT_FIELDS["reference"],
    summary: str = _TEXT_FIELDS["summary"], keywords: str = _TEXT_FIELDS["keywords"],
    language: str = _TEXT_FIELDS["language"], jurisdiction: str = _TEXT_FIELDS["jurisdiction"],
    category: str = _TEXT_FIELDS["category"], effective_date: str = _TEXT_FIELDS["effective_date"],
    source_url: str = _TEXT_FIELDS["source_url"], db: Session = Depends(get_db),
):
    try:
        data = _text_form(title, reference, summary, keywords, language, jurisdiction, category, effective_date, source_url)
    except (ValidationError, ValueError):
        return _redirect(f"/texts/{text_id}", "err_invalid", "error")
    corpus.update_text(db, text_id, data)
    return _redirect(f"/texts/{text_id}", "saved")


@router.post("/texts/{text_id}/status")
def set_status(text_id: str, status: str = Form(...), db: Session = Depends(get_db)):
    if status not in TextStatus.__members__:
        return _redirect(f"/texts/{text_id}", "err_invalid", "error")
    corpus.set_status(db, text_id, TextStatus(status))
    return _redirect(f"/texts/{text_id}", "saved")


@router.post("/texts/{text_id}/verify")
def verify(text_id: str, verified_by: str = Form(""), db: Session = Depends(get_db)):
    if not verified_by.strip():
        return _redirect(f"/texts/{text_id}", "err_invalid", "error")
    corpus.verify(db, text_id, verified_by)
    return _redirect(f"/texts/{text_id}", "verified_ok")


@router.post("/texts/{text_id}/unverify")
def unverify(text_id: str, db: Session = Depends(get_db)):
    corpus.unverify(db, text_id)
    return _redirect(f"/texts/{text_id}", "unverified_ok")


@router.post("/texts/{text_id}/delete")
def delete_text(text_id: str, db: Session = Depends(get_db)):
    corpus.delete_text(db, text_id)
    return _redirect("/", "deleted")


@router.post("/texts/{text_id}/articles")
def add_article(text_id: str, number: str = Form(""), text: str = Form(""), db: Session = Depends(get_db)):
    try:
        data = ArticleIn(number=number, text=text)
    except ValidationError:
        return _redirect(f"/texts/{text_id}", "err_invalid", "error")
    corpus.add_article(db, text_id, data)
    return _redirect(f"/texts/{text_id}", "saved")


@router.post("/texts/{text_id}/import")
def import_articles(text_id: str, raw_text: str = Form(""), replace: str = Form(""), db: Session = Depends(get_db)):
    created, _, _ = corpus.import_articles(db, text_id, raw_text, replace == "on")
    return _redirect(f"/texts/{text_id}", "imported", count=len(created))


@router.post("/articles/{article_id}")
def update_article(
    article_id: str, number: str = Form(""), text: str = Form(""), order: str = Form(""),
    db: Session = Depends(get_db),
):
    article = corpus.get_article(db, article_id)
    try:
        data = ArticleIn(number=number, text=text, order=int(order) if order.strip() else None)
    except (ValidationError, ValueError):
        return _redirect(f"/texts/{article.legal_text_id}", "err_invalid", "error")
    corpus.update_article(db, article_id, data)
    return _redirect(f"/texts/{article.legal_text_id}", "saved")


@router.post("/articles/{article_id}/delete")
def delete_article(article_id: str, db: Session = Depends(get_db)):
    text_id = corpus.get_article(db, article_id).legal_text_id
    corpus.delete_article(db, article_id)
    return _redirect(f"/texts/{text_id}", "deleted")


@router.get("/search", response_class=HTMLResponse)
def search_page(
    request: Request,
    q: str = "",
    jurisdiction: str = "",
    category: str = "",
    unverified: str = "",
    quote: str = "",
    text_id: str = "",
    article_number: str = "",
    db: Session = Depends(get_db),
    embedder: Embedder = Depends(get_embedder),
):
    hits = None
    if q.strip():
        hits = search(db, embedder, SearchIn(
            query=q,
            top_k=10,
            jurisdiction=Jurisdiction(jurisdiction) if jurisdiction in Jurisdiction.__members__ else None,
            category=Category(category) if category in Category.__members__ else None,
            include_unverified=unverified == "on",
        ))
    citation = None
    if quote.strip():
        citation = check_citation(db, CitationIn(
            quote=quote, legal_text_id=text_id or None, article_number=article_number or None,
        ))
    return _render(
        request, "search.html", hits=hits, citation=citation, all_texts=corpus.list_texts(db),
        status=index_status(db, embedder),
        form={"q": q, "jurisdiction": jurisdiction, "category": category, "unverified": unverified,
              "quote": quote, "text_id": text_id, "article_number": article_number},
    )


@router.post("/index/rebuild")
def rebuild(db: Session = Depends(get_db), embedder: Embedder = Depends(get_embedder)):
    rebuild_index(db, embedder, force=True)
    return _redirect("/search", "index_rebuilt")
