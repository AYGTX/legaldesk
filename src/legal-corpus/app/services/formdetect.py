"""Finds the fields of a contract PDF (text boxes, checkboxes, signatures) so a company can turn it into a template.

Two sources, merged: the fillable fields already inside the PDF (read with pypdf, no model needed), and — when the
optional CommonForms package is installed — a vision model that finds the blanks of a *static* PDF. Each field then
gets a label read from the text next to it, and a suggested type, fill role and auto-fill source from EN/FR rules.
A language model may tidy the labels, but only when it runs locally: contracts are never sent to a remote provider.

The output uses SignFlow's coordinates: top-left origin, pixels at RENDER_SCALE times the PDF's points. Every field is
a proposal for a person to review in the template editor, never saved on its own."""

import io
import re
import tempfile
import unicodedata
from dataclasses import dataclass, field, replace
from pathlib import Path

from pypdf import PdfReader

from app.services.llm import LlmProvider, LlmUnavailable

RENDER_SCALE = 1.5  # SignFlow stamps fields at x / 1.5 points (PdfStampService).
MODEL_NAME = "FFDNet-L"
MIN_SIDE_POINTS = 4.0
LABEL_NEAR_POINTS = 40.0  # a label further than this left of its box is not its label
LABEL_JOIN_POINTS = 12.0  # words closer than this belong to the same label
LABEL_ABOVE_POINTS = 28.0


@dataclass(frozen=True)
class Widget:
    """A field box in PDF points, origin top-left."""
    page: int  # 1-based
    x: float
    y: float
    width: float
    height: float
    kind: str  # "text" | "checkbox" | "signature" | "choice"
    name: str = ""
    options: tuple[str, ...] = ()


@dataclass(frozen=True)
class TextRun:
    x: float
    y: float  # baseline, origin top-left
    text: str
    end: float = 0.0  # estimated right edge: pypdf gives no glyph widths, so ~half the font size per character


@dataclass
class DetectedField:
    field_key: str
    label: str
    type: str  # PdfFieldType: Text, Date, Number, Checkbox, Signature, Dropdown, Textarea
    fill_role: str  # PdfFieldFillRole: Requester, Approver, System
    page: int
    x: float
    y: float
    width: float
    height: float
    is_required: bool = False
    auto_fill_from: str | None = None
    options: list[str] = field(default_factory=list)
    source: str = "pdf"  # "pdf" = already fillable, "model" = found by the detector
    confidence: str = "rule"  # "rule" or "ai"


@dataclass
class Detection:
    fields: list[DetectedField]
    pages: int
    model: str | None  # None when the vision model is not installed
    ai_used: bool = False
    notes: list[str] = field(default_factory=list)


# ---------------------------------------------------------------- reading widgets

def _norm(text: str) -> str:
    text = unicodedata.normalize("NFD", text.lower())
    return "".join(c for c in text if unicodedata.category(c) != "Mn")


def read_widgets(pdf: bytes) -> tuple[list[Widget], list[list[TextRun]], int]:
    """Every widget annotation in the PDF plus the text runs of each page."""
    reader = PdfReader(io.BytesIO(pdf))
    widgets: list[Widget] = []
    runs: list[list[TextRun]] = []
    for number, page in enumerate(reader.pages, start=1):
        box = page.mediabox
        height = float(box.top) - float(box.bottom)
        left, bottom = float(box.left), float(box.bottom)
        runs.append(_text_runs(page, height, left, bottom))
        for ref in page.get("/Annots") or []:
            annot = ref.get_object()
            if annot.get("/Subtype") != "/Widget":
                continue
            rect = [float(v) for v in annot.get("/Rect", [])]
            if len(rect) != 4:
                continue
            x0, x1 = sorted((rect[0], rect[2]))
            y0, y1 = sorted((rect[1], rect[3]))
            if x1 - x0 < MIN_SIDE_POINTS or y1 - y0 < MIN_SIDE_POINTS:
                continue
            kind, options = _widget_kind(annot)
            widgets.append(Widget(number, x0 - left, height - (y1 - bottom), x1 - x0, y1 - y0, kind,
                                  str(_inherited(annot, "/T") or ""), options))
    return widgets, runs, len(reader.pages)


