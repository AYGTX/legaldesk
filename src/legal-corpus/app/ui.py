import logging
import json
from datetime import date
from types import SimpleNamespace
from urllib.parse import quote, urlencode, urlsplit

from fastapi import APIRouter, Depends, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import HTMLResponse, JSONResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from pydantic import ValidationError
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app import auth
from app.config import BASE_DIR
from app.deps import get_db, get_embedder
from app.i18n import LANGUAGES, is_known, pick_language, translator
from app.models import Category, HarveyUser, Jurisdiction, Role, TextStatus, wording_on
from app.schemas import ArticleIn, CitationIn, LegalTextIn, SearchIn
from app.services import accounts, amendments, assist, corpus, definitions, explanations, pdf, playbook, references
from app.services.corpus import RuleError
from app.services.llm import LlmUnavailable
from app.services.citations import check_citation
from app.services.embeddings import Embedder
from app.services.search import index_status, rebuild_index, search
from app.services.splitter import occurrences as split_occurrences
from app.services.splitter import remove_occurrence, split_articles

templates = Jinja2Templates(directory=str(BASE_DIR / "app" / "templates"))


def same_origin(request: Request) -> None:
    """Refuse form posts that a page on another site could make a signed-in browser send."""
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
            "flash_args": {"count": request.query_params.get("count", ""), "number": request.query_params.get("number", "")},
            "jurisdictions": list(Jurisdiction),
            "categories": list(Category),
            "statuses": list(TextStatus),
            "user": getattr(request.state, "user", None),
            # The signed-in account: templates show approve buttons only to a lawyer, account admin only to an admin.
            "account": getattr(request.state, "account", None),
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


def _lawyer(request: Request) -> HarveyUser:
    """The signed-in lawyer, for an approval (R-M11-2); anyone else gets the page back with an explanation."""
    user = getattr(request.state, "account", None)
    if user is None or not user.can_approve:
        raise RuleError("not_lawyer", "Only a lawyer's account can approve.")
    return user


def _session_cookie(response, request: Request, user: HarveyUser, stage: str) -> None:
    lifetime = auth.LIFETIME_SECONDS if stage == auth.STAGE_FULL else auth.PENDING_SECONDS
    response.set_cookie(auth.COOKIE, auth.make_session(request.app.state.session_secret, user.id, stage),
                        max_age=lifetime, httponly=True, samesite="lax", secure=request.url.scheme == "https")


@router.get("/login", response_class=HTMLResponse)
def login_page(request: Request, next: str = "/"):
    if auth.current_user(request):
        return RedirectResponse(_safe_next(next), status_code=303)
    step = "code" if auth.pending_user(request) else "password"
    return _render(request, "login.html", next=_safe_next(next), error=False, step=step)


@router.post("/login", response_class=HTMLResponse)
def login(request: Request, login: str = Form(""), password: str = Form(""), next: str = Form("/"),
          db: Session = Depends(get_db)):
    user = accounts.authenticate(db, login, password)
    if user is None:
        return _render(request, "login.html", next=_safe_next(next), error=True, login=login, step="password")
    if user.totp_enabled:
        response = _render(request, "login.html", next=_safe_next(next), error=False, step="code")
        _session_cookie(response, request, user, auth.STAGE_PASSWORD)
        return response
    response = RedirectResponse(_safe_next(next), status_code=303)
    _session_cookie(response, request, user, auth.STAGE_FULL)
    return response


@router.post("/login/code", response_class=HTMLResponse)
def login_code(request: Request, code: str = Form(""), next: str = Form("/"), db: Session = Depends(get_db)):
    pending = auth.pending_user(request)
    if pending is None:
        return RedirectResponse("/login", status_code=303)
    user = db.get(HarveyUser, pending.id)
    if not accounts.verify_totp(user.totp_secret, code):
        return _render(request, "login.html", next=_safe_next(next), error=True, step="code")
    response = RedirectResponse(_safe_next(next), status_code=303)
    _session_cookie(response, request, user, auth.STAGE_FULL)
    return response


@router.post("/logout")
def logout():
    response = RedirectResponse("/login", status_code=303)
    response.delete_cookie(auth.COOKIE)
    return response


# ------------------------------------------------------------------ own account

@router.get("/account", response_class=HTMLResponse)
def account_page(request: Request, db: Session = Depends(get_db)):
    user = db.get(HarveyUser, request.state.account.id)
    return _render(request, "account.html", me=user, totp_uri=accounts.totp_uri(user) if user.totp_secret and not user.totp_enabled else None,
                    must_totp=request.app.state.settings.require_totp and user.can_approve and not user.totp_enabled)


