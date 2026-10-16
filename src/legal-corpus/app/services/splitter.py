"""Splits a pasted law into articles by their headings ("Article premier", "Art. 12 bis", "الفصل 3"), and undoes the
print layout: lines wrapped by the page width are joined, real paragraphs (alinéas) and list items are kept, and the
chapter/section titles that sit between articles are taken out."""

import re
from dataclasses import dataclass, field

_FIRST = {"premier", "1er", "première", "الأول"}

_HEADING = re.compile(
    r"^[ \t]*(?:article|art\.|الفصل)[ \t]+"
    # Prints also spell suffixes "quinter"/"sexis" and wrap them in guillemets: « quinter ».
    r"(?P<number>premier|première|1er|الأول|\d+(?:[ \t]*[«\"]?[ \t]*"
    r"(?:bis|ter|quater|quinquies|quinter|sexies|sexis|septies|octies)[ \t]*[»\"]?)?)"
    r"(?!\w)[ \t]*[.\-–—:)]*[ \t]*",
    re.IGNORECASE | re.MULTILINE,
)

# A line that ends like this closes a paragraph; any other line was wrapped by the page width.
_PARAGRAPH_END = re.compile(r"[.:;!?»)]$")
# A line that starts like this opens a new item even when the previous line has no closing punctuation.
_LIST_ITEM = re.compile(r"^(?:[-–—•*]\s|\d+\s*[)°./]\s|[a-z]\)\s|[ivx]+\)\s)", re.IGNORECASE)
# Separator rules some PDF extractors emit ("/tatweel/tatweel/...", "_____").
_ARTIFACT = re.compile(r"^(?:(?:/tatweel)+/?|[_\-–—.=*\s]{3,})$")
# Case-sensitive on purpose: a wrapped sentence often continues with "chapitre trois du ..." or "paragraphe de l'article".
_STRUCTURE = re.compile(
    r"^(?:LIVRE|Livre|TITRE|Titre|SOUS-TITRE|Sous-titre|Sous titre|CHAPITRE|Chapitre|SECTION|Section|SOUS-SECTION|"
    r"Sous-section|PARAGRAPHE|Paragraphe|Parag\.|الباب|القسم|العنوان)(?!\w)"
)


@dataclass(frozen=True)
class SplitArticle:
    number: str
    text: str


@dataclass(frozen=True)
class SplitResult:
    preamble: str
    articles: list[SplitArticle]
    # Chapter/section titles removed from the end of articles, so a person can check nothing else was dropped.
    removed_headings: list[str] = field(default_factory=list)


def normalize_number(raw: str) -> str:
    raw = re.sub(r"\s+", " ", re.sub(r"[«»\"]", " ", raw).strip())
    return "1" if raw.lower() in _FIRST else raw.lower()


def unwrap(text: str) -> list[str]:
    """Joins page-width line wraps back into paragraphs."""
    paragraphs: list[str] = []
    for line in (re.sub(r"[ \t]+", " ", raw).strip() for raw in text.split("\n")):
        if _ARTIFACT.match(line):
            continue
        if not line:
            if paragraphs and paragraphs[-1]:
                paragraphs.append("")
            continue
        previous = paragraphs[-1] if paragraphs else ""
        if previous and not _PARAGRAPH_END.search(previous) and not _LIST_ITEM.match(line):
            paragraphs[-1] = f"{previous} {line}"
        else:
            paragraphs.append(line)
    return [p for p in paragraphs if p]


def _strip_trailing_headings(paragraphs: list[str]) -> tuple[list[str], list[str]]:
    """Titles printed before the next article have no closing punctuation; the article's own text always does."""
    removed: list[str] = []
    while len(paragraphs) > 1 and (_STRUCTURE.match(paragraphs[-1]) or not _PARAGRAPH_END.search(paragraphs[-1])):
        removed.insert(0, paragraphs.pop())
    # A structure keyword that got its title merged in is still a heading, wherever the article ends.
    while len(paragraphs) > 1 and _STRUCTURE.match(paragraphs[-1]):
        removed.insert(0, paragraphs.pop())
    return paragraphs, removed


def split_articles(raw_text: str) -> SplitResult:
    text = raw_text.replace("\r\n", "\n")
    matches = list(_HEADING.finditer(text))
    if not matches:
        return SplitResult(preamble="\n".join(unwrap(text)), articles=[])

    articles: list[SplitArticle] = []
    removed: list[str] = []
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        paragraphs, dropped = _strip_trailing_headings(unwrap(text[match.end():end]))
        removed.extend(dropped)
        if paragraphs:
            articles.append(SplitArticle(number=normalize_number(match.group("number")), text="\n".join(paragraphs)))

    preamble, dropped = _strip_trailing_headings(unwrap(text[: matches[0].start()]) or [""])
    return SplitResult(preamble="\n".join(preamble).strip(), articles=articles, removed_headings=removed + dropped)
