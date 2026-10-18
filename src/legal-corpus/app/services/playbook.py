"""The clause playbook: which verified articles back each risk the rules scan detects. Lives in playbook.json so it
can be reviewed and versioned; a rule is served only once someone has confirmed it, and any edit to a rule clears
that confirmation — the same principle as verifying a law."""

import json
import os
import re
import tempfile
import threading
from datetime import datetime, timezone
from pathlib import Path

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.config import BASE_DIR
from app.models import Article, LegalText
from app.services.citations import citation_label
from app.services.corpus import RuleError
from app.services.splitter import normalize_number

PLAYBOOK_PATH = BASE_DIR / "playbook.json"

# The risks LegalDesk's rule engine detects. A rule with another key is never triggered, so the editor warns about it.
DETECTED_KEYS = {
    "liability": ("Unlimited liability", "Responsabilité illimitée"),
    "indemnification": ("Indemnity", "Garantie / indemnisation"),
    "penalty": ("Penalty clause", "Clause pénale"),
    "termination": ("Termination (missing or one-sided)", "Résiliation (absente ou unilatérale)"),
    "auto_renewal": ("Automatic renewal", "Reconduction tacite"),
    "exclusivity": ("Exclusivity / non-compete", "Exclusivité / non-concurrence"),
    "personal_data": ("Personal data", "Données personnelles"),
    "contradiction": ("Contradictions, wrong references, party names", "Contradictions, renvois erronés, désignation des parties"),
    "payment": ("Price and payment (via regulations)", "Prix et paiement (via les réglementations)"),
    "unilateral_change": ("Unilateral change of the contract", "Modification unilatérale du contrat"),
    "force_majeure": ("Force majeure (missing or extended)", "Force majeure (absente ou élargie)"),
    "electronic_signature": ("Electronic signature", "Signature électronique"),
    "commercial_agency": ("Commercial agency", "Agence commerciale"),
    "consumer_online": ("Online sale to consumers", "Vente en ligne au consommateur"),
    "deposit": ("Deposit (arrhes / acompte)", "Arrhes / acompte"),
    "breach_notice": ("Termination for breach without formal notice", "Résiliation pour manquement sans mise en demeure"),
    "suspension": ("Suspension of performance", "Suspension de l'exécution"),
    "works_guarantee": ("Works guarantee", "Garantie de l'ouvrage"),
    "transport": ("Transport contract", "Contrat de transport"),
    "electronic_payment": ("Electronic payment", "Paiement électronique"),
    "bank_outsourcing": ("Bank outsourcing (audit and central-bank supervision)", "Externalisation bancaire (audit et supervision BCT)"),
    "bank_secrecy": ("Banking secrecy", "Secret bancaire"),
    "bank_account": ("Account agreement (fees, mediator)", "Convention de compte (commissions, médiateur)"),
}

_lock = threading.Lock()


def load(path: Path = PLAYBOOK_PATH) -> dict:
    data = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
    data.setdefault("rules", [])
    data.setdefault("regulations", {})
    return data


def save(data: dict, path: Path = PLAYBOOK_PATH) -> None:
    """Write-then-rename, so a crash mid-save never leaves a half-written playbook for LegalDesk to read."""
    fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=".playbook-", suffix=".json")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
    os.replace(tmp, path)


def _edit(path: Path, change):
    with _lock:
        data = load(path)
        result = change(data)
        save(data, path)
        return result


def _rule(data: dict, key: str) -> dict:
    rule = next((r for r in data["rules"] if r["key"] == key), None)
    if rule is None:
        raise RuleError("unknown_rule", f"No playbook rule '{key}'.")
    return rule


def _unconfirm(rule: dict) -> None:
    rule["confirmed_by"] = None
    rule["confirmed_at"] = None


def find_article(db: Session, reference: str, number: str) -> Article | None:
    return db.scalar(
        select(Article).join(LegalText)
        .where(LegalText.reference == reference, Article.number == normalize_number(number))
    )


