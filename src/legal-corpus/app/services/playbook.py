"""The risk grid (« Grille des risques »): which verified articles back each risk the rules scan detects.

Two layers (rework spec R-M11-3). playbook_rules is the working copy people edit; any edit clears its approval and
records who made it. Approving a rule, by a lawyer's account, freezes it as a numbered version in rule_versions.
Reviews and LegalDesk use each rule's latest approved version only, so an edit never changes a review until a lawyer
approves it, and every finding names the exact version it came from."""

import json
import re
import threading
from datetime import datetime, timezone
from pathlib import Path

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.models import Article, LegalText, PlaybookRule, PlaybookRuleArticle, RuleVersion, Severity
from app.services.citations import citation_label
from app.services.corpus import RuleError
from app.services.splitter import normalize_number

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
    "payment": ("Price and payment", "Prix et paiement"),
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
    "bank_related_party": ("Bank related-party agreement", "Convention réglementée (banque)"),
    "bank_supervisor_access": ("Confidentiality without a central-bank exception", "Confidentialité sans exception pour la BCT"),
    "bank_customer_complaints": ("Customer complaint procedure", "Procédure de réclamation client"),
    "bank_aml": ("Anti-money laundering (third-party identification, record keeping)", "Lutte contre le blanchiment (identification par un tiers, conservation)"),
    "resale_price": ("Imposed resale price", "Prix de revente imposé"),
    "market_sharing": ("Market sharing between competitors", "Répartition de marché entre concurrents"),
}

# What each risk LegalDesk detects is about, so a reviewer (and the AI assistant) can tell close risks apart.
DEFAULT_DESCRIPTIONS = {
    "liability": "La responsabilité d'une partie n'a pas de plafond, ou des catégories entières de réclamations échappent au plafond.",
    "indemnification": "Une partie s'engage à garantir ou indemniser l'autre contre des réclamations, souvent sans limite de montant ni de durée.",
    "penalty": "Clause pénale : une somme forfaitaire est due en cas de retard ou d'inexécution, parfois sans plafond.",
    "termination": "Le contrat ne prévoit pas de résiliation, ou une seule partie peut y mettre fin.",
    "auto_renewal": "Le contrat se renouvelle automatiquement (tacite reconduction), parfois sans délai de préavis pour l'arrêter.",
    "exclusivity": "Une partie s'engage à l'exclusivité ou à ne pas faire concurrence, avec un périmètre, un territoire ou une durée excessifs.",
    "personal_data": "Le contrat implique des données personnelles sans prévoir consentement, sécurité, sous-traitance ou transfert à l'étranger.",
    "contradiction": "Erreurs internes au contrat : délais ou montants contradictoires, renvoi à un article inexistant, même partie désignée de plusieurs façons.",
    "payment": "Prix et paiement : intérêts de retard exclus, délais ou modalités de paiement défavorables.",
    "unilateral_change": "Une partie peut modifier seule le contrat, son périmètre ou son prix, sans l'accord de l'autre.",
    "force_majeure": "Pas de clause de force majeure, ou une force majeure élargie à des événements qui ne répondent pas à la définition légale.",
    "electronic_signature": "Le contrat est signé électroniquement sans préciser que la signature est certifiée ni comment l'original est conservé.",
    "commercial_agency": "Relation d'agence commerciale : préavis et indemnités de fin de contrat de l'agent.",
    "consumer_online": "Vente en ligne à un consommateur sans information précontractuelle ni droit de rétractation.",
    "deposit": "Arrhes ou acompte conservés dans tous les cas, quelle que soit la partie en faute.",
    "breach_notice": "Résiliation pour manquement sans mise en demeure préalable ni délai pour remédier.",
    "suspension": "Une partie peut suspendre l'exécution sans que ce soit lié à une inexécution de l'autre.",
    "works_guarantee": "Contrat de travaux ou d'ouvrage qui écarte les garanties légales de l'entrepreneur.",
    "transport": "Contrat de transport qui écarte ou limite la responsabilité légale du transporteur.",
    "electronic_payment": "Paiement électronique : obligations d'information, de sécurité et de conservation de l'émetteur.",
    "bank_outsourcing": "Une banque externalise une activité sans garantir son droit d'audit ni la supervision de la Banque centrale.",
    "bank_secrecy": "Un prestataire accède aux informations d'une banque sans être soumis au secret professionnel.",
    "bank_account": "Convention de compte sans liste des commissions ni information sur le médiateur bancaire.",
    "bank_related_party": "Une banque contracte avec un actionnaire, un dirigeant ou une société du groupe sans autorisation du conseil ni approbation de l'assemblée.",
    "bank_supervisor_access": "Clause de confidentialité d'une banque sans exception pour la Banque centrale et les autorités de contrôle.",
    "bank_customer_complaints": "Convention client sans procédure de réclamation ni délai de réponse.",
    "bank_aml": "Lutte contre le blanchiment : identification des clients confiée à un tiers non soumis aux règles, ou documents conservés moins de dix ans.",
    "resale_price": "Le fournisseur impose au revendeur un prix de revente ou une marge minimum (pratique interdite).",
    "market_sharing": "Des concurrents se répartissent des marchés, des territoires ou des clients, ou fixent leurs prix ensemble.",
}