@router.post("/account/password")
def account_password(request: Request, current: str = Form(""), new: str = Form(""), confirm: str = Form(""),
                     db: Session = Depends(get_db)):
    if new != confirm:
        return _redirect("/account", "err_password_mismatch", "error")
    accounts.change_password(db, db.get(HarveyUser, request.state.account.id), current, new)
    return _redirect("/account", "password_changed")


@router.post("/account/totp/start")
def account_totp_start(request: Request, db: Session = Depends(get_db)):
    accounts.start_totp(db, db.get(HarveyUser, request.state.account.id))
    return _redirect("/account")


@router.post("/account/totp/confirm")
def account_totp_confirm(request: Request, code: str = Form(""), db: Session = Depends(get_db)):
    accounts.confirm_totp(db, db.get(HarveyUser, request.state.account.id), code)
    return _redirect("/", "totp_enabled")


# ------------------------------------------------------------------ accounts (administrators)

@router.get("/users", response_class=HTMLResponse)
def users_page(request: Request, db: Session = Depends(get_db)):
    auth.require_admin(request)
    users = list(db.scalars(select(HarveyUser).order_by(HarveyUser.name)))
    return _render(request, "users.html", users=users, roles=list(Role))


@router.post("/users")
def users_create(request: Request, email: str = Form(""), name: str = Form(""), role: str = Form("Reader"),
                 bar_reference: str = Form(""), password: str = Form(""), is_admin: str = Form(""),
                 db: Session = Depends(get_db)):
    auth.require_admin(request)
    accounts.create(db, email, name, Role(role) if role in Role.__members__ else Role.Reader, password,
                    is_admin=bool(is_admin), bar_reference=bar_reference, must_change_password=True)
    return _redirect("/users", "user_created")


@router.post("/users/{user_id}")
def users_update(user_id: str, request: Request, role: str = Form("Reader"), bar_reference: str = Form(""),
                 is_admin: str = Form(""), active: str = Form(""), db: Session = Depends(get_db)):
    actor = auth.require_admin(request)
    accounts.update(db, actor, user_id, role=Role(role) if role in Role.__members__ else Role.Reader,
                    is_admin=bool(is_admin), active=bool(active), bar_reference=bar_reference)
    return _redirect("/users", "saved")


@router.post("/users/{user_id}/reset")
def users_reset(user_id: str, request: Request, password: str = Form(""), db: Session = Depends(get_db)):
    auth.require_admin(request)
    accounts.reset(db, user_id, password)
    return _redirect("/users", "user_reset")


