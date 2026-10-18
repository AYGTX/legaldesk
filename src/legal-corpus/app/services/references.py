"""Finds the references an article makes — to other articles of the same text ("l'article 43 de la présente loi"), to
another law ("la loi organique n° 2015-26 du 7 août 2015") or to an article of another law ("l'article 5 de la loi
n° 99-64") — and resolves them against the corpus. Unresolved laws are the texts the corpus cites but does not hold:
the list of PDFs worth fetching next."""

import re
from collections import defaultdict
from dataclasses import dataclass, field

from markupsafe import Markup, escape

from app.models import Article, LegalText
from app.services import definitions
from app.services.splitter import normalize_number

_NUMBER = r"(?:premier|\d+(?:\s+(?:bis|ter|quater|quinquies|sexies))?)"
_ARTICLES = rf"(?P<arts>{_NUMBER}(?:\s*(?:,|et|à)\s*{_NUMBER})*)"
_LAW = (r"(?P<law>(?:loi|décret|décret-loi|décret\s+gouvernemental|arrêté)(?:\s+organique)?\s+n°\s*"
        r"(?P<year>\d{2,4})\s*-\s*(?P<num>\d+)(?:\s+du\s+\d{1,2}(?:er)?\s+\w+\s+\d{4})?)")
_CODES = {
    "code des obligations et des contrats": "Code des obligations et des contrats",
    "code de commerce": "Code de commerce",
    "code des sociétés commerciales": "Code des sociétés commerciales",
}
_CODE = r"(?P<code>" + "|".join(re.escape(c) for c in _CODES) + r")"

# Longest forms first: "article 5 de la loi n° …", "articles 2 et 3 du code de commerce", "article 43 de la présente
# loi", a law on its own, then a bare "article 12" (same text).
_PATTERNS = [
    re.compile(rf"\barticles?\s+{_ARTICLES}\s+(?:de\s+la|du|de\s+l['’])\s*{_LAW}", re.IGNORECASE),
    re.compile(rf"\barticles?\s+{_ARTICLES}\s+(?:du|de\s+la)\s+{_CODE}", re.IGNORECASE),
    re.compile(rf"\barticles?\s+{_ARTICLES}(?P<self>\s+(?:de\s+la\s+présente\s+(?:loi|décret|code)|du\s+présent\s+(?:code|décret)))?",
               re.IGNORECASE),
    re.compile(_LAW, re.IGNORECASE),
    re.compile(_CODE, re.IGNORECASE),
]


def law_keys(text: str) -> list[str]:
    """Every key a text answers to: a known code's name and/or its law number ("Code des sociétés commerciales (Loi
    n° 2000-93 …)" is cited both ways)."""
    keys = []
    low = text.lower()
    code = next((name for key, name in _CODES.items() if low.startswith(key)), None)
    if code:
        keys.append(code)
    m = re.search(r"n°\s*(\d{2,4})\s*-\s*(\d+)", text)
    if m:
        keys.append(f"{m.group(1)}-{m.group(2)}")
    return keys


def law_key(text: str) -> str | None:
    keys = law_keys(text)
    return keys[0] if keys else None


@dataclass
class Index:
    """The corpus's texts by law key; a code split into parts (COC art. 2-70, 240-338…) maps to all its parts."""
    texts: dict[str, list[LegalText]] = field(default_factory=lambda: defaultdict(list))

    @classmethod
    def build(cls, texts: list[LegalText]) -> "Index":
        index = cls()
        for text in texts:
            for key in law_keys(text.reference or text.title):
                index.texts[key].append(text)
        return index

    def article(self, key: str, number: str) -> Article | None:
        for text in self.texts.get(key, []):
            for article in text.articles:
                if article.number == number:
                    return article
        return None


@dataclass(frozen=True)
class Reference:
    start: int
    end: int
    law: str | None          # law key, None for the same text
    numbers: tuple[str, ...]  # article numbers, empty for a whole law


def _numbers(raw: str | None) -> tuple[str, ...]:
    if not raw:
        return ()
    parts = re.split(r"\s*(?:,|\bet\b|\bà\b)\s*", raw)
    return tuple(normalize_number(p) for p in parts if p.strip())


_NOTE = re.compile(r"\([^()]*\b(?:modifiée?s?|ajoutée?s?|abrogée?s?|complétée?s?|remplacée?s?)\s+par\b[^()]*\)|^\s*\(\*\).*$",
                   re.IGNORECASE | re.MULTILINE)