def add_rule(key: str, label: str, path: Path = PLAYBOOK_PATH) -> None:
    key = key.strip().lower().replace(" ", "_")
    if not key:
        raise RuleError("invalid", "A rule needs a key.")

    def change(data):
        if any(r["key"] == key for r in data["rules"]):
            raise RuleError("duplicate_rule", f"Rule '{key}' already exists.")
        data["rules"].append({"key": key, "label": label.strip() or key, "articles": [], "confirmed_by": None, "confirmed_at": None})

    _edit(path, change)


def update_label(key: str, label: str, path: Path = PLAYBOOK_PATH) -> None:
    def change(data):
        rule = _rule(data, key)
        rule["label"] = label.strip() or key

    _edit(path, change)


def delete_rule(key: str, path: Path = PLAYBOOK_PATH) -> None:
    def change(data):
        _rule(data, key)
        data["rules"] = [r for r in data["rules"] if r["key"] != key]
        data["regulations"] = {k: v for k, v in data["regulations"].items() if v != key}

    _edit(path, change)


def add_article(db: Session, key: str, text_id: str, number: str, why: str, path: Path = PLAYBOOK_PATH) -> None:
    text = db.get(LegalText, text_id)
    if text is None or not text.reference:
        raise RuleError("no_reference", "Choose a legal text that has a reference.")
    article = find_article(db, text.reference, number)
    if article is None:
        raise RuleError("article_not_found", f"{text.reference} has no article {number}.")

    def change(data):
        rule = _rule(data, key)
        if any(a["reference"] == text.reference and normalize_number(a["number"]) == article.number for a in rule["articles"]):
            raise RuleError("duplicate_article", "This article is already in the rule.")
        rule["articles"].append({"reference": text.reference, "number": article.number, "why": why.strip() or None})
        _unconfirm(rule)

    _edit(path, change)


def update_article(key: str, index: int, why: str, path: Path = PLAYBOOK_PATH) -> None:
    def change(data):
        rule = _rule(data, key)
        rule["articles"][index]["why"] = why.strip() or None
        _unconfirm(rule)

    _edit(path, change)


def remove_article(key: str, index: int, path: Path = PLAYBOOK_PATH) -> None:
    def change(data):
        rule = _rule(data, key)
        del rule["articles"][index]
        _unconfirm(rule)

    _edit(path, change)


MIN_PHRASE_CHARS = 4


def add_phrase(key: str, phrase: str, path: Path = PLAYBOOK_PATH) -> None:
    """Wording that should raise this rule even where LegalDesk's built-in detection misses it. Like any edit to the
    rule, it withdraws the confirmation: a phrase is only used once a person has confirmed the rule again."""
    phrase = " ".join(phrase.split())
    if len(phrase) < MIN_PHRASE_CHARS:
        raise RuleError("phrase_too_short", "A phrase needs at least four characters.")

    def change(data):
        rule = _rule(data, key)
        phrases = rule.setdefault("phrases", [])
        if any(p.casefold() == phrase.casefold() for p in phrases):
            raise RuleError("duplicate_phrase", "This phrase is already in the rule.")
        phrases.append(phrase)
        _unconfirm(rule)

    _edit(path, change)


def remove_phrase(key: str, index: int, path: Path = PLAYBOOK_PATH) -> None:
    def change(data):
        rule = _rule(data, key)
        del rule.setdefault("phrases", [])[index]
        _unconfirm(rule)

    _edit(path, change)


EXAMPLE_KINDS = ("examples", "counter_examples")
MIN_EXAMPLE_CHARS, MAX_EXAMPLE_CHARS = 30, 600


def add_example(key: str, kind: str, text: str, source: str = "lawyer", path: Path = PLAYBOOK_PATH) -> None:
    """A clause that sounds like the risk (example) or that is close but safe (counter-example). Used by meaning-based
    matching; like any edit it withdraws the rule's confirmation."""
    if kind not in EXAMPLE_KINDS:
        raise RuleError("invalid", "Unknown example kind.")
    text = " ".join(text.split())
    if not MIN_EXAMPLE_CHARS <= len(text) <= MAX_EXAMPLE_CHARS:
        raise RuleError("example_length", "An example clause needs 30 to 600 characters.")

    def change(data):
        rule = _rule(data, key)
        items = rule.setdefault(kind, [])
        if any(e["text"].casefold() == text.casefold() for e in rule.get("examples", []) + rule.get("counter_examples", [])):
            raise RuleError("duplicate_example", "This clause is already in the rule.")
        items.append({"text": text, "source": source})
        _unconfirm(rule)

    _edit(path, change)


