"""Suggestions from Harvey's optional language model, on law texts only (never a contract). Each one is checked before
it is shown and nothing is saved until a person accepts it: the existing forms do the saving, with their own rules
(an added article or phrase still withdraws the rule's confirmation)."""

from dataclasses import dataclass

from app.models import Category, LegalText
from app.services.llm import LlmProvider
from app.services.splitter import normalize_number
from app.services.textnorm import normalize_for_match

MAX_LAW_CHARS = 50_000


# ------------------------------------------------------------------ 1. metadata for an imported law

METADATA_SYSTEM = f"""You describe a Tunisian legal text for a law library. Answer with JSON only:
{{"title": "<short title, e.g. Loi relative aux banques et aux établissements financiers>",
"reference": "<the official reference exactly as printed, e.g. Loi n° 2016-48 du 11 juillet 2016>",
"category": one of {[c.value for c in Category]},
"keywords": "<5 to 10 comma-separated keywords, in the text's language>",
"summary": "<two or three sentences on what the text governs, in the text's language>"}}.
Never invent a reference: copy it from the text, or leave it empty."""


@dataclass
class Metadata:
    title: str
    reference: str
    category: Category | None
    keywords: str
    summary: str


def suggest_metadata(llm: LlmProvider, raw_text: str) -> Metadata:
    answer = llm.complete_json(METADATA_SYSTEM, raw_text[:MAX_LAW_CHARS])
    reference = " ".join(str(answer.get("reference") or "").split())
    # A reference the text does not contain is a guess, and a wrong reference breaks the playbook's links.
    if reference and normalize_for_match(reference) not in normalize_for_match(raw_text):
        reference = ""
    category = str(answer.get("category") or "")
    return Metadata(
        title=" ".join(str(answer.get("title") or "").split())[:300],
        reference=reference[:300],
        category=Category(category) if category in Category.__members__ else None,
        keywords=" ".join(str(answer.get("keywords") or "").split())[:500],
        summary=str(answer.get("summary") or "").strip()[:1500],
    )


# ------------------------------------------------------------------ 2. articles that back a playbook rule

ARTICLES_SYSTEM = """You help a lawyer link a contract risk to the articles of law that ground it. You get the risk and
the articles of one Tunisian legal text. Answer with JSON only: {"articles": [{"number": "<article number as given>",
"why": "<one sentence, in French, on why this article applies to the risk>"}]}. Pick at most 5 articles, most relevant
first, and only articles that genuinely bear on the risk. If none does, answer {"articles": []}."""


@dataclass
class ArticleSuggestion:
    number: str
    why: str
    text: str


def suggest_articles(llm: LlmProvider, risk: str, text: LegalText, already: set[str]) -> list[ArticleSuggestion]:
    body, used = [], 0
    for article in text.articles:
        line = f"Article {article.number}: {' '.join(article.text.split())}"
        if used + len(line) > MAX_LAW_CHARS:
            break
        body.append(line)
        used += len(line)
    answer = llm.complete_json(ARTICLES_SYSTEM, f"Risk: {risk}\nLegal text: {text.reference or text.title}\n\n" + "\n".join(body))

    by_number = {a.number: a for a in text.articles}
    suggestions, seen = [], set()
    for item in answer.get("articles") or []:
        if not isinstance(item, dict):
            continue
        number = normalize_number(str(item.get("number", "")))
        # Only articles that exist in the text and are not already in the rule.
        if number not in by_number or number in already or number in seen:
            continue
        seen.add(number)
        suggestions.append(ArticleSuggestion(number, " ".join(str(item.get("why") or "").split())[:300],
                                             by_number[number].text))
    return suggestions[:5]


# ------------------------------------------------------------------ 3. other wordings of a risk

PHRASES_SYSTEM = """You help a lawyer list the wordings French-language contracts use for one contract risk, so that
clauses using them can be flagged. Answer with JSON only: {"phrases": ["<phrase>", ...]}. Give up to 8 short phrases
(3 to 10 words) exactly as they would appear inside a contract clause, in French, lower case, without quotation marks.
Prefer distinctive wordings over generic words. Do not repeat the wordings already known."""


def suggest_phrases(llm: LlmProvider, risk: str, known: list[str]) -> list[str]:
    known_list = "\n".join(f"- {p}" for p in known) or "- (none)"
    answer = llm.complete_json(PHRASES_SYSTEM, f"Risk: {risk}\nWordings already known:\n{known_list}")
    taken = {normalize_for_match(p).casefold() for p in known}
    phrases = []
    for raw in answer.get("phrases") or []:
        phrase = " ".join(str(raw).strip().strip("«»\"“”").split())
        key = normalize_for_match(phrase).casefold()
        if not 4 <= len(phrase) <= 200 or key in taken:
            continue
        taken.add(key)
        phrases.append(phrase)
    return phrases[:8]


# ------------------------------------------------------------------ 4. example clauses for meaning-based matching

EXAMPLES_SYSTEM = """You help a lawyer build a bank of example contract clauses for one contract risk, used to recognise
clauses that mean the same thing in other words. Answer with JSON only:
{"examples": ["<clause>", ...], "counter_examples": ["<clause>", ...]}.
examples: up to 8 short French contract clauses (one or two sentences each) that carry this risk, varied in wording,
sentence structure and point of view (either party), as a Tunisian commercial contract would phrase them.
counter_examples: up to 4 French clauses on the same subject that are close in wording but do NOT carry the risk
(for instance a capped or balanced version). Invent no party names: use "le Client", "le Prestataire", "la Banque"."""


@dataclass
class ExampleDrafts:
    examples: list[str]
    counter_examples: list[str]


def draft_examples(llm: LlmProvider, risk: str, phrases: list[str], known: list[str]) -> ExampleDrafts:
    hints = "\n".join(f"- {p}" for p in phrases) or "- (none)"
    existing = "\n".join(f"- {k}" for k in known) or "- (none)"
    answer = llm.complete_json(EXAMPLES_SYSTEM, f"Risk: {risk}\nWordings known to signal it:\n{hints}\n"
                                                f"Example clauses already in the bank (do not repeat):\n{existing}")
    taken = {normalize_for_match(k).casefold() for k in known}

    def keep(items, limit):
        kept = []
        for raw in items or []:
            clause = " ".join(str(raw).strip().strip("«»\"“”").split())
            key = normalize_for_match(clause).casefold()
            if not 30 <= len(clause) <= 600 or key in taken:
                continue
            taken.add(key)
            kept.append(clause)
        return kept[:limit]

    return ExampleDrafts(keep(answer.get("examples"), 8), keep(answer.get("counter_examples"), 4))