def in_amendment_note(text: str, position: int) -> bool:
    """True inside an edition's history note — "(Alinéa 3 modifié par la loi n° …)" or a "(*) …" footnote — rather
    than in a rule of the article."""
    return any(m.start() <= position < m.end() for m in _NOTE.finditer(text))


def find(text: str) -> list[Reference]:
    found: list[Reference] = []
    taken: list[tuple[int, int]] = []
    for pattern in _PATTERNS:
        for m in pattern.finditer(text):
            if any(m.start() < e and s < m.end() for s, e in taken):
                continue
            groups = m.groupdict()
            if groups.get("law"):
                law = f"{groups['year']}-{groups['num']}"
            elif groups.get("code"):
                law = _CODES[groups["code"].lower()]
            else:
                law = None
            found.append(Reference(m.start(), m.end(), law, _numbers(groups.get("arts"))))
            taken.append((m.start(), m.end()))
    return sorted(found, key=lambda r: r.start)


def _terms(segment: str, article: Article, defined: list, pattern) -> Markup:
    """Plain text with each term the law defines underlined; the definition shows on hover. Not in the defining article."""
    if pattern is None:
        return escape(segment)
    out, last = [], 0
    for m in pattern.finditer(segment):
        d = definitions.lookup(defined, m.group(0))
        if d is None or d.article_id == article.id:
            continue
        out.append(escape(segment[last:m.start()]))
        out.append(Markup('<span class="term" tabindex="0" data-def="{} (art. {}) : {}">{}</span>').format(
            d.term, d.article_number, d.definition, m.group(0)))
        last = m.end()
    out.append(escape(segment[last:]))
    return Markup("").join(out)


def linkify(article: Article, own: LegalText, index: Index, missing_title: str = "", defined: list | None = None) -> Markup:
    """The article's text as HTML, with each reference linked to its target; a law the corpus lacks is marked.
    With the law's definitions, the terms it defines are underlined too."""
    text, out, last = article.text, [], 0
    defined = defined or []
    pattern = definitions.matcher(defined)
    own_keys = law_keys(own.reference or own.title)
    for ref in find(text):
        out.append(_terms(text[last:ref.start], article, defined, pattern))
        label = escape(text[ref.start:ref.end])
        if ref.law is None or ref.law in own_keys:
            targets = [a for n in ref.numbers if (a := next((x for x in own.articles if x.number == n), None))]
        else:
            targets = [a for n in ref.numbers if (a := index.article(ref.law, n))]
        if targets:
            first = targets[0]
            out.append(Markup('<a class="ref" href="/texts/{}#art-{}" title="{}">{}</a>').format(
                first.legal_text_id, first.id, f"art. {', '.join(t.number for t in targets)}", label))
        elif ref.law and index.texts.get(ref.law):
            out.append(Markup('<a class="ref" href="/texts/{}">{}</a>').format(index.texts[ref.law][0].id, label))
        elif ref.law and ref.law not in own_keys:
            out.append(Markup('<span class="ref ref-missing" title="{}">{}</span>').format(missing_title, label))
        else:
            out.append(label)
        last = ref.end
    out.append(_terms(text[last:], article, defined, pattern))
    return Markup("").join(out)


@dataclass
class Missing:
    key: str
    label: str
    count: int
    cited_by: list[str]


def missing_laws(texts: list[LegalText], index: Index) -> list[Missing]:
    """Laws cited in the corpus but not held by it, most cited first, with the texts that cite them."""
    seen: dict[str, Missing] = {}
    for text in texts:
        own_keys = law_keys(text.reference or text.title)
        for article in text.articles:
            for ref in find(article.text):
                if (not ref.law or ref.law in own_keys or index.texts.get(ref.law)
                        or in_amendment_note(article.text, ref.start)):
                    continue
                label = re.sub(r"\s+", " ", article.text[ref.start:ref.end])
                label = re.sub(r"^articles?\s+.*?\s+(?=(?:de\s+la|du|de\s+l)\b)", "", label, flags=re.IGNORECASE)
                label = re.sub(r"^(?:de\s+la|du|de\s+l['’])\s*", "", label, flags=re.IGNORECASE)
                label = re.sub(r"(\d{2,4})\s*-\s*(\d+)", r"\1-\2", label)
                label = label[:1].upper() + label[1:]
                entry = seen.setdefault(ref.law, Missing(ref.law, label, 0, []))
                entry.count += 1
                if len(label) > len(entry.label):
                    entry.label = label
                citing = f"{text.reference or text.title}, art. {article.number}"
                if citing not in entry.cited_by:
                    entry.cited_by.append(citing)
    return sorted(seen.values(), key=lambda m: -m.count)
