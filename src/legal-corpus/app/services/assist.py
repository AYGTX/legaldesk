"""Suggestions from Harvey's optional language model, on law texts only (never a contract). Each one is checked before
it is shown and nothing is saved until a person accepts it: the existing forms do the saving, with their own rules
(an added article or phrase still withdraws the rule's confirmation)."""

from dataclasses import dataclass

from app.models import Category, LegalText
from app.services.llm import LlmProvider, LlmUnavailable
from app.services.splitter import normalize_number
from app.services.textnorm import normalize_for_match

MAX_LAW_CHARS = 50_000
# Title, reference and summary are found at the start of a law; sending the whole code would exceed Groq's limits.
METADATA_CHARS = 12_000


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
    answer = llm.complete_json(METADATA_SYSTEM, raw_text[:METADATA_CHARS])
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

# Who Harvey works for and what a link between an article and a risk is for; shared by the suggestion prompts.
CONTEXT = """Context: Harvey is the verified Tunisian law reference behind LegalDesk, a contract review tool used by
in-house lawyers (for example at a Tunisian bank) to review commercial contracts with suppliers and customers.
LegalDesk detects risky clauses in a contract (each risk is described below); for every risk found, the reviewer is
shown the articles of law linked to that risk, to explain why the clause is risky or what the law requires instead.
So an article grounds a risk only if a lawyer reviewing such a clause would cite that article to the business: it
must govern the same legal question (same obligation, same type of clause, same party protection). An article on a
neighbouring topic, or one that only shares a word with the risk (\"engagement\", \"responsabilité\",
\"partie\"), does not ground it. Articles on capacity to contract, persons, family, succession or procedure rarely
ground a contract-clause risk."""


ARTICLES_SYSTEM = CONTEXT + """

You help a lawyer link a contract risk to the articles of law that ground it. You get the risk and
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


# ------------------------------------------------------------------ 2b. risks an article might ground

RULES_SYSTEM = CONTEXT + """

You help a lawyer decide which contract risks, from a fixed list, an article of law could ground.
You get the article's text and the list of risks, each with its key, its name and a description of what the risk is
about. Read the descriptions: a risk fits only when the article's subject is the subject the description names, not
when a word merely sounds related (an article on capacity to contract does not ground a risk about errors inside a
contract).

Work in two steps. First write the article's subject: the legal question it settles, in a few French words (for
example "capacité du mineur commerçant", "réduction de la clause pénale par le juge"). Then, for each risk you
consider, ask: does a clause carrying this risk raise exactly that legal question? Rate the fit "direct" only if it
does; "indirect" if the article is merely on a neighbouring topic or only shares words with the risk.