_lock = threading.Lock()


def load(db: Session) -> dict:
    """The grid as plain data, rules and their articles in display order."""
    rules = []
    for row in db.scalars(select(PlaybookRule).order_by(PlaybookRule.position, PlaybookRule.key)):
        rule = {"key": row.key, "label": row.label, "description": row.description,
                "articles": [{"reference": a.reference, "number": a.number, "why": a.why} for a in row.articles],
                "confirmed_by": row.confirmed_by, "confirmed_at": row.confirmed_at,
                "catalog_seeded": bool(row.catalog_seeded), "checks": dict(row.checks or {}),
                "severity": row.severity, "edited_by_user_id": row.edited_by_user_id,
                "edited_at": row.edited_at.isoformat() if row.edited_at else None}
        for name in ("phrases", "examples", "counter_examples"):
            rule[name] = list(getattr(row, name) or [])
        if row.model_clause:
            rule["model_clause"] = dict(row.model_clause)
        rules.append(rule)
    return {"rules": rules}


def save(db: Session, data: dict) -> None:
    """Writes the whole grid in one transaction, so LegalDesk never reads a half-saved grid."""
    existing = {r.key: r for r in db.scalars(select(PlaybookRule))}
    keep = set()
    for position, rule in enumerate(data["rules"]):
        row = existing.get(rule["key"])
        if row is None:
            row = PlaybookRule(key=rule["key"])
            db.add(row)
        keep.add(rule["key"])
        row.position, row.label = position, rule["label"]
        row.description = rule.get("description")
        row.catalog_seeded = bool(rule.get("catalog_seeded"))
        row.checks = dict(rule.get("checks") or {}) or None
        row.severity = rule.get("severity")
        row.edited_by_user_id = rule.get("edited_by_user_id")
        edited_at = rule.get("edited_at")
        row.edited_at = datetime.fromisoformat(edited_at) if isinstance(edited_at, str) else edited_at
        row.confirmed_by, row.confirmed_at = rule.get("confirmed_by"), rule.get("confirmed_at")
        row.phrases = list(rule.get("phrases", []))
        row.examples = list(rule.get("examples", []))
        row.counter_examples = list(rule.get("counter_examples", []))
        row.model_clause = rule.get("model_clause")
        row.articles = [PlaybookRuleArticle(position=i, reference=a["reference"], number=a["number"], why=a.get("why"))
                        for i, a in enumerate(rule["articles"])]
    for key, row in existing.items():
        if key not in keep:
            db.delete(row)
    db.commit()


# What a rule says, as opposed to who approved or last edited it. A change to any of these needs a new approval.
# The model clause is approved on its own and is not part of a rule's version.
_CONTENT = ("label", "description", "severity", "articles", "phrases", "examples", "counter_examples", "checks")