def remove_example(key: str, kind: str, index: int, path: Path = PLAYBOOK_PATH) -> None:
    if kind not in EXAMPLE_KINDS:
        raise RuleError("invalid", "Unknown example kind.")

    def change(data):
        rule = _rule(data, key)
        del rule.setdefault(kind, [])[index]
        _unconfirm(rule)

    _edit(path, change)


def move_article(key: str, index: int, offset: int, path: Path = PLAYBOOK_PATH) -> None:
    def change(data):
        articles = _rule(data, key)["articles"]
        target = index + offset
        if 0 <= target < len(articles):
            articles[index], articles[target] = articles[target], articles[index]
            _unconfirm(_rule(data, key))

    _edit(path, change)


def confirm(db: Session, key: str, confirmed_by: str, path: Path = PLAYBOOK_PATH) -> None:
    rule = _rule(load(path), key)
    if not rule["articles"]:
        raise RuleError("no_articles", "A rule without articles has nothing to confirm.")
    for ref in rule["articles"]:
        article = find_article(db, ref["reference"], ref["number"])
        if article is None or not article.legal_text.is_verified:
            raise RuleError("unverified_article",
                            f"{ref['reference']}, art. {ref['number']} is not verified law: verify its text first.")

    def change(data):
        r = _rule(data, key)
        r["confirmed_by"] = confirmed_by.strip()
        r["confirmed_at"] = datetime.now(timezone.utc).isoformat(timespec="seconds")

    _edit(path, change)


def unconfirm(key: str, path: Path = PLAYBOOK_PATH) -> None:
    _edit(path, lambda data: _unconfirm(_rule(data, key)))


def set_mapping(fragment: str, key: str, path: Path = PLAYBOOK_PATH) -> None:
    fragment = fragment.strip()
    if not fragment:
        raise RuleError("invalid", "The reference fragment is empty.")

    def change(data):
        _rule(data, key)
        data["regulations"][fragment] = key

    _edit(path, change)


def delete_mapping(fragment: str, path: Path = PLAYBOOK_PATH) -> None:
    def change(data):
        data["regulations"].pop(fragment, None)

    _edit(path, change)


_SHORT_CODES = {
    "Code des obligations et des contrats": "COC",
    "Code de commerce": "C. com.",
    "Code des sociétés commerciales": "CSC",
}


def short_citation(reference: str, number: str) -> str:
    """"Loi organique n° 2004-63 du …" → "Loi 2004-63, art. 27"; codes by their usual abbreviation."""
    for name, code in _SHORT_CODES.items():
        if reference.startswith(name):
            return f"{code}, art. {number}"
    law = re.search(r"n°\s*([\d-]+)", reference)
    if law:
        return f"Loi {law.group(1)}, art. {number}"
    return f"{reference[:24]}…, art. {number}" if len(reference) > 24 else f"{reference}, art. {number}"


def describe(db: Session, path: Path = PLAYBOOK_PATH) -> dict:
    """Every rule, confirmed or not, with each article resolved for display."""
    data = load(path)
    rules = []
    for rule in data["rules"]:
        articles = []
        for ref in rule["articles"]:
            article = find_article(db, ref["reference"], ref["number"])
            articles.append({
                **ref,
                "found": article is not None,
                "verified": bool(article and article.legal_text.is_verified),
                "citation": citation_label(article.legal_text, article.number) if article else f"{ref['reference']}, art. {ref['number']}",
                "short": short_citation(ref["reference"], ref["number"]),
                "text": article.text if article else None,
                "legal_text_id": article.legal_text_id if article else None,
            })
        rules.append({**rule, "articles": articles, "phrases": rule.get("phrases", []),
                      "examples": rule.get("examples", []), "counter_examples": rule.get("counter_examples", []),
                      "detected": rule["key"] in DETECTED_KEYS,
                      "ready": bool(articles) and all(a["verified"] for a in articles)})
    return {"rules": rules, "regulations": data["regulations"]}


