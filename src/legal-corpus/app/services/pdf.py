"""Reads a law out of a PDF, locally: the text is extracted page by page, the gazette's running headers are taken out,
and a gazette page that carries several texts (a law, then two arrêtés) is cut into one candidate per text, so a person
picks the one to import. Nothing is stored here — the result goes to a review screen first."""

import io
import re
from collections import Counter
from dataclasses import dataclass

from pypdf import PdfReader
from pypdf.errors import PdfReadError

from app.services.corpus import RuleError
from app.services.splitter import split_articles

MAX_BYTES = 20 * 1024 * 1024
# Below this many characters per page on average, the PDF is a scan (images) and has no text layer to read.
_MIN_CHARS_PER_PAGE = 80

# "N° 58 Journal Officiel de la République Tunisienne — 15 juillet 2016 Page 2195" and its mirror on even pages.
_GAZETTE_HEADER = re.compile(r"^\s*(?:N°\s*\d+\s+)?Journal Officiel de la République Tunisienne.*$|^\s*Page\s+\d+\s+Journal Officiel.*$",
                             re.IGNORECASE)
_PAGE_NUMBER = re.compile(r"^\s*(?:Page\s+)?\d{1,4}\s*$", re.IGNORECASE)
# A new text in the gazette starts with its kind, then "du"/"n°" on the same line: "Loi n° 2016-48 du …", "Arrêté du
# ministre … du …". Case-sensitive, so a wrapped "loi n° 2009-64" inside a sentence is not mistaken for a new text.
_TEXT_START = re.compile(
    r"^(?:Loi(?:\s+organique)?|Décret(?:-loi|\s+gouvernemental|\s+présidentiel|\s+Présidentiel)?|Arrêté|Décision|Circulaire)"
    r"\b[^\n]{0,80}?\b(?:n°|du)\s",
)
# The enacting words that follow a title whose last line has no full stop.
_TITLE_END = re.compile(r"^(?:Au nom du peuple|Le Président|Le ministre|Le Chef du|Vu\s)", re.IGNORECASE)
# A law "portant promulgation du code X" reproduces the code as an annex: a standalone all-caps title line ("CODE DES
# ASSURANCES"), then the code's own numbering restarts at "Article premier" — a second text with no Loi/Décret header
# of its own to trigger _TEXT_START, bundled inside the promulgation law's candidate. Its first few articles then
# collide in number with the promulgation law's own short articles ("Article premier" through "Article 4 or 5"),
# so without cutting here too, the two get imported as one text and "Article premier" exists twice.
_CODE_ANNEX_TITLE = re.compile(r"^(CODE\s+(?:DES|DU|DE(?:\s+LA)?|D')\s*[A-ZÀ-ÞŒ][A-ZÀ-ÞŒ' -]{2,77})\s*$")
# The citation part of a title: everything up to the date's year (and a number after it, e.g. "du 1er mars 2016").
_REFERENCE = re.compile(r"^(.*?\b(?:19|20)\d{2})(?=[\s,.(]|$)")


@dataclass(frozen=True)
class Candidate:
    title: str
    reference: str
    raw_text: str
    article_count: int


@dataclass(frozen=True)
class PdfLaw:
    pages: int
    candidates: list[Candidate]


def _key(line: str) -> str:
    return re.sub(r"\d+", "#", line.strip().lower())


def _clean(pages: list[str]) -> str:
    # Any other line repeated at the top or bottom of most pages is a running header or footer.
    edges = Counter(_key(line) for page in pages for line in (page.splitlines()[:2] + page.splitlines()[-2:]) if line.strip())
    repeated = {k for k, n in edges.items() if len(pages) >= 3 and n >= len(pages) * 0.6}

    kept = []
    for page in pages:
        for line in page.splitlines():
            if _GAZETTE_HEADER.match(line) or _PAGE_NUMBER.match(line) or _key(line) in repeated:
                continue
            kept.append(line.rstrip())
    text = "\n".join(kept)
    # Superscripts come out detached: "1 er mars", "Article 1 er".
    return re.sub(r"\b1\s+er\b", "1er", text)


def _title(lines: list[str]) -> str:
    """The title runs from the start line until a line closes it with a full stop (at most five lines)."""
    taken = []
    for line in lines[:5]:
        if taken and _TITLE_END.match(line.strip()):
            break
        taken.append(line.strip())
        if line.rstrip().endswith("."):
            break
    title = re.sub(r"\s+", " ", " ".join(taken))
    # Gazette prints sometimes split a number around its hyphen: "n° 2015 -26" must read "n° 2015-26".
    title = re.sub(r"(\d{2,4})\s*-\s*(\d+)", r"\1-\2", title)
    return re.sub(r"\s*\(\d+\)\s*\.?$", "", title).rstrip(" .")


