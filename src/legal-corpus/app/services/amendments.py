"""Reads an amending law ("L'article 12 de la loi n° … est modifié comme suit : …") and proposes the changes it makes
to a text in the corpus, so they can be reviewed and applied as article versions.

The model only proposes. Every proposal is checked before anyone sees it: the new wording must appear word for word
in the amending law (so the model cannot invent or rephrase law), and a replaced or repealed article must exist in
the target text. Proposals that fail are dropped and counted, never shown as if they were sound."""

from dataclasses import dataclass, field

from app.models import LegalText
from app.services.llm import LlmProvider, LlmUnavailable
from app.services.splitter import normalize_number
from app.services.textnorm import normalize_for_match

MAX_INPUT_CHARS = 60_000
ACTIONS = ("replace", "add", "repeal")

SYSTEM = """You read Tunisian amending laws (French or Arabic) and list the changes they make to ONE target law.
Answer with JSON only: {"changes": [{"article": "<article number in the target law, e.g. 12 or 12 bis>",
"action": "replace" | "add" | "repeal", "new_text": "<the new wording of that article, copied EXACTLY, character
for character, from the amending law; empty for repeal>"}]}.
Rules: list only changes to the target law named by the user, never to other laws. Copy new_text exactly as printed,
without the introductory sentence ("est modifié comme suit :") and without quotation marks around it. Do not
summarise, translate or correct anything. If the amending law changes only part of an article (a paragraph or a
few words), use "replace" only when the full new wording of the article is printed; otherwise skip that change.
If there is no change to the target law, answer {"changes": []}."""


@dataclass
class Proposal:
    number: str
    action: str
    new_text: str
    current_text: str | None


@dataclass
class AmendmentResult:
    proposals: list[Proposal] = field(default_factory=list)
    # Changes the model returned that failed a check, with the reason, so the reviewer knows something was dropped.
    rejected: list[str] = field(default_factory=list)


def _clean(text: str) -> str:
    return " ".join(text.strip().strip("«»\"“”").split())


def propose(llm: LlmProvider, target: LegalText, amending_text: str) -> AmendmentResult:
    if not amending_text.strip():
        raise LlmUnavailable("The amending law is empty.")
    user = (f"Target law: {target.reference or target.title}\n"
            f"Articles currently in the target law: {', '.join(a.number for a in target.articles)}\n\n"
            f"Amending law:\n{amending_text[:MAX_INPUT_CHARS]}")
    answer = llm.complete_json(SYSTEM, user)

    haystack = normalize_for_match(amending_text)
    by_number = {a.number: a for a in target.articles}
    result = AmendmentResult()
    seen: set[str] = set()
    for change in answer.get("changes") or []:
        if not isinstance(change, dict):
            continue
        number = normalize_number(str(change.get("article", "")).strip())
        action = str(change.get("action", "")).lower()
        new_text = _clean(str(change.get("new_text") or ""))
        label = f"art. {number or '?'} ({action or '?'})"

        if action not in ACTIONS or not number:
            result.rejected.append(f"{label}: unreadable change")
            continue
        if number in seen:
            result.rejected.append(f"{label}: listed twice")
            continue
        current = by_number.get(number)
        if action in ("replace", "repeal") and current is None:
            result.rejected.append(f"{label}: no such article in the target law")
            continue
        if action == "add" and current is not None:
            result.rejected.append(f"{label}: the article already exists")
            continue
        if action != "repeal":
            if len(new_text) < 15 or normalize_for_match(new_text) not in haystack:
                result.rejected.append(f"{label}: the new wording is not word for word in the amending law")
                continue
        seen.add(number)
        result.proposals.append(Proposal(number, action, new_text, current.text if current else None))
    return result