@router.get("/", response_class=HTMLResponse)
def dashboard(request: Request, db: Session = Depends(get_db)):
    texts = corpus.list_texts(db, None, None, None, None, None)
    counts = corpus.article_counts(db)
    book = playbook.describe(db)
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
    missing = references.missing_laws(texts, references.Index.build(texts))
    return _render(request, "dashboard.html", stats=stats, to_verify=to_verify, to_publish=to_publish, missing=missing,
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
    with_articles = [c for c in law.candidates if c.article_count]
    if len(with_articles) == 1:
        # Only one real text: go straight to its review.
        c = with_articles[0]
        return _pdf_review(request, c.raw_text, c.title, c.reference, file.filename)
    return _render(request, "pdf_choose.html", law=law, filename=file.filename, roles=pdf.guess_roles(law.candidates))


@router.post("/import-pdf/roles", response_class=HTMLResponse)
async def pdf_roles(request: Request):
    """A codified edition holds several texts: the code, the act that promulgated it, laws already written into it.
    One is imported; the promulgation act becomes its reference and the others are named in its summary."""
    form = await request.form()
    count = int(form.get("count", 0))
    candidates = [pdf.Candidate(form.get(f"title_{i}", ""), form.get(f"reference_{i}", ""), form.get(f"raw_text_{i}", ""),
                                int(form.get(f"articles_{i}", 0))) for i in range(count)]
    roles = [form.get(f"role_{i}", "ignore") for i in range(count)]
    filename = form.get("filename", "")
    if roles.count("main") != 1 or any(r not in pdf.ROLES for r in roles):
        law = pdf.PdfLaw(pages=0, candidates=candidates)
        return _render(request, "pdf_choose.html", law=law, filename=filename, roles=roles, error="pdf_one_main")
    main, reference, summary = pdf.combine(candidates, roles)
    draft = SimpleNamespace(title=main.title, reference=reference, source_url=None, jurisdiction=Jurisdiction.Tunisia,
                            category=Category.Other, language=pdf.guess_language(main.raw_text), effective_date=None,
                            keywords="", summary=summary)
    return _pdf_review(request, main.raw_text, main.title, reference, filename, fields=draft)


def _pdf_review(request: Request, raw_text: str, title: str, reference: str, filename: str | None,
                error: str | None = None, fields=None, notice: str | None = None) -> HTMLResponse:
    split = split_articles(raw_text)
    draft = fields or SimpleNamespace(
        title=title or (filename or "").rsplit(".", 1)[0], reference=reference, source_url=None,
        jurisdiction=Jurisdiction.Tunisia, category=Category.Other, language=pdf.guess_language(raw_text),
        effective_date=None, keywords="", summary="",
    )
    numbers = [a.number for a in split.articles]
    duplicates = sorted({n for n in numbers if numbers.count(n) > 1})
    # For each repeated number, every occurrence with its exact span, so a person can remove the wrong one in place
    # instead of hand-editing the full pasted text.
    dup_occurrences = {n: [o for o in split_occurrences(raw_text) if o.number == n] for n in duplicates}
    return _render(request, "pdf_review.html", text=draft, raw_text=raw_text, split=split, filename=filename,
                   error=error, duplicates=duplicates, dup_occurrences=dup_occurrences, notice=notice,
                   ai=request.app.state.llm.name)


@router.post("/import-pdf/suggest", response_class=HTMLResponse)
def pdf_suggest(
    request: Request, raw_text: str = Form(""), filename: str = Form(""),
    title: str = _TEXT_FIELDS["title"], reference: str = _TEXT_FIELDS["reference"],
    summary: str = _TEXT_FIELDS["summary"], keywords: str = _TEXT_FIELDS["keywords"],
    language: str = _TEXT_FIELDS["language"], jurisdiction: str = _TEXT_FIELDS["jurisdiction"],
    category: str = _TEXT_FIELDS["category"], effective_date: str = _TEXT_FIELDS["effective_date"],
    source_url: str = _TEXT_FIELDS["source_url"],
):
    """Fills the empty fields with the model's suggestions; what the person already typed is kept."""
    try:
        meta = assist.suggest_metadata(request.app.state.llm, raw_text)
    except LlmUnavailable as error:
        logging.getLogger("harvey").warning("Metadata suggestion failed: %s", error)
        return _pdf_review(request, raw_text, title, reference, filename, "amend_unavailable")
    draft = SimpleNamespace(
        title=title.strip() or meta.title, reference=reference.strip() or meta.reference,
        source_url=source_url.strip() or None,
        jurisdiction=Jurisdiction(jurisdiction) if jurisdiction in Jurisdiction.__members__ else Jurisdiction.Tunisia,
        category=meta.category if (category in ("", "Other") and meta.category) else
        (Category(category) if category in Category.__members__ else Category.Other),
        language=language or pdf.guess_language(raw_text),
        effective_date=effective_date or None, keywords=keywords.strip() or meta.keywords,
        summary=summary.strip() or meta.summary,
    )
    return _pdf_review(request, raw_text, draft.title, draft.reference, filename, fields=draft, notice="ai_suggested")


@router.post("/import-pdf/review", response_class=HTMLResponse)
def pdf_review(request: Request, raw_text: str = Form(""), title: str = Form(""), reference: str = Form(""),
               filename: str = Form("")):
    return _pdf_review(request, raw_text, title, reference, filename)


@router.post("/import-pdf/review/remove-duplicate", response_class=HTMLResponse)
def pdf_remove_duplicate(
    request: Request, raw_text: str = Form(""), title: str = Form(""), reference: str = Form(""),
    filename: str = Form(""), span: str = Form(...),
):
    """Drops one occurrence of a duplicate article number, by the exact span its preview carried ("start:end"), so a
    stray cross-reference or a quoted footnote mistaken for a heading can be removed without hand-editing the full
    text. The span is only ever one this page itself printed, from the text currently in the form — never trusted
    beyond the bounds of that same text."""
    try:
        start, end = (int(part) for part in span.split(":", 1))
    except ValueError:
        return _pdf_review(request, raw_text, title, reference, filename, "err_invalid")
    if not (0 <= start < end <= len(raw_text)):
        return _pdf_review(request, raw_text, title, reference, filename, "err_invalid")
    cleaned = remove_occurrence(raw_text, start, end)
    return _pdf_review(request, cleaned, title, reference, filename, notice="pdf_duplicate_removed")


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


ARTICLES_PER_PAGE = 50


def _article_page(db: Session, article) -> int:
    """The page of its text an article is shown on."""
    from app.models import Article

    before = db.scalar(select(func.count()).select_from(Article)
                       .where(Article.legal_text_id == article.legal_text_id, Article.order < article.order))
    return before // ARTICLES_PER_PAGE + 1


def article_url(db: Session, article, query: str = "") -> str:
    extra = f"&{query}" if query else ""
    return f"/texts/{article.legal_text_id}?page={_article_page(db, article)}{extra}#art-{article.id}"


@router.get("/articles/{article_id}/go")
def go_to_article(article_id: str, request: Request, db: Session = Depends(get_db)):
    """Opens a text at the page that shows this article (cross-references, definitions, back after an edit). Other
    query parameters, such as a flash message, are passed on."""
    return RedirectResponse(article_url(db, corpus.get_article(db, article_id), request.url.query), status_code=303)


@router.get("/texts/{text_id}/goto")
def go_to_number(text_id: str, number: str = "", db: Session = Depends(get_db)):
    """« Aller à l'article 278 »: opens the page that shows it."""
    from app.models import Article
    from app.services.splitter import normalize_number

    article = db.scalar(select(Article).where(Article.legal_text_id == text_id,
                                              Article.number == normalize_number(number.strip())))
    if article is None:
        return _redirect(f"/texts/{text_id}", "goto_missing", "error", number=number.strip()[:20])
    return RedirectResponse(article_url(db, article), status_code=303)


@router.get("/texts/{text_id}", response_class=HTMLResponse)
def text_page(text_id: str, request: Request, as_of: str = "", page: int = 1, db: Session = Depends(get_db)):
    return _text_page(text_id, request, db, as_of, page=page)


def _text_page(text_id: str, request: Request, db: Session, as_of: str = "", page: int = 1, **extra) -> HTMLResponse:
    try:
        day = date.fromisoformat(as_of) if as_of else None
    except ValueError:
        day = None
    text = corpus.get_text(db, text_id)
    lang = pick_language(request.cookies.get("lang"), request.headers.get("accept-language"))
    ref_index = references.Index.build(corpus.list_texts(db))
    from app.models import Article

    total = db.scalar(select(func.count()).select_from(Article).where(Article.legal_text_id == text.id))
    pages = max(1, -(-total // ARTICLES_PER_PAGE))
    page = min(max(page, 1), pages)
    # Only this page's articles are loaded and rendered: a code of 1,500 articles stays fast.
    page_articles = list(db.scalars(select(Article).where(Article.legal_text_id == text.id).order_by(Article.order)
                                    .offset((page - 1) * ARTICLES_PER_PAGE).limit(ARTICLES_PER_PAGE)))
    shown = {}
    for article in page_articles:
        explanation = next((e for e in article.explanations if e.language == lang), None)
        if explanation is not None:
            shown[article.id] = (explanation, explanations.status(explanation, article))
    grid = playbook.load(db)["rules"]
    rules = [{"key": r["key"], "label": r["label"]} for r in grid]
    # Which risks each article of this text backs, so the page shows the links made in the risk grid.
    from app.services.splitter import normalize_number

    linked: dict[str, list[dict]] = {}
    for r in grid:
        for ref in r["articles"]:
            if ref["reference"] == text.reference:
                linked.setdefault(normalize_number(ref["number"]), []).append(
                    {"key": r["key"], "label": r["label"], "confirmed": bool(r.get("confirmed_by"))})
    return _render(request, "text_detail.html", text=text, as_of=day, wording_on=wording_on,
                   ai=request.app.state.llm.name, explained=shown, linkify=references.linkify, ref_index=ref_index,
                   defined=definitions.of_text(list(text.articles)), rules=rules, linked=linked, articles=page_articles,
                   page=page, pages=pages, total=total, first=(page - 1) * ARTICLES_PER_PAGE + 1, **extra)


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
def verify(text_id: str, request: Request, source_url: str = Form(""), db: Session = Depends(get_db)):
    corpus.verify(db, text_id, _lawyer(request), source_url)
    return _redirect(f"/texts/{text_id}", "verified_ok")


@router.post("/texts/{text_id}/unverify")
def unverify(text_id: str, request: Request, db: Session = Depends(get_db)):
    _lawyer(request)
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
def import_articles(text_id: str, request: Request, raw_text: str = Form(""), replace: str = Form(""),
                    note: str = Form(""), applies_from: str = Form(""), db: Session = Depends(get_db)):
    try:
        day = date.fromisoformat(applies_from) if applies_from else None
    except ValueError:
        return _redirect(f"/texts/{text_id}", "err_invalid", "error")
    created, _, _ = corpus.import_articles(db, text_id, raw_text, replace == "on",
                                           changed_by=getattr(request.state, "user", None), note=note, applies_from=day)
    return _redirect(f"/texts/{text_id}", "imported", count=len(created))


@router.post("/articles/{article_id}")
def update_article(
    article_id: str, request: Request, number: str = Form(""), text: str = Form(""), order: str = Form(""),
    note: str = Form(""), applies_from: str = Form(""), db: Session = Depends(get_db),
):
    article = corpus.get_article(db, article_id)
    try:
        data = ArticleIn(number=number, text=text, order=int(order) if order.strip() else None, note=note or None,
                         applies_from=date.fromisoformat(applies_from) if applies_from else None)
    except (ValidationError, ValueError):
        return _redirect(f"/texts/{article.legal_text_id}", "err_invalid", "error")
    corpus.update_article(db, article_id, data, changed_by=getattr(request.state, "user", None))
    return _redirect(f"/texts/{article.legal_text_id}", "saved")


@router.post("/texts/{text_id}/amendments", response_class=HTMLResponse)
def amendment_review(text_id: str, request: Request, amending_text: str = Form(""), applies_from: str = Form(""),
                     note: str = Form(""), file: UploadFile | None = File(None), db: Session = Depends(get_db)):
    text = corpus.get_text(db, text_id)
    if file is not None and file.filename:
        law = pdf.read_pdf(file.file.read(pdf.MAX_BYTES + 1))
        amending_text = "\n\n".join(c.raw_text for c in law.candidates)
    try:
        result = amendments.propose(request.app.state.llm, text, amending_text)
    except LlmUnavailable as error:
        return _render(request, "amendment_review.html", text=text, result=None, error=str(error),
                       ai=request.app.state.llm.name, applies_from=applies_from, note=note, changes_json="[]")
    changes = [{"number": p.number, "action": p.action, "new_text": p.new_text} for p in result.proposals]
    return _render(request, "amendment_review.html", text=text, result=result, error=None,
                   ai=request.app.state.llm.name, applies_from=applies_from, note=note,
                   changes_json=json.dumps(changes, ensure_ascii=False))


@router.post("/texts/{text_id}/amendments/apply")
def amendment_apply(text_id: str, request: Request, changes_json: str = Form("[]"), accept: list[str] = Form([]),
                    applies_from: str = Form(""), note: str = Form(""), db: Session = Depends(get_db)):
    try:
        changes = json.loads(changes_json)
        day = date.fromisoformat(applies_from) if applies_from else None
        chosen = [changes[int(i)] for i in accept]
    except (ValueError, IndexError, TypeError):
        return _redirect(f"/texts/{text_id}", "err_invalid", "error")
    count = corpus.apply_amendment(db, text_id, chosen, day, note or None, getattr(request.state, "user", None))
    return _redirect(f"/texts/{text_id}", "amendments_applied", count=count)


def _back_to_article(article) -> str:
    return f"/articles/{article.id}/go"


@router.post("/articles/{article_id}/explanation/generate")
def explanation_generate(article_id: str, request: Request, lang: str = Form("fr"), db: Session = Depends(get_db)):
    article = corpus.get_article(db, article_id)
    try:
        explanations.generate(db, request.app.state.llm, article, lang, getattr(request.state, "user", None))
    except LlmUnavailable:
        return _redirect(_back_to_article(article), "explain_error", "error")
    return _redirect(_back_to_article(article), "explanation_drafted")


@router.post("/articles/{article_id}/explanation")
def explanation_save(article_id: str, request: Request, lang: str = Form("fr"), summary: str = Form(""),
                     in_practice: str = Form(""), approve: str = Form(""), db: Session = Depends(get_db)):
    article = corpus.get_article(db, article_id)
    approver = _lawyer(request) if approve else None
    saved = explanations.save(db, article, lang, summary, in_practice, getattr(request.state, "user", None), approver)
    return _redirect(_back_to_article(article), "explanation_approved" if saved.approved_by else "saved")


@router.post("/articles/{article_id}/explanation/approve")
def explanation_approve(article_id: str, request: Request, lang: str = Form("fr"), db: Session = Depends(get_db)):
    article = corpus.get_article(db, article_id)
    explanations.approve(db, article, lang, _lawyer(request))
    return _redirect(_back_to_article(article), "explanation_approved")


@router.post("/articles/{article_id}/explanation/withdraw")
def explanation_withdraw(article_id: str, request: Request, lang: str = Form("fr"), db: Session = Depends(get_db)):
    _lawyer(request)
    article = corpus.get_article(db, article_id)
    explanations.withdraw(db, article, lang)
    return _redirect(_back_to_article(article), "unverified_ok")


@router.post("/articles/{article_id}/search")
def article_search(article_id: str, excluded: str = Form(""), db: Session = Depends(get_db)):
    article = corpus.set_excluded(db, article_id, excluded == "1")
    return _redirect(f"/texts/{article.legal_text_id}", "excluded_ok" if article.excluded_from_search else "included_ok")


@router.post("/articles/{article_id}/link-to-rule")
def article_link_to_rule(article_id: str, request: Request, key: str = Form(""), why: str = Form(""),
                         db: Session = Depends(get_db)):
    article = corpus.get_article(db, article_id)
    if not key:
        return _redirect(_back_to_article(article), "err_invalid", "error")
    playbook.add_article(db, key, article.legal_text_id, article.number, why)
    return _redirect(_rule_url(key), "pb_article_linked")


@router.post("/articles/{article_id}/suggest-rules", response_class=HTMLResponse)
def article_suggest_rules(article_id: str, request: Request, db: Session = Depends(get_db)):
    article = corpus.get_article(db, article_id)
    text = corpus.get_text(db, article.legal_text_id)
    rules_data = playbook.load(db)["rules"]
    already = {
        r["key"] for r in rules_data
        if any(a["reference"] == text.reference and a["number"] == article.number for a in r["articles"])
    }
    candidates = [{"key": r["key"], "label": r["label"], "description": r.get("description") or ""}
                  for r in rules_data if r["key"] not in already]
    try:
        suggestions = assist.suggest_rules(request.app.state.llm, text.reference or text.title, article.number,
                                           article.text, candidates)
    except LlmUnavailable:
        return _text_page(article.legal_text_id, request, db, page=_article_page(db, article), ai_error=True)
    return _text_page(article.legal_text_id, request, db, page=_article_page(db, article),
                      suggested_rules=suggestions, suggested_article_id=article.id)


@router.post("/articles/{article_id}/versions/{version_id}/restore")
def restore_version(article_id: str, version_id: str, request: Request, db: Session = Depends(get_db)):
    article = corpus.restore_version(db, article_id, version_id, changed_by=getattr(request.state, "user", None))
    return _redirect(f"/texts/{article.legal_text_id}", "restored_ok")


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


def _rule_url(key: str) -> str:
    return f"/playbook/rules/{quote(key, safe='')}"


@router.get("/playbook", response_class=HTMLResponse)
def playbook_page(request: Request, db: Session = Depends(get_db)):
    described = playbook.describe(db)
    used = {r["key"] for r in described["rules"]}
    rules = described["rules"]
    stats = {
        "rules": len(rules),
        "confirmed": sum(1 for r in rules if r.get("confirmed_by")),
        "articles": len({(a["reference"], a["number"]) for r in rules for a in r["articles"]}),
        "laws": len({playbook.law_name(a["short"]) for r in rules for a in r["articles"]}),
        "phrases": sum(len(r["phrases"]) for r in rules),
        "checks": sum(1 for r in rules for c in r["builtin_checks"] if c["enabled"]),
    }
    return _render(request, "playbook.html", pb=described, detected=playbook.DETECTED_KEYS,
                   free_keys=[k for k in playbook.DETECTED_KEYS if k not in used],
                   graph=playbook.link_map(rules), stats=stats)


@router.post("/playbook/rules")
def create_rule(request: Request, key: str = Form(""), label: str = Form(""), description: str = Form(""),
                db: Session = Depends(get_db)):
    playbook.add_rule(db, key, label, description)
    return _redirect(_rule_url(key.strip().lower().replace(" ", "_")), "saved")


@router.get("/playbook/rules/{key}", response_class=HTMLResponse)
def rule_page(key: str, request: Request, db: Session = Depends(get_db)):
    return _rule_page(key, request, db)


def _rule_page(key: str, request: Request, db: Session, **extra):
    described = playbook.describe(db)
    rule = next((r for r in described["rules"] if r["key"] == key), None)
    if rule is None:
        return _redirect("/playbook")
    texts = [t for t in corpus.list_texts(db) if t.reference and t.is_verified]
    account = getattr(request.state, "account", None)
    editor = db.get(HarveyUser, rule["edited_by_user_id"]) if rule.get("edited_by_user_id") else None
    # Four-eyes (R-M11-2): the last editor of the working copy cannot approve it.
    own_edit = bool(request.app.state.settings.four_eyes and account and editor and editor.id == account.id)
    return _render(request, "playbook_rule.html", rule=rule, texts=texts,
                   detected=playbook.DETECTED_KEYS, ai=request.app.state.llm.name,
                   versions=playbook.versions_of(db, key), editor=editor, own_edit=own_edit,
                   severities=["Critical", "High", "Medium", "Low"], **extra)


def _risk_description(rule: dict) -> str:
    detected = playbook.DETECTED_KEYS.get(rule["key"])
    name = f"{rule['label']} ({detected[1]})" if detected and detected[1] != rule["label"] else rule["label"]
    return f"{name} — {rule['description']}" if rule.get("description") else name


@router.post("/playbook/rules/{key}/suggest-articles", response_class=HTMLResponse)
def rule_suggest_articles(key: str, request: Request, text_id: str = Form(""), db: Session = Depends(get_db)):
    rule = next((r for r in playbook.describe(db)["rules"] if r["key"] == key), None)
    if rule is None:
        return _redirect("/playbook")
    text = corpus.get_text(db, text_id)
    already = {a["number"] for a in rule["articles"] if a["reference"] == text.reference}
    try:
        suggestions = assist.suggest_articles(request.app.state.llm, _risk_description(rule), text, already)
    except LlmUnavailable:
        return _rule_page(key, request, db, ai_error=True)
    return _rule_page(key, request, db, suggested_articles=suggestions, suggested_text=text)


@router.post("/playbook/rules/{key}/examples/{kind}")
def rule_add_example(key: str, kind: str, request: Request, text: str = Form(""), source: str = Form("lawyer"), db: Session = Depends(get_db)):
    playbook.add_example(db, key, kind, text, source if source in ("lawyer", "model", "missed") else "lawyer")
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/examples/{kind}/{index}/delete")
def rule_remove_example(key: str, kind: str, index: int, request: Request, db: Session = Depends(get_db)):
    playbook.remove_example(db, key, kind, index)
    return _redirect(_rule_url(key), "deleted")


@router.post("/playbook/rules/{key}/draft-examples", response_class=HTMLResponse)
def rule_draft_examples(key: str, request: Request, db: Session = Depends(get_db)):
    rule = next((r for r in playbook.describe(db)["rules"] if r["key"] == key), None)
    if rule is None:
        return _redirect("/playbook")
    known = [e["text"] for e in rule["examples"] + rule["counter_examples"]]
    try:
        drafts = assist.draft_examples(request.app.state.llm, _risk_description(rule), rule["phrases"], known)
    except LlmUnavailable:
        return _rule_page(key, request, db, ai_error=True)
    return _rule_page(key, request, db, drafted=drafts)


@router.post("/playbook/rules/{key}/phrases-accept", response_class=HTMLResponse)
async def rule_accept_phrase(key: str, request: Request, db: Session = Depends(get_db)):
    """Adds one suggested phrase and shows the page again with the other suggestions still listed."""
    form = await request.form()
    phrase = form.get("phrase", "")
    rest = [p for p in form.getlist("suggested") if p != phrase]
    try:
        playbook.add_phrase(db, key, phrase)
        flash = {"flash": "saved"}
    except RuleError as error:
        flash = {"flash": f"err_{error.code}" if is_known(f"err_{error.code}") else "err_invalid", "flash_kind": "error"}
    return _rule_page(key, request, db, suggested_phrases=rest, **flash)


@router.post("/playbook/rules/{key}/examples-accept", response_class=HTMLResponse)
async def rule_accept_example(key: str, request: Request, db: Session = Depends(get_db)):
    """Adds one drafted clause and shows the page again with the other suggestions still listed."""
    form = await request.form()
    kind, text = form.get("kind", ""), form.get("text", "")
    added = playbook.add_examples(db, key, [(kind, text)])
    rest = SimpleNamespace(examples=[t for t in form.getlist("examples") if t != text],
                           counter_examples=[t for t in form.getlist("counter_examples") if t != text])
    flash = {"flash": "examples_added", "flash_args": {"count": added, "number": ""}} if added else {}
    return _rule_page(key, request, db, drafted=rest, **flash)


@router.post("/playbook/rules/{key}/checks/{check_id}")
def rule_set_check(key: str, check_id: str, enabled: str = Form(""), severity: str = Form(""), db: Session = Depends(get_db)):
    """Switches one built-in check of a risk on or off, and sets the severity its findings get."""
    playbook.set_check(db, key, check_id, enabled == "1", severity)
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/examples-bulk")
async def rule_add_examples_bulk(key: str, request: Request, db: Session = Depends(get_db)):
    """« Tout ajouter »: every drafted example and counter-example at once."""
    form = await request.form()
    items = [("examples", t) for t in form.getlist("examples")] + [("counter_examples", t) for t in form.getlist("counter_examples")]
    added = playbook.add_examples(db, key, items)
    return _redirect(_rule_url(key), "examples_added", count=added)


@router.post("/playbook/rules/{key}/model-clause")
def rule_model_clause(key: str, request: Request, fr: str = Form(""), en: str = Form(""), source: str = Form("lawyer"),
                      approve: str = Form(""), db: Session = Depends(get_db)):
    approver = _lawyer(request) if approve else None
    playbook.set_model_clause(db, key, fr, en, source if source in ("lawyer", "model") else "lawyer", approver)
    return _redirect(_rule_url(key), "model_clause_approved" if approver else "saved")


@router.post("/playbook/rules/{key}/model-clause/approve")
def rule_model_clause_approve(key: str, request: Request, db: Session = Depends(get_db)):
    playbook.approve_model_clause(db, key, _lawyer(request))
    return _redirect(_rule_url(key), "model_clause_approved")


@router.post("/playbook/rules/{key}/model-clause/withdraw")
def rule_model_clause_withdraw(key: str, request: Request, db: Session = Depends(get_db)):
    _lawyer(request)
    playbook.withdraw_model_clause(db, key)
    return _redirect(_rule_url(key), "unverified_ok")


@router.post("/playbook/rules/{key}/model-clause/draft", response_class=HTMLResponse)
def rule_model_clause_draft(key: str, request: Request, db: Session = Depends(get_db)):
    rule = next((r for r in playbook.describe(db)["rules"] if r["key"] == key), None)
    if rule is None:
        return _redirect("/playbook")
    try:
        draft = assist.draft_model_clause(request.app.state.llm, _risk_description(rule),
                                          [a for a in rule["articles"] if a.get("text")])
    except LlmUnavailable:
        return _rule_page(key, request, db, ai_error=True)
    return _rule_page(key, request, db, drafted_clause=draft)


@router.post("/playbook/rules/{key}/suggest-phrases", response_class=HTMLResponse)
def rule_suggest_phrases(key: str, request: Request, db: Session = Depends(get_db)):
    rule = next((r for r in playbook.describe(db)["rules"] if r["key"] == key), None)
    if rule is None:
        return _redirect("/playbook")
    try:
        phrases = assist.suggest_phrases(request.app.state.llm, _risk_description(rule), rule["phrases"])
    except LlmUnavailable:
        return _rule_page(key, request, db, ai_error=True)
    return _rule_page(key, request, db, suggested_phrases=phrases)


@router.post("/playbook/rules/{key}/label")
def rule_label(key: str, request: Request, label: str = Form(""), description: str = Form(""),
               db: Session = Depends(get_db)):
    playbook.update_label(db, key, label, description)
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/delete")
def rule_delete(key: str, request: Request, db: Session = Depends(get_db)):
    playbook.delete_rule(db, key)
    return _redirect("/playbook", "deleted")


@router.post("/playbook/rules/{key}/articles")
def rule_add_article(key: str, request: Request, text_id: str = Form(""), number: str = Form(""),
                     why: str = Form(""), db: Session = Depends(get_db)):
    if not text_id or not number.strip():
        return _redirect(_rule_url(key), "err_invalid", "error")
    playbook.add_article(db, key, text_id, number, why)
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/articles/{index}")
def rule_update_article(key: str, index: int, request: Request, why: str = Form(""), db: Session = Depends(get_db)):
    playbook.update_article(db, key, index, why)
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/phrases")
def rule_add_phrase(key: str, request: Request, phrase: str = Form(""), db: Session = Depends(get_db)):
    playbook.add_phrase(db, key, phrase)
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/phrases/{index}/delete")
def rule_remove_phrase(key: str, index: int, request: Request, db: Session = Depends(get_db)):
    playbook.remove_phrase(db, key, index)
    return _redirect(_rule_url(key), "deleted")


@router.post("/playbook/rules/{key}/articles/{index}/delete")
def rule_remove_article(key: str, index: int, request: Request, db: Session = Depends(get_db)):
    playbook.remove_article(db, key, index)
    return _redirect(_rule_url(key), "deleted")


@router.post("/playbook/rules/{key}/articles/{index}/move")
def rule_move_article(key: str, index: int, request: Request, offset: int = Form(0), db: Session = Depends(get_db)):
    playbook.move_article(db, key, index, offset)
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/severity")
def rule_severity(key: str, request: Request, severity: str = Form(""), db: Session = Depends(get_db)):
    playbook.set_severity(db, key, severity)
    return _redirect(_rule_url(key), "saved")


@router.post("/playbook/rules/{key}/confirm")
def rule_confirm(key: str, request: Request, db: Session = Depends(get_db)):
    playbook.approve(db, key, _lawyer(request), request.app.state.settings.four_eyes)
    return _redirect(_rule_url(key), "rule_confirmed")


@router.post("/playbook/rules/{key}/unconfirm")
def rule_unconfirm(key: str, request: Request, db: Session = Depends(get_db)):
    _lawyer(request)
    playbook.withdraw(db, key)
    return _redirect(_rule_url(key), "unverified_ok")
