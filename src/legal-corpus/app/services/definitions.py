"""Finds the terms a law defines, so a reader (or an agent) can jump from "dépôts" to the article that says what the
law means by it. Three ways French-language Tunisian law defines a term:

- a list: « Au sens de la présente loi, on entend par : - Echanges électroniques : … - Commerce électronique : … »
- one term: « on entend par données à caractère personnel toutes les informations … »
- « Est considérée banque, toute personne morale … » / « Sont considérés dépôts reçus du public au sens de la présente
  loi, les fonds … »
"""

import re
from dataclasses import dataclass

from app.models import Article

_LIST = re.compile(r"on\s+entend\s+par(?:\s+les\s+termes\s+suivants)?\s*:\s*(?P<body>.+)", re.IGNORECASE | re.DOTALL)
_ITEM = re.compile(r"(?:^|\s)[-–*•]\s*(?P<term>[^:–\-*•\n]{2,80}?)\s*:\s*(?P<def>.+?)(?=\s[-–*•]\s*[^:\n]{2,80}?\s*:|\Z)", re.DOTALL)
_ONE = re.compile(
    r"on\s+entend\s+par\s+(?!les\s+termes)(?P<term>[^,:;]{3,80}?)\s*(?:,\s*|\s+(?=(?:toute?s?|tous|le|la|les|l['’]|un|une)\s))(?P<def>[^.]{10,})",
    re.IGNORECASE)
_DEEMED = re.compile(
    r"(?:^|[.;]\s+)(?:Est|Sont)\s+considérée?s?\s+(?:comme\s+)?(?P<term>[^,:;]{3,70}?)\s*(?:,|\s+au\s+sens\s+d[eu]|\s*:)\s*(?P<def>[^.]{10,})")
_LEADING_ARTICLE = re.compile(r"^(?:(?:le|la|les|un|une|des)\s+|l['’]\s*|d['’]\s*)", re.IGNORECASE)
_LEFTOVER = re.compile(r"^(?:(?:au\s+sens\s+)?(?:de\s+)?(?:la\s+présente\s+loi|du\s+présent\s+(?:article|code|décret))\s*[,:]?\s*)+",
                       re.IGNORECASE)
_NOT_A_TERM = re.compile(r"^(?:dans|nulle?s?|comme|au sens|à|par|en|pour|de)\b", re.IGNORECASE)


@dataclass(frozen=True)
class Definition:
    term: str
    definition: str
    article_id: str
    article_number: str


def _term(raw: str) -> str | None:
    term = re.sub(r"\s+", " ", raw).strip(" ,.-–*•")
    term = _LEADING_ARTICLE.sub("", term)
    if not 3 <= len(term) <= 70 or _NOT_A_TERM.match(term) or term.count(" ") > 8:
        return None
    return term[:1].upper() + term[1:]


def _clip(text: str, limit: int = 260) -> str:
    text = _LEFTOVER.sub("", re.sub(r"\s+", " ", text).strip(" ,;"))
    return text if len(text) <= limit else text[:limit].rsplit(" ", 1)[0] + " …"


def find(article: Article) -> list[Definition]:
    text, found = article.text, []
    listed = _LIST.search(text)
    if listed:
        for item in _ITEM.finditer(" " + listed.group("body")):
            term = _term(item.group("term"))
            if term:
                found.append(Definition(term, _clip(item.group("def")), article.id, article.number))
    else:
        for m in _ONE.finditer(text):
            term = _term(m.group("term"))
            if term:
                found.append(Definition(term, _clip(m.group("def")), article.id, article.number))
    for m in _DEEMED.finditer(text):
        term = _term(m.group("term"))
        if term:
            found.append(Definition(term, _clip(m.group("def")), article.id, article.number))
    return found


def of_text(articles: list[Article]) -> list[Definition]:
    """Every definition in a text, alphabetically, the first definition of a term kept."""
    seen: dict[str, Definition] = {}
    for article in articles:
        for d in find(article):
            seen.setdefault(d.term.casefold(), d)
    return sorted(seen.values(), key=lambda d: d.term.casefold())


def matcher(defined: list[Definition]) -> re.Pattern | None:
    """One pattern for every defined term (longest first, singular or plural, any case), or None."""
    terms = sorted({d.term for d in defined}, key=len, reverse=True)
    if not terms:
        return None
    alternatives = "|".join(r"\s+".join(map(re.escape, t.split())) + r"(?:s|x)?" for t in terms)
    return re.compile(rf"(?<![\w-])(?:{alternatives})(?![\w-])", re.IGNORECASE)


def lookup(defined: list[Definition], found: str) -> Definition | None:
    key = re.sub(r"\s+", " ", found).casefold()
    by_term = {d.term.casefold(): d for d in defined}
    return by_term.get(key) or by_term.get(key[:-1])
