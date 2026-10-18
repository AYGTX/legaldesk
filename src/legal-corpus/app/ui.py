from datetime import date
from types import SimpleNamespace
from urllib.parse import quote, urlencode, urlsplit

from fastapi import APIRouter, Depends, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import HTMLResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from pydantic import ValidationError
from sqlalchemy.orm import Session

from app import auth
from app.config import BASE_DIR
from app.deps import get_db, get_embedder
from app.i18n import LANGUAGES, is_known, pick_language, translator
from app.models import Category, Jurisdiction, TextStatus
from app.schemas import ArticleIn, CitationIn, LegalTextIn, SearchIn
from app.services import corpus, pdf, playbook
from app.services.citations import check_citation
from app.services.embeddings import Embedder
from app.services.search import index_status, rebuild_index, search
from app.services.splitter import split_articles

templates = Jinja2Templates(directory=str(BASE_DIR / "app" / "templates"))


def same_origin(request: Request) -> None:
    """The UI has no login, so refuse form posts that a page on another site could make the browser send."""
    if request.method != "POST":
        return
    origin = request.headers.get("origin") or request.headers.get("referer")
    if origin and urlsplit(origin).netloc != request.headers.get("host"):
        raise HTTPException(status_code=403, detail="Cross-origin form post refused.")


router = APIRouter(include_in_schema=False, dependencies=[Depends(same_origin), Depends(auth.require_login)])


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
            "user": getattr(request.state, "user", None),
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


def _safe_next(path: str) -> str:
    """Only local paths, so the sign-in page cannot be used to bounce someone to another site."""
    return path if path.startswith("/") and not path.startswith("//") and not path.startswith("/\\") else "/"


@router.get("/login", response_class=HTMLResponse)
def login_page(request: Request, next: str = "/"):
    if auth.current_user(request):
        return RedirectResponse(_safe_next(next), status_code=303)
    return _render(request, "login.html", next=_safe_next(next), error=False)


@router.post("/login", response_class=HTMLResponse)
def login(request: Request, login: str = Form(""), password: str = Form(""), next: str = Form("/")):
    if not auth.check_credentials(request.app.state.settings, login, password):
        return _render(request, "login.html", next=_safe_next(next), error=True, login=login)
    response = RedirectResponse(_safe_next(next), status_code=303)
    response.set_cookie(auth.COOKIE, auth.make_session(request.app.state.session_secret, request.app.state.settings.admin_user),
                        max_age=auth.LIFETIME_SECONDS, httponly=True, samesite="lax")
    return response


@router.post("/logout")
def logout():
    response = RedirectResponse("/login", status_code=303)
    response.delete_cookie(auth.COOKIE)
    return response


@router.get("/", response_class=HTMLResponse)
def dashboard(request: Request, db: Session = Depends(get_db)):
    texts = corpus.list_texts(db, None, None, None, None, None)
    counts = corpus.article_counts(db)
    book = playbook.describe(db, _playbook_path(request))
    rules = book["rules"]
    stats = {
        "texts": len(texts),
        "verified": sum(1 for t in texts if t.verified_by),
        "articles": sum(counts.values()),
        "rules": len(rules),
        "confirmed": sum(1 for r in rules if r.get("confirmed_by")),
    }
    to_verify = [t for t in texts if not t.verified_by and t.status != TextStatus.Archived]
    to_publish = [t for t in texts if t.verified_by and t.status == TextStatus.Draft]
    to_confirm = [r for r in rules if not r.get("confirmed_by")]
    return _render(request, "dashboard.html", stats=stats, to_verify=to_verify, to_publish=to_publish,
                   to_confirm=to_confirm, counts=counts)


@router.get("/texts", response_class=HTMLResponse)
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


@router.get("/import-pdf", response_class=HTMLResponse)
def pdf_upload_page(request: Request):
    return _render(request, "pdf_upload.html")


@router.post("/import-pdf", response_class=HTMLResponse)
def pdf_upload(request: Request, file: UploadFile = File(...)):
    law = pdf.read_pdf(file.file.read(pdf.MAX_BYTES + 1))
    if len(law.candidates) == 1:
        c = law.candidates[0]
        return _pdf_review(request, c.raw_text, c.title, c.reference, file.filename)
    return _render(request, "pdf_choose.html", law=law, filename=file.filename)


def _pdf_review(request: Request, raw_text: str, title: str, reference: str, filename: str | None,
                error: str | None = None, fields: LegalTextIn | None = None) -> HTMLResponse:
    split = split_articles(raw_text)
    draft = fields or SimpleNamespace(
        title=title or (filename or "").rsplit(".", 1)[0], reference=reference, source_url=None,
        jurisdiction=Jurisdiction.Tunisia, category=Category.Other, language=pdf.guess_language(raw_text),
        effective_date=None, keywords="", summary="",
    )
    numbers = [a.number for a in split.articles]
    duplicates = sorted({n for n in numbers if numbers.count(n) > 1})
    return _render(request, "pdf_review.html", text=draft, raw_text=raw_text, split=split, filename=filename,
                   error=error, duplicates=duplicates)


@router.post("/import-pdf/review", response_class=HTMLResponse)
def pdf_review(request: Request, raw_text: str = Form(""), title: str = Form(""), reference: str = Form(""),
               filename: str = Form("")):
    return _pdf_review(request, raw_text, title, reference, filename)