def _content(rule: dict) -> dict:
    return {k: rule.get(k) for k in _CONTENT}


def _fingerprint(rule: dict) -> str:
    return json.dumps(_content(rule), sort_keys=True, default=str)


def _edit(db: Session, change):
    """Applies a change to the working copy. Every rule whose content changed loses its approval and records who
    changed it, so that person cannot approve it (four-eyes)."""
    with _lock:
        data = load(db)
        before = {r["key"]: _fingerprint(r) for r in data["rules"]}
        result = change(data)
        now = datetime.now(timezone.utc).isoformat(timespec="seconds")
        for rule in data["rules"]:
            if before.get(rule["key"]) != _fingerprint(rule):
                _unconfirm(rule)
                rule["edited_by_user_id"] = db.info.get("editor_id")
                rule["edited_at"] = now
        save(db, data)
        return result


def set_check(db: Session, key: str, check_id: str, enabled: bool, severity: str) -> None:
    """Switches one built-in check of a risk on or off and sets its severity."""
    from app.services import review

    info = review.CHECK_INFO.get(check_id)
    if info is None or info.rule_key != key:
        raise RuleError("invalid", "Unknown check for this risk.")
    severity = severity if severity in _SEVERITIES else info.severity

    def change(data):
        rule = _rule(data, key)
        checks = rule.setdefault("checks", {})
        checks[check_id] = {"enabled": enabled, "severity": severity}

    _edit(db, change)


_SEVERITIES = tuple(s.value for s in Severity)


def set_severity(db: Session, key: str, severity: str) -> None:
    """Severity of this risk's findings by meaning or phrase (R-M11-4). Critical blocks a contract in LegalDesk, so it
    is for clauses a lawyer would never let out (illegal, or missing a mention the law requires). Empty keeps the
    catalogue's default."""
    if severity and severity not in _SEVERITIES:
        raise RuleError("invalid", "Unknown severity.")

    def change(data):
        _rule(data, key)["severity"] = severity or None

    _edit(db, change)


def seed_examples(db: Session) -> int:
    """Copies Harvey's example clauses into each risk of the grid that never received them (source "harvey")."""
    from app.services.risk_catalog import seed_clauses

    def change(data):
        added = 0
        for rule in data["rules"]:
            if rule.get("catalog_seeded"):
                continue
            examples, counters = seed_clauses(rule["key"])
            known = {e["text"].casefold() for e in rule.get("examples", []) + rule.get("counter_examples", [])}
            for kind, texts in (("examples", examples), ("counter_examples", counters)):
                for text in texts:
                    if text.casefold() not in known:
                        rule.setdefault(kind, []).append({"text": text, "source": "harvey"})
                        known.add(text.casefold())
                        added += 1
            rule["catalog_seeded"] = True
        return added

    return _edit(db, change)


def bootstrap(db: Session, legacy_file: Path | None = None, seed: bool = True) -> None:
    """On an empty grid: take over a playbook.json left by an older Harvey (renamed afterwards so it is read once),
    otherwise create one empty rule per risk LegalDesk detects, for a lawyer to fill in."""
    if db.scalar(select(PlaybookRule.key).limit(1)) is not None:
        # Risks created before descriptions existed get the default one.
        missing = list(db.scalars(select(PlaybookRule).where(PlaybookRule.description.is_(None))))
        for row in missing:
            if row.key in DEFAULT_DESCRIPTIONS:
                row.description = DEFAULT_DESCRIPTIONS[row.key]
        if missing:
            db.commit()
        return
    if legacy_file is not None and legacy_file.exists():
        data = json.loads(legacy_file.read_text(encoding="utf-8"))
        data.setdefault("rules", [])
        save(db, data)
        legacy_file.replace(legacy_file.with_name(legacy_file.name + ".imported"))
        return
    if seed:
        save(db, {"rules": [{"key": key, "label": fr, "description": DEFAULT_DESCRIPTIONS.get(key), "articles": []}
                        for key, (_, fr) in DETECTED_KEYS.items()]})


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