def _candidates(text: str) -> list[Candidate]:
    lines = text.split("\n")
    starts = [i for i, line in enumerate(lines) if _TEXT_START.match(line.strip()) or _CODE_ANNEX_TITLE.match(line.strip())]
    if not starts:
        starts = [0]
    elif starts[0] != 0 and "\n".join(lines[: starts[0]]).strip():
        # Text before the first title (the end of the previous issue's text) is kept as its own candidate.
        starts = [0, *starts]

    candidates = []
    for index, start in enumerate(starts):
        end = starts[index + 1] if index + 1 < len(starts) else len(lines)
        raw = "\n".join(lines[start:end]).strip()
        if not raw:
            continue
        first_line = lines[start].strip()
        annex = _CODE_ANNEX_TITLE.match(first_line)
        title = _title(lines[start:end]) if _TEXT_START.match(first_line) else (annex.group(1).capitalize() if annex else "")
        reference = _REFERENCE.match(title)
        count = len(split_articles(raw).articles)
        # A scrap with no articles is not an importable text: either the tail of the previous page (no title), or a
        # table-of-contents line that happens to read like a title (e.g. "Décret n° ... du ... 161" with no body).
        if count == 0:
            continue
        candidates.append(Candidate(
            title=title[:300],
            reference=reference.group(1) if reference else "",
            raw_text=raw,
            article_count=count,
        ))
    return candidates


def read_pdf(data: bytes) -> PdfLaw:
    if len(data) > MAX_BYTES:
        raise RuleError("pdf_too_large", "The PDF is larger than 20 MB.")
    try:
        reader = PdfReader(io.BytesIO(data))
        if reader.is_encrypted and reader.decrypt("") == 0:
            raise RuleError("pdf_unreadable", "The PDF is password-protected.")
        pages = [page.extract_text() or "" for page in reader.pages]
    except (PdfReadError, ValueError, KeyError, TypeError) as error:
        raise RuleError("pdf_unreadable", f"The file could not be read as a PDF ({error}).") from error

    if not pages or sum(len(p.strip()) for p in pages) / len(pages) < _MIN_CHARS_PER_PAGE:
        raise RuleError("pdf_scanned", "The PDF has no text layer (a scan); it needs OCR first.")
    return PdfLaw(pages=len(pages), candidates=_candidates(_clean(pages)))


def guess_language(text: str) -> str:
    letters = [c for c in text if c.isalpha()]
    if letters and sum("؀" <= c <= "ۿ" for c in letters) / len(letters) > 0.3:
        return "ar"
    words = re.findall(r"[a-zà-ÿ]+", text.lower())
    english = sum(w in {"the", "and", "of", "shall"} for w in words)
    french = sum(w in {"le", "la", "les", "et", "des", "du"} for w in words)
    return "en" if english > french else "fr"


ROLES = ("main", "promulgation", "amendment", "ignore")
_PROMULGATION = re.compile(r"portant\s+promulgation|promulguant", re.IGNORECASE)
_AMENDMENT = re.compile(r"portant\s+(?:approbation|modification|réorganisation)|modifiant|complétant|abrogeant", re.IGNORECASE)


def guess_roles(candidates: list[Candidate]) -> list[str]:
    """What each text of a codified edition is: the act that put the code in force, a law already written into it,
    or the code itself (the text with the most articles). Shown to a person, who can change it."""
    roles = []
    for c in candidates:
        if _PROMULGATION.search(c.title):
            roles.append("promulgation")
        elif _AMENDMENT.search(c.title):
            roles.append("amendment")
        else:
            roles.append("other")
    others = [i for i, r in enumerate(roles) if r == "other"]
    main = max(others or range(len(candidates)), key=lambda i: candidates[i].article_count)
    return ["main" if i == main else ("ignore" if r == "other" else r) for i, r in enumerate(roles)]


def combine(candidates: list[Candidate], roles: list[str]) -> tuple[Candidate, str, str]:
    """The text to import, with its reference taken from the promulgation act and a summary line naming the laws
    already written into it."""
    main = next(c for c, r in zip(candidates, roles) if r == "main")
    promulgation = next((c for c, r in zip(candidates, roles) if r == "promulgation"), None)
    amendments = [c.title for c, r in zip(candidates, roles) if r == "amendment"]
    reference = main.reference or main.title
    if promulgation is not None:
        act = re.split(r",\s*portant", promulgation.title, maxsplit=1)[0].strip()
        reference = f"{main.title} ({act})" if main.title else act
    summary = ("Version consolidée, intégrant : " + " ; ".join(amendments) + ".") if amendments else ""
    return main, reference, summary