def _inherited(annot, key):
    node = annot
    while node is not None:
        if key in node:
            return node[key]
        parent = node.get("/Parent")
        node = parent.get_object() if parent else None
    return None


def _widget_kind(annot) -> tuple[str, tuple[str, ...]]:
    ft = str(_inherited(annot, "/FT") or "")
    flags = int(_inherited(annot, "/Ff") or 0)
    if ft == "/Sig":
        return "signature", ()
    if ft == "/Btn":
        return "checkbox", ()
    if ft == "/Ch":
        names = []
        for opt in _inherited(annot, "/Opt") or []:
            opt = opt.get_object()
            names.append(str(opt[-1] if isinstance(opt, list) else opt))
        names = tuple(names)
        return "choice", names
    if flags & (1 << 12):  # multiline text
        return "textarea", ()
    return "text", ()


def _text_runs(page, height: float, left: float, bottom: float) -> list[TextRun]:
    found: list[TextRun] = []

    def visit(text, cm, tm, font_dict, font_size):
        if not text or not text.strip():
            return
        x = tm[4] * cm[0] + tm[5] * cm[2] + cm[4]
        y = tm[4] * cm[1] + tm[5] * cm[3] + cm[5]
        size = (font_size or 10) * (abs(tm[3] * cm[3]) or 1)
        found.append(TextRun(x - left, height - (y - bottom), text, x - left + len(text) * size * 0.5))

    try:
        page.extract_text(visitor_text=visit)
    except Exception:  # an odd content stream must not stop the detection of the fields themselves
        return []
    return found


# ---------------------------------------------------------------- labels and types

def nearest_label(widget: Widget, runs: list[TextRun]) -> str:
    """The text printed just left of the box on its line (stopping at the gap before a neighbouring field's text),
    else the line just above it."""
    baseline = widget.y + widget.height * 0.8
    if widget.kind == "checkbox":
        # A checkbox is normally followed by its text ("☐ Approuvé"); only fall back to the left when nothing follows.
        right = sorted((r for r in runs if abs(r.y - baseline) <= max(6.0, widget.height * 0.6)
                        and widget.x + widget.width - 1 <= r.x <= widget.x + widget.width + LABEL_NEAR_POINTS),
                       key=lambda r: r.x)
        if right:
            chain, edge = [right[0]], right[0].end
            for r in right[1:]:
                if r.x - edge > LABEL_JOIN_POINTS:
                    break
                chain.append(r)
                edge = max(edge, r.end)
            label = _clean_label(" ".join(r.text for r in chain))
            if label:
                return label
    left = [r for r in runs if abs(r.y - baseline) <= max(6.0, widget.height * 0.6) and r.x < widget.x + 1]
    left.sort(key=lambda r: r.end, reverse=True)
    if left and widget.x - left[0].end <= LABEL_NEAR_POINTS:
        chain, edge = [left[0]], left[0].x
        for r in left[1:]:
            if edge - r.end > LABEL_JOIN_POINTS:
                break
            chain.append(r)
            edge = min(edge, r.x)
        label = _clean_label(" ".join(r.text for r in sorted(chain, key=lambda r: r.x)))
        if label:
            return label
    above = [r for r in runs if widget.y - LABEL_ABOVE_POINTS <= r.y <= widget.y + 1
             and widget.x - 10 <= r.x <= widget.x + widget.width]
    if above:
        line = max(r.y for r in above)
        return _clean_label(" ".join(r.text for r in sorted(above, key=lambda r: r.x) if abs(r.y - line) < 3))
    return ""


def _clean_label(text: str) -> str:
    text = re.sub(r"[_\.…]{2,}", " ", text)
    text = re.sub(r"[☐☑☒□■*]", " ", text)
    text = " ".join(text.split()).strip(" :;-–—|")
    return text[-60:].strip() if len(text) > 60 else text