def add_rule(db: Session, key: str, label: str, description: str = "") -> None:
    key = key.strip().lower().replace(" ", "_")
    if not key:
        raise RuleError("invalid", "A rule needs a key.")

    def change(data):
        if any(r["key"] == key for r in data["rules"]):
            raise RuleError("duplicate_rule", f"Rule '{key}' already exists.")
        data["rules"].append({"key": key, "label": label.strip() or key,
                              "description": " ".join(description.split())[:1000] or DEFAULT_DESCRIPTIONS.get(key),
                              "articles": [], "confirmed_by": None, "confirmed_at": None})

    _edit(db, change)


def update_label(db: Session, key: str, label: str, description: str | None = None) -> None:
    def change(data):
        rule = _rule(data, key)
        rule["label"] = label.strip() or key
        if description is not None:
            rule["description"] = " ".join(description.split())[:1000] or None

    _edit(db, change)


def delete_rule(db: Session, key: str) -> None:
    def change(data):
        _rule(data, key)
        data["rules"] = [r for r in data["rules"] if r["key"] != key]

    _edit(db, change)


def add_article(db: Session, key: str, text_id: str, number: str, why: str) -> None:
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

    _edit(db, change)


def update_article(db: Session, key: str, index: int, why: str) -> None:
    def change(data):
        rule = _rule(data, key)
        rule["articles"][index]["why"] = why.strip() or None
        _unconfirm(rule)

    _edit(db, change)


def remove_article(db: Session, key: str, index: int) -> None:
    def change(data):
        rule = _rule(data, key)
        del rule["articles"][index]
        _unconfirm(rule)

    _edit(db, change)


MIN_PHRASE_CHARS = 4


def add_phrase(db: Session, key: str, phrase: str) -> None:
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

    _edit(db, change)


def remove_phrase(db: Session, key: str, index: int) -> None:
    def change(data):
        rule = _rule(data, key)
        del rule.setdefault("phrases", [])[index]
        _unconfirm(rule)

    _edit(db, change)


EXAMPLE_KINDS = ("examples", "counter_examples")
MIN_EXAMPLE_CHARS, MAX_EXAMPLE_CHARS = 30, 600


def add_example(db: Session, key: str, kind: str, text: str, source: str = "lawyer") -> None:
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

    _edit(db, change)


def add_examples(db: Session, key: str, items: list[tuple[str, str]], source: str = "model") -> int:
    """Adds several (kind, text) examples in one save; duplicates and clauses of the wrong length are skipped."""
    def change(data):
        rule = _rule(data, key)
        known = {e["text"].casefold() for e in rule.get("examples", []) + rule.get("counter_examples", [])}
        added = 0
        for kind, text in items:
            text = " ".join(text.split())
            if kind not in EXAMPLE_KINDS or not MIN_EXAMPLE_CHARS <= len(text) <= MAX_EXAMPLE_CHARS or text.casefold() in known:
                continue
            rule.setdefault(kind, []).append({"text": text, "source": source})
            known.add(text.casefold())
            added += 1
        if added:
            _unconfirm(rule)
        return added

    return _edit(db, change)


def remove_example(db: Session, key: str, kind: str, index: int) -> None:
    if kind not in EXAMPLE_KINDS:
        raise RuleError("invalid", "Unknown example kind.")

    def change(data):
        rule = _rule(data, key)
        del rule.setdefault(kind, [])[index]
        _unconfirm(rule)

    _edit(db, change)


def move_article(db: Session, key: str, index: int, offset: int) -> None:
    def change(data):
        articles = _rule(data, key)["articles"]
        target = index + offset
        if 0 <= target < len(articles):
            articles[index], articles[target] = articles[target], articles[index]
            _unconfirm(_rule(data, key))

    _edit(db, change)