@router.post("/import-pdf/create", response_class=HTMLResponse)
def pdf_create(
    request: Request, raw_text: str = Form(""), filename: str = Form(""),
    title: str = _TEXT_FIELDS["title"], reference: str = _TEXT_FIELDS["reference"],
    summary: str = _TEXT_FIELDS["summary"], keywords: str = _TEXT_FIELDS["keywords"],
    language: str = _TEXT_FIELDS["language"], jurisdiction: str = _TEXT_FIELDS["jurisdiction"],
    category: str = _TEXT_FIELDS["category"], effective_date: str = _TEXT_FIELDS["effective_date"],
    source_url: str = _TEXT_FIELDS["source_url"], db: Session = Depends(get_db),
):
    # Errors re-show the review with everything typed so far, instead of losing the extracted text.
    try:
        data = _text_form(title, reference, summary, keywords, language, jurisdiction, category, effective_date, source_url)
    except (ValidationError, ValueError):
        return _pdf_review(request, raw_text, title, reference, filename, "err_invalid")
    split = split_articles(raw_text)
    numbers = [a.number for a in split.articles]
    if not numbers:
        return _pdf_review(request, raw_text, title, reference, filename, "err_no_articles_found", data)
    if len(set(numbers)) != len(numbers):
        return _pdf_review(request, raw_text, title, reference, filename, "err_pdf_duplicate", data)

    text = corpus.create_text(db, data)
    created, _, _ = corpus.import_articles(db, text.id, raw_text, replace=False)
    return _redirect(f"/texts/{text.id}", "pdf_imported", count=len(created))


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
    return _redirect("/texts", "deleted")


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


# ------------------------------------------------------------------ playbook editor

def _playbook_path(request: Request):
    return request.app.state.settings.playbook_path


def _rule_url(key: str) -> str:
    return f"/playbook/rules/{quote(key, safe='')}"


@router.get("/playbook", response_class=HTMLResponse)
def playbook_page(request: Request, db: Session = Depends(get_db)):
    described = playbook.describe(db, _playbook_path(request))
    used = {r["key"] for r in described["rules"]}
    return _render(request, "playbook.html", pb=described, detected=playbook.DETECTED_KEYS,
                   free_keys=[k for k in playbook.DETECTED_KEYS if k not in used])


@router.post("/playbook/rules")
def create_rule(request: Request, key: str = Form(""), label: str = Form("")):
    playbook.add_rule(key, label, _playbook_path(request))
    return _redirect(_rule_url(key.strip().lower().replace(" ", "_")), "saved")


@router.get("/playbook/rules/{key}", response_class=HTMLResponse)
def rule_page(key: str, request: Request, db: Session = Depends(get_db)):
    described = playbook.describe(db, _playbook_path(request))
    rule = next((r for r in described["rules"] if r["key"] == key), None)
    if rule is None:
        return _redirect("/playbook")
    texts = [t for t in corpus.list_texts(db) if t.reference and t.is_verified]
    mappings = [f for f, k in described["regulations"].items() if k == key]
    return _render(request, "playbook_rule.html", rule=rule, texts=texts, mappings=mappings,
                   detected=playbook.DETECTED_KEYS)


@router.post("/playbook/rules/{key}/label")
def rule_label(key: str, request: Request, label: str = Form("")):
    playbook.update_label(key, label, _playbook_path(request))
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/delete")
def rule_delete(key: str, request: Request):
    playbook.delete_rule(key, _playbook_path(request))
    return _redirect("/playbook", "deleted")


@router.post("/playbook/rules/{key}/articles")
def rule_add_article(key: str, request: Request, text_id: str = Form(""), number: str = Form(""),
                     why: str = Form(""), db: Session = Depends(get_db)):
    if not text_id or not number.strip():
        return _redirect(_rule_url(key), "err_invalid", "error")
    playbook.add_article(db, key, text_id, number, why, _playbook_path(request))
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/articles/{index}")
def rule_update_article(key: str, index: int, request: Request, why: str = Form("")):
    playbook.update_article(key, index, why, _playbook_path(request))
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/articles/{index}/delete")
def rule_remove_article(key: str, index: int, request: Request):
    playbook.remove_article(key, index, _playbook_path(request))
    return _redirect(_rule_url(key), "deleted")


@router.post("/playbook/rules/{key}/articles/{index}/move")
def rule_move_article(key: str, index: int, request: Request, offset: int = Form(0)):
    playbook.move_article(key, index, offset, _playbook_path(request))
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/confirm")
def rule_confirm(key: str, request: Request, confirmed_by: str = Form(""), db: Session = Depends(get_db)):
    if not confirmed_by.strip():
        return _redirect(_rule_url(key), "err_invalid", "error")
    playbook.confirm(db, key, confirmed_by, _playbook_path(request))
    return _redirect(_rule_url(key), "rule_confirmed")


@router.post("/playbook/rules/{key}/unconfirm")
def rule_unconfirm(key: str, request: Request):
    playbook.unconfirm(key, _playbook_path(request))
    return _redirect(_rule_url(key), "unverified_ok")


@router.post("/playbook/mappings")
def mapping_add(request: Request, fragment: str = Form(""), key: str = Form(""), back: str = Form("/playbook")):
    playbook.set_mapping(fragment, key, _playbook_path(request))
    return _redirect(back if back.startswith("/playbook") else "/playbook", "saved")


@router.post("/playbook/mappings/delete")
def mapping_delete(request: Request, fragment: str = Form(""), back: str = Form("/playbook")):
    playbook.delete_mapping(fragment, _playbook_path(request))
    return _redirect(back if back.startswith("/playbook") else "/playbook", "deleted")