# (rule key, pattern on accent-stripped lowercase label) — first match wins.
_TYPE_RULES: list[tuple[str, str]] = [
    ("signature", r"signature|signed|sign here|signataire|paraphe|initial|lu et approuve|cachet|stamp"),
    ("date", r"\bdate\b|\bdu\b.*\bau\b|fait le|signed on|effective|echeance|expir|\bjj\b|dd/mm|le jour"),
    ("email", r"e-?mail|courriel|mail"),
    ("number", r"montant|amount|prix|price|total|salaire|salary|capital|\bn[°o]\b|numero|number|\bcin\b|tva|vat|"
               r"matricule|duree|duration|quantite|quantity|tel|phone|mobile|fax|rib|iban"),
    ("company", r"societe|company|raison sociale|entreprise|denomination|employeur|employer"),
    ("name", r"\bnom\b|prenom|name|represent|gerant|directeur|titulaire"),
    ("address", r"adresse|address|siege|domicile|ville|city"),
]

# Only the requester's own company is filled from their profile: a name, email or address next to "client",
# "fournisseur" or "salarié" belongs to the other party, and filling it with the requester's details would be wrong.
_AUTOFILL = {"company": "user.company"}


def classify(widget: Widget, label: str) -> tuple[str, str, str | None]:
    """(PdfFieldType, PdfFieldFillRole, auto-fill source) from the widget kind and its label."""
    text = _norm(label or widget.name)
    if widget.kind == "signature":
        return "Signature", "Approver", None
    if widget.kind == "checkbox":
        return "Checkbox", "Requester", None
    if widget.kind == "choice":
        return "Dropdown", "Requester", None
    rule = next((key for key, pattern in _TYPE_RULES if re.search(pattern, text)), None)
    if rule == "signature":
        return "Signature", "Approver", None
    if rule == "date":
        # "Fait le" at the foot of a contract is the day it is signed; a validity period is the requester's call.
        if re.search(r"fait le|signed on|le jour", text):
            return "Date", "System", "system.today"
        return "Date", "Requester", None
    if rule == "number":
        return "Number", "Requester", None
    if widget.kind == "textarea" or widget.height >= 48:
        return "Textarea", "Requester", None
    if rule in _AUTOFILL and _AUTOFILL[rule]:
        return "Text", "Requester", _AUTOFILL[rule]
    return "Text", "Requester", None


def slug(text: str, taken: set[str]) -> str:
    base = re.sub(r"[^a-z0-9]+", "_", _norm(text)).strip("_")[:40] or "field"
    key, n = base, 2
    while key in taken:
        key, n = f"{base}_{n}", n + 1
    taken.add(key)
    return key


# ---------------------------------------------------------------- vision model (optional)

def load_model():
    """The CommonForms entry point, or None when the package is not installed."""
    try:
        from commonforms import prepare_form  # type: ignore
    except ImportError:
        return None
    return prepare_form


def model_widgets(pdf: bytes, prepare_form) -> list[Widget]:
    """Runs the detector on a static PDF; it writes a fillable copy whose widgets we read back."""
    with tempfile.TemporaryDirectory() as tmp:
        source, target = Path(tmp, "in.pdf"), Path(tmp, "out.pdf")
        source.write_bytes(pdf)
        # FFDNet-L, not CommonForms' default FFDetr: that one breaks on rfdetr >= 1.11 ("list has no with_nms").
        # use_signature_fields keeps signature boxes as signatures instead of text boxes.
        prepare_form(str(source), str(target), model_or_path=MODEL_NAME, use_signature_fields=True)
        return read_widgets(target.read_bytes())[0]


def _overlaps(a: Widget, b: Widget) -> bool:
    if a.page != b.page:
        return False
    w = min(a.x + a.width, b.x + b.width) - max(a.x, b.x)
    h = min(a.y + a.height, b.y + b.height) - max(a.y, b.y)
    return w > 0 and h > 0 and (w * h) / min(a.width * a.height, b.width * b.height) > 0.5