def approve(db: Session, key: str, approver, four_eyes: bool = True) -> RuleVersion:
    """A lawyer's account approves the working copy: it becomes the rule's next version, served from now on
    (R-M11-2, R-M11-3). Under the four-eyes rule, whoever last edited the rule cannot approve it."""
    rule = _rule(load(db), key)
    if not rule["articles"]:
        raise RuleError("no_articles", "A rule without articles has nothing to confirm.")
    for ref in rule["articles"]:
        article = find_article(db, ref["reference"], ref["number"])
        if article is None or not article.legal_text.is_verified:
            raise RuleError("unverified_article",
                            f"{ref['reference']}, art. {ref['number']} is not verified law: verify its text first.")
    if four_eyes and rule.get("edited_by_user_id") and rule["edited_by_user_id"] == approver.id:
        raise RuleError("four_eyes", "You made the last change to this rule: another lawyer must approve it.")

    with _lock:
        number = (db.scalar(select(func.max(RuleVersion.version)).where(RuleVersion.rule_key == key)) or 0) + 1
        version = RuleVersion(rule_key=key, version=number, payload=_content(rule), approved_by_user_id=approver.id,
                              approved_by_name=approver.signature, edited_by_user_id=rule.get("edited_by_user_id"))
        db.add(version)
        row = db.get(PlaybookRule, key)
        row.confirmed_by = approver.signature
        row.confirmed_at = datetime.now(timezone.utc).isoformat(timespec="seconds")
        db.commit()
    return version


def confirm(db: Session, key: str, approver, four_eyes: bool = True) -> RuleVersion:
    """Older name of approve."""
    return approve(db, key, approver, four_eyes)


def withdraw(db: Session, key: str) -> None:
    """Takes the rule out of reviews: its latest version is marked withdrawn and the working copy loses its approval.
    It comes back only with a new approval."""
    _rule(load(db), key)
    with _lock:
        latest = _latest_version(db, key)
        if latest is not None and latest.withdrawn_at is None:
            latest.withdrawn_at = datetime.now(timezone.utc)
        row = db.get(PlaybookRule, key)
        row.confirmed_by, row.confirmed_at = None, None
        db.commit()


def _latest_version(db: Session, key: str) -> RuleVersion | None:
    return db.scalar(select(RuleVersion).where(RuleVersion.rule_key == key).order_by(RuleVersion.version.desc()).limit(1))


def served(db: Session) -> list[dict]:
    """The rules reviews use: each rule's latest approved version, unless withdrawn, in grid order. Each dict is the
    frozen content plus key, version, approver and date."""
    order = {r.key: (r.position, r.key) for r in db.scalars(select(PlaybookRule))}
    latest: dict[str, RuleVersion] = {}
    for version in db.scalars(select(RuleVersion).order_by(RuleVersion.rule_key, RuleVersion.version)):
        latest[version.rule_key] = version
    rules = []
    for key, version in latest.items():
        if version.withdrawn_at is not None or key not in order:
            continue
        rules.append({**version.payload, "key": key, "version": version.version,
                      "approved_by": version.approved_by_name, "approved_at": version.approved_at.isoformat()})
    rules.sort(key=lambda r: order[r["key"]])
    return rules


def grid_version(rules: list[dict]) -> str:
    """A short fingerprint of which rule versions a review used, stored by LegalDesk with each review run."""
    import hashlib

    fingerprint = ";".join(f"{r['key']}:{r.get('version', 0)}" for r in sorted(rules, key=lambda r: r["key"]))
    return hashlib.sha256(fingerprint.encode()).hexdigest()[:12]


def versions_of(db: Session, key: str) -> list[RuleVersion]:
    return list(db.scalars(select(RuleVersion).where(RuleVersion.rule_key == key).order_by(RuleVersion.version.desc())))


def set_model_clause(db: Session, key: str, fr: str, en: str, source: str = "lawyer", approver=None) -> None:
    """The corrected wording LegalDesk proposes when this risk is found. Approved on its own: changing it withdraws
    its approval, never the risk's."""
    fr, en = " ".join(fr.split()), " ".join(en.split())
    if len(fr) < 30:
        raise RuleError("model_clause_too_short", "A model clause needs at least 30 characters.")

    def change(data):
        rule = _rule(data, key)
        current = rule.get("model_clause") or {}
        changed = current.get("fr") != fr or (current.get("en") or "") != en
        clause = {"fr": fr, "en": en or None, "source": source if changed else current.get("source", source),
                  "approved_by": None if changed else current.get("approved_by"),
                  "approved_at": None if changed else current.get("approved_at")}
        if approver is not None:
            _approve_clause(clause, approver)
        rule["model_clause"] = clause

    _edit(db, change)