def resolve(db: Session, path: Path = PLAYBOOK_PATH, include_unconfirmed: bool = False) -> dict:
    playbook = load(path)
    rules = {}
    unresolved = []

    for rule in playbook.get("rules", []):
        if not rule.get("confirmed_by") and not include_unconfirmed:
            continue
        articles = []
        for ref in rule.get("articles", []):
            article = find_article(db, ref["reference"], ref["number"])
            # Only verified law is served: an article whose text lost its sign-off drops out until re-verified.
            if article is None or not article.legal_text.is_verified:
                unresolved.append(f"{rule['key']}: {ref['reference']}, art. {ref['number']}")
                continue
            from app.services import explanations

            approved = {lang: explanations.approved(db, article, lang) for lang in explanations.LANGUAGES}
            articles.append({
                "article_id": article.id,
                "citation": citation_label(article.legal_text, article.number),
                "text": article.text,
                "why": ref.get("why"),
                # Lawyer-approved plain-language explanations for this wording, by language.
                "explanations": {lang: e.summary for lang, e in approved.items() if e is not None},
            })
        rules[rule["key"]] = {"label": rule.get("label"), "confirmed_by": rule.get("confirmed_by"), "articles": articles,
                              "phrases": rule.get("phrases", []),
                              "examples": [e["text"] for e in rule.get("examples", [])],
                              "counter_examples": [e["text"] for e in rule.get("counter_examples", [])]}

    return {"rules": rules, "regulations": playbook.get("regulations", {}), "unresolved": unresolved}


def law_name(short: str) -> str:
    """"COC, art. 278" → "COC"; "Loi 2016-48, art. 81" → "Loi 2016-48"."""
    return short.rsplit(", art.", 1)[0]


def link_map(rules: list[dict]) -> dict:
    """Coordinates for the playbook's link map: risks on the left, laws on the right, one curve per risk-law pair.
    Drawn as plain SVG by the template, so the page needs no script library and works offline."""
    row, top, node_h = 40, 16, 30
    left_w, right_x, width = 270, 650, 920

    laws: dict[str, dict] = {}
    for rule in rules:
        for article in rule["articles"]:
            law = laws.setdefault(law_name(article["short"]), {"name": law_name(article["short"]), "articles": set(), "rules": set()})
            law["articles"].add(article["number"])
            law["rules"].add(rule["key"])
    ordered_laws = sorted(laws.values(), key=lambda l: (-len(l["rules"]), l["name"]))

    height = top * 2 + max(len(rules), 1) * row
    law_step = (height - 2 * top) / max(len(ordered_laws), 1)
    law_nodes = []
    for i, law in enumerate(ordered_laws):
        y = top + law_step * i + (law_step - node_h) / 2
        law_nodes.append({"name": law["name"], "y": round(y, 1), "articles": len(law["articles"]),
                          "rules": len(law["rules"]), "keys": " ".join(sorted(law["rules"]))})
    law_y = {n["name"]: n["y"] + node_h / 2 for n in law_nodes}

    rule_nodes, links = [], []
    for i, rule in enumerate(rules):
        y = top + i * row
        rule_nodes.append({"key": rule["key"], "label": rule["label"], "y": y, "confirmed": bool(rule.get("confirmed_by")),
                           "ready": rule.get("ready", False), "articles": len(rule["articles"])})
        by_law: dict[str, list[str]] = {}
        for article in rule["articles"]:
            by_law.setdefault(law_name(article["short"]), []).append(article["number"])
        y1 = y + node_h / 2
        for law, numbers in by_law.items():
            y2 = law_y[law]
            mid = (left_w + right_x) / 2
            links.append({"rule": rule["key"], "law": law, "confirmed": bool(rule.get("confirmed_by")),
                          "articles": ", ".join(numbers), "weight": min(1 + len(numbers), 4),
                          "d": f"M{left_w},{y1:.1f} C{mid:.0f},{y1:.1f} {mid:.0f},{y2:.1f} {right_x},{y2:.1f}"})

    return {"width": width, "height": height, "node_h": node_h, "left_w": left_w, "right_x": right_x,
            "rules": rule_nodes, "laws": law_nodes, "links": links}