# ---------------------------------------------------------------- entry point

def detect(pdf: bytes, llm: LlmProvider | None = None, prepare_form=None) -> Detection:
    """Fields of the PDF in reading order. `prepare_form` is injected in tests; by default the installed model is used."""
    existing, runs, pages = read_widgets(pdf)
    prepare = prepare_form or load_model()
    notes: list[str] = []
    found = [(w, "pdf") for w in existing]
    model_name = None
    if prepare is not None:
        model_name = "commonforms"
        try:
            for widget in model_widgets(pdf, prepare):
                if not any(_overlaps(widget, e) for e in existing):
                    found.append((widget, "model"))
        except Exception as error:  # a model failure must not lose the fields already in the PDF
            notes.append(f"model_failed: {error}")
    found.sort(key=lambda item: (item[0].page, round(item[0].y / 6), item[0].x))

    taken: set[str] = set()
    fields: list[DetectedField] = []
    for widget, source in found:
        label = nearest_label(widget, runs[widget.page - 1] if widget.page <= len(runs) else [])
        kind, role, auto = classify(widget, label)
        # The model names its widgets "textbox_0_6": not a label a person should see.
        named = widget.name if widget.name and not re.fullmatch(r"(textbox|checkbox|signature|choice)_\d+_\d+|undefined|null", widget.name, re.I) else ""
        shown = label or named or f"Field {len(fields) + 1}"
        fields.append(DetectedField(
            field_key=slug(shown, taken), label=shown, type=kind, fill_role=role, page=widget.page,
            x=round(widget.x * RENDER_SCALE, 1), y=round(widget.y * RENDER_SCALE, 1),
            width=round(widget.width * RENDER_SCALE, 1), height=round(widget.height * RENDER_SCALE, 1),
            is_required=kind in ("Signature", "Date", "Text", "Number") and role != "System",
            auto_fill_from=auto, options=list(widget.options), source=source))

    ai_used = False
    if llm is not None and fields and getattr(llm, "name", "none") == "local":
        fields, ai_used = _refine(fields, llm, notes)
    return Detection(fields, pages, model_name, ai_used, notes)


_REFINE_SYSTEM = (
    "You help prepare a contract PDF as a signing template. You receive blank fields, each with the text printed next to "
    "it. For each field return a short, clear label in the language of the contract and, if obvious, a type among "
    "Text, Date, Number, Checkbox, Signature, Dropdown, Textarea. Never invent fields, never change their number. "
    'Answer JSON: {"fields": [{"index": 0, "label": "...", "type": "..."}]}.')
_TYPES = {"Text", "Date", "Number", "Checkbox", "Signature", "Dropdown", "Textarea"}


def _refine(fields: list[DetectedField], llm: LlmProvider, notes: list[str]) -> tuple[list[DetectedField], bool]:
    lines = [f"{i}. page {f.page}, current label: {f.label!r}, type: {f.type}" for i, f in enumerate(fields)]
    try:
        answer = llm.complete_json(_REFINE_SYSTEM, "\n".join(lines))
    except LlmUnavailable as error:
        notes.append(f"ai_unavailable: {error}")
        return fields, False
    refined = list(fields)
    changed = False
    for item in answer.get("fields", []) if isinstance(answer.get("fields"), list) else []:
        index = item.get("index") if isinstance(item, dict) else None
        if not isinstance(index, int) or not 0 <= index < len(refined):
            continue
        current = refined[index]
        label = str(item.get("label") or "").strip()[:60] or current.label
        kind = item.get("type") if item.get("type") in _TYPES else current.type
        # The widget kind is a hard fact; the language model may not turn a signature box into a text box.
        if current.type in ("Signature", "Checkbox", "Dropdown"):
            kind = current.type
        if (label, kind) != (current.label, current.type):
            refined[index] = replace(current, label=label, type=kind, confidence="ai")
            changed = True
    return refined, changed