def approve_model_clause(db: Session, key: str, approver) -> None:
    def change(data):
        clause = _rule(data, key).get("model_clause")
        if not clause:
            raise RuleError("no_model_clause", "There is no model clause to approve.")
        _approve_clause(clause, approver)

    _edit(db, change)


def _approve_clause(clause: dict, approver) -> None:
    """Records the lawyer's account, not a typed name (R-M11-2)."""
    clause["approved_by"] = approver.signature
    clause["approved_by_user_id"] = approver.id
    clause["approved_at"] = datetime.now(timezone.utc).isoformat(timespec="seconds")


def withdraw_model_clause(db: Session, key: str) -> None:
    def change(data):
        clause = _rule(data, key).get("model_clause")
        if clause:
            clause["approved_by"] = None
            clause["approved_by_user_id"] = None
            clause["approved_at"] = None

    _edit(db, change)


def unconfirm(db: Session, key: str) -> None:
    """Older name of withdraw."""
    withdraw(db, key)


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


def describe(db: Session) -> dict:
    """Every rule of the working copy, with each article resolved for display and what reviews use today: the served
    version, who approved it and when, and whether the working copy has changed since."""
    data = load(db)
    in_service = {r["key"]: r for r in served(db)}
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
                      "model_clause": rule.get("model_clause"),
                      "examples": rule.get("examples", []), "counter_examples": rule.get("counter_examples", []),
                      "detected": rule["key"] in DETECTED_KEYS,
                      "builtin_checks": _checks_view(rule),
                      "served": in_service.get(rule["key"]),
                      "changed_since_approval": rule["key"] in in_service and not rule.get("confirmed_by"),
                      "ready": bool(articles) and all(a["verified"] for a in articles)})
    return {"rules": rules}


def _checks_view(rule: dict) -> list[dict]:
    from app.services import review

    settings = rule.get("checks") or {}
    out = []
    for cid in review.checks_of(rule["key"]):
        info, setting = review.CHECK_INFO[cid], settings.get(cid) or {}
        out.append({"id": cid, "label": info.label, "enabled": setting.get("enabled", True),
                    "severity": setting.get("severity") or info.severity, "default_severity": info.severity})
    return out


def resolve(db: Session, include_unconfirmed: bool = False) -> dict:
    """What LegalDesk grounds findings in: each served rule's articles (verified law only) and its approved model
    clause. include_unconfirmed previews the working copy instead; LegalDesk never asks for it (R-M11-5)."""
    working = {r["key"]: r for r in load(db).get("rules", [])}
    source = list(working.values()) if include_unconfirmed else served(db)
    rules = {}
    unresolved = []

    for rule in source:
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
        # The model clause is approved on its own (not part of a version): taken from the working copy.
        clause = (working.get(rule["key"]) or {}).get("model_clause")
        rules[rule["key"]] = {"label": rule.get("label"), "version": rule.get("version"),
                              "approved_by": rule.get("approved_by") or rule.get("confirmed_by"),
                              "approved_at": rule.get("approved_at"),
                              "confirmed_by": rule.get("approved_by") or rule.get("confirmed_by"),
                              "severity": rule.get("severity"), "articles": articles,
                              "phrases": rule.get("phrases", []),
                              # Served only once a lawyer approved it.
                              "model_clause": ({"fr": clause["fr"], "en": clause.get("en")}
                                               if clause and (include_unconfirmed or clause.get("approved_by"))
                                               else None),
                              "examples": [e["text"] for e in rule.get("examples", [])],
                              "counter_examples": [e["text"] for e in rule.get("counter_examples", [])]}

    return {"rules": rules, "regulations": {}, "unresolved": unresolved}


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