Answer with JSON only: {"subject": "<the article's subject>", "risks": [{"key": "<risk key>", "fit": "direct" or
"indirect", "why": "<one sentence, in French, naming what the article requires or forbids that bears on this
risk>"}]}. At most 3 risks, most relevant first. If none fits, answer {"subject": "...", "risks": []}: an empty
answer is better than a loose one. Use only keys from the given list, copied exactly."""


VERIFY_SYSTEM = CONTEXT + """

You check proposed links between one article of law and contract risks, each risk on its own. For each risk, imagine a
contract clause that carries the risk as described. Would a Tunisian lawyer, reviewing that clause, cite this article as the legal rule that makes
the clause risky or that the clause must respect? Answer "yes" only if the article settles that very question for
contracts between the parties; answer "no" if it concerns another situation (for example who may contract, a minor,
procedure) even when words overlap. Answer with JSON only: {"cite": {"<risk key>": "yes" or "no", ...}}, one entry
per risk given."""


def confirmed(llm: LlmProvider, reference: str, number: str, text: str, risks: list[dict]) -> set[str]:
    """One focused check of all suggested links at once; the keys answered "yes" are kept."""
    if not risks:
        return set()
    listed = chr(10).join(f"- {r['key']} — {r['label']} : {r.get('description') or ''}" for r in risks)
    prompt = f"Article {number} of {reference}:" + chr(10) + text[:MAX_LAW_CHARS] + chr(10) * 2 + "Risks:" + chr(10) + listed
    answer = llm.complete_json(VERIFY_SYSTEM, prompt).get("cite")
    if not isinstance(answer, dict):
        return set()
    # The model sometimes copies the whole line ("penalty — Clause pénale") instead of the key alone.
    return {str(k).split("—")[0].split(":")[0].strip() for k, v in answer.items() if str(v).strip().lower() == "yes"}


@dataclass
class RuleSuggestion:
    key: str
    label: str
    why: str = ""


def suggest_rules(llm: LlmProvider, reference: str, number: str, text: str, rules: list[dict]) -> list[RuleSuggestion]:
    catalog = "\n".join(f"- {r['key']} — {r['label']}" + (f" : {r['description']}" if r.get("description") else "")
                        for r in rules)
    answer = llm.complete_json(RULES_SYSTEM, f"Article {number} of {reference}:\n{text[:MAX_LAW_CHARS]}\n\nRisks:\n{catalog}")
    by_label = {r["key"]: r["label"] for r in rules}
    suggestions, seen = [], set()
    for item in answer.get("risks") or answer.get("keys") or []:
        if isinstance(item, dict) and str(item.get("fit", "direct")).lower() != "direct":
            continue  # a neighbouring topic is not a legal basis
        key = str((item.get("key") if isinstance(item, dict) else item) or "").strip()
        why = " ".join(str(item.get("why") or "").split())[:300] if isinstance(item, dict) else ""
        if key not in by_label or key in seen:
            continue
        seen.add(key)
        suggestions.append(RuleSuggestion(key, by_label[key], why))
    suggestions = suggestions[:3]
    kept = confirmed(llm, reference, number, text, [r for r in rules if r["key"] in {s.key for s in suggestions}])
    return [s for s in suggestions if s.key in kept]


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


# ------------------------------------------------------------------ 5. an article in plain words

EXPLAIN_SYSTEM = """You explain one article of Tunisian law to a non-lawyer (a manager or a bank employee).
Answer with JSON only: {"summary": "<2 to 4 short sentences in plain {language}: who it applies to, what it requires
or allows, and the consequence if stated>", "in_practice": "<one sentence starting with what it means in practice
for a company signing contracts>"}.
Rules: explain only what this article says. Do not add obligations, exceptions, deadlines, amounts or other articles
that are not in the text. Do not give legal advice. If the article only refers to other provisions, say so plainly."""

LANGUAGES = {"fr": "French", "en": "English"}


@dataclass
class Explanation:
    summary: str
    in_practice: str


def explain_article(llm: LlmProvider, reference: str, number: str, text: str, language: str = "fr") -> Explanation:
    system = EXPLAIN_SYSTEM.replace("{language}", LANGUAGES.get(language, "French"))
    answer = llm.complete_json(system, f"{reference}, article {number}:\n{text[:MAX_LAW_CHARS]}")
    summary = " ".join(str(answer.get("summary") or "").split())[:1200]
    in_practice = " ".join(str(answer.get("in_practice") or "").split())[:400]
    if len(summary) < 20:
        raise LlmUnavailable("The language model returned no usable explanation.")
    return Explanation(summary, in_practice)


# ------------------------------------------------------------------ 6. a model clause that removes the risk

MODEL_CLAUSE_SYSTEM = """You draft a model contract clause for a Tunisian commercial contract that avoids one contract
risk while complying with the articles of law given. Answer with JSON only: {"fr": "<the clause in French, ready to
paste into a contract, 1 to 4 sentences, neutral: 'le Client', 'le Prestataire', 'la Banque'>", "en": "<the same
clause in English>"}. Write contract wording only: no title, no article number, no explanation, no legal advice.
Do not cite article numbers inside the clause."""


def draft_model_clause(llm: LlmProvider, risk: str, articles: list[dict]) -> dict:
    law = "\n".join(f"- {a['citation']}: {' '.join((a.get('text') or '').split())[:1500]}" for a in articles) or "- (none)"
    answer = llm.complete_json(MODEL_CLAUSE_SYSTEM, f"Risk to avoid: {risk}\nArticles of law:\n{law}")
    fr = " ".join(str(answer.get("fr") or "").split())[:1500]
    en = " ".join(str(answer.get("en") or "").split())[:1500]
    if len(fr) < 30:
        raise LlmUnavailable("The language model returned no usable clause.")
    return {"fr": fr, "en": en}
