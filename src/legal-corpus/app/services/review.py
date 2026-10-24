"""Contract review: the risks Harvey finds in a contract's clauses, for LegalDesk to show and ground in law.

Two kinds of detection:
- **By meaning**: each clause is compared with example clauses of every risk (the catalogue in risk_catalog.py plus
  the examples and phrases added in the risk grid). Wording varies; meaning is what is matched.
- **Checks in code**, for facts a model reads poorly: figures that contradict each other, a reference to an article
  the contract lacks, a party named two ways, a record kept fewer than ten years, a mention the law requires that is
  missing.

LegalDesk splits the contract into clauses and sends them; nothing leaves this server.
"""

import re
import unicodedata
from dataclasses import asdict, dataclass

from app.services.matching import ExampleMatcher, RuleExamples
from app.services.risk_catalog import CATALOG, STANDARD_CLAUSES

HIGH, MEDIUM, LOW = "High", "Medium", "Low"
POSSIBLE = "Possible"


@dataclass
class Clause:
    reference: str
    text: str
    number: int | None = None


@dataclass
class Finding:
    clause_type: str
    severity: str
    explanation: str
    recommended_action: str | None = None
    contract_reference: str | None = None
    rule_key: str | None = None
    finding_type: str | None = None
    check: str | None = None  # the built-in check that raised it; None for a finding by meaning or phrase


class _L:
    def __init__(self, fr: bool):
        self.fr = fr

    def t(self, en: str, fr: str) -> str:
        return fr if self.fr else en


def _has(text: str, *terms: str) -> bool:
    low = text.casefold()
    return any(t.casefold() in low for t in terms)


def fold(s: str) -> str:
    """Lower case, no accents, one space, straight apostrophes."""
    s = unicodedata.normalize("NFD", s.replace("’", "'"))
    s = "".join(ch for ch in s if unicodedata.category(ch) != "Mn").lower()
    return " ".join(s.split())


_FR_WORDS = {"le", "la", "les", "des", "du", "et", "est", "une", "pour", "dans", "par", "sur", "aux", "qui", "que"}
_EN_WORDS = {"the", "and", "of", "to", "is", "for", "in", "shall", "by", "with", "this", "that", "be", "any"}


def language(text: str) -> str:
    words = re.findall(r"[^\W\d_]+", text.lower())
    fr = sum(w in _FR_WORDS for w in words)
    en = sum(w in _EN_WORDS for w in words)
    return "en" if en > fr else "fr"


_HEADING_NUMBER = re.compile(r"^\s*(?:article|art\.|clause|section)\s*(\d+)", re.IGNORECASE)


def clause_number(reference: str, text: str) -> int | None:
    m = _HEADING_NUMBER.match(reference) or _HEADING_NUMBER.match(text)
    return int(m.group(1)) if m else None


def _f(c: Clause, key: str | None, title: str, severity: str, explanation: str, action: str | None) -> Finding:
    return Finding(title, severity, explanation, action, c.reference, key)


# --------------------------------------------------------------------------- checks in code

_TERMINATION_WORDS = ("terminat", "résiliation", "résilier", "résilié", "resiliation", "rupture du contrat", "fin du contrat")


def _missing_termination(text, clauses, l, out):
    if not _has(text, *_TERMINATION_WORDS):
        out.append(Finding(l.t("Termination rights", "Droit de résiliation"), HIGH,
                           l.t("The contract has no termination clause.", "Le contrat ne prévoit aucune clause de résiliation."),
                           l.t("Add termination for breach, with notice, for both parties.",
                               "Prévoir une résiliation pour manquement, avec préavis, ouverte aux deux parties."),
                           rule_key="termination"))


def _missing_force_majeure(text, clauses, l, out):
    if not _has(text, "force majeure", "cas fortuit", "act of god"):
        out.append(Finding(l.t("No force majeure clause", "Pas de clause de force majeure"), LOW,
                           l.t("The contract does not address force majeure: the statutory regime applies by default.",
                               "Le contrat ne traite pas de la force majeure : le régime légal s'applique par défaut."),
                           l.t("Add a force majeure clause listing the events, the notice to give and the right to terminate if it lasts.",
                               "Ajouter une clause de force majeure : événements visés, notification et droit de résilier si elle se prolonge."),
                           rule_key="force_majeure"))


_FOREIGN = ("constituée à l'étranger", "constituée à l’étranger", "société étrangère", "de droit étranger",
            "établie à l'étranger", "foreign company", "incorporated outside", "foreign entity")


def _personal_data(text, clauses, l, out):
    c = next((c for c in clauses if _has(c.text, "données personnelles", "données à caractère personnel",
                                         "donnees personnelles", "personal data")), None)
    if c is None:
        return
    foreign = _has(text, *_FOREIGN)
    safeguards = _has(c.text, "consentement", "sécurité", "confidentialité", "consent", "security", "confidential")
    missing = []
    if not _has(c.text, "consentement", "consent"):
        missing.append(l.t("consent", "consentement"))
    if not _has(c.text, "sécurité", "security", "mesures techniques"):
        missing.append(l.t("security", "sécurité"))
    if not _has(c.text, "sous-trait", "subcontract", "processor"):
        missing.append(l.t("subcontracting", "sous-traitance"))
    if not missing and not foreign:
        return
    detail = l.t("The contract involves personal data", "Le contrat prévoit un traitement de données personnelles")
    if missing:
        detail += l.t(f" but says nothing about {', '.join(missing)}", f" sans rien prévoir sur : {', '.join(missing)}")
    detail += (l.t("; a party is established abroad, so a transfer abroad needs prior authorisation.",
                   " ; une partie est établie à l'étranger, tout transfert vers l'étranger exige une autorisation préalable.")
               if foreign else ".")
    out.append(_f(c, "personal_data", l.t("Personal data", "Données personnelles"),
                  HIGH if foreign or not safeguards else MEDIUM, detail,
                  l.t("Add a data-processing clause: purpose, consent, security measures, subcontracting, and transfer authorisation.",
                      "Ajouter une clause de traitement des données : finalité, consentement, mesures de sécurité, sous-traitance et autorisation de transfert.")))


def _consumer_online(text, clauses, l, out):
    if not (_has(text, "en ligne", "site internet", "site web", "commande électronique", "voie électronique", "online", "website")
            and _has(text, "consommateur", "consumer")):
        return
    if _has(text, "rétractation", "retractation", "withdrawal", "cooling-off"):
        return
    out.append(Finding(l.t("Online sale to consumers", "Vente en ligne au consommateur"), HIGH,
                       l.t("Online sale to consumers with no withdrawal right stated: the consumer has a statutory right to withdraw.",
                           "Vente en ligne au consommateur sans droit de rétractation : le consommateur dispose d'un droit légal de rétractation."),
                       l.t("State the pre-contract information and the 10-working-day withdrawal right.",
                           "Prévoir l'information précontractuelle et le droit de rétractation de 10 jours ouvrables."),
                       rule_key="consumer_online"))


_DAYS = re.compile(r"(\d{1,3})\s*(?:jours|days)", re.IGNORECASE)
_PAY_WORDS = ("payable", "paiement", "payment", "paiera", "paid", "réglé", "règlement")


def _contradictory_payment(text, clauses, l, out):
    for c in clauses:
        days = []
        for sentence in re.split(r"(?<=[.;])\s+", c.text):
            if _has(sentence, *_PAY_WORDS):
                for m in _DAYS.finditer(sentence):
                    if int(m.group(1)) not in days:
                        days.append(int(m.group(1)))
        if len(days) < 2:
            continue
        listed = " / ".join(l.t(f"{d} days", f"{d} jours") for d in days)
        out.append(_f(c, "contradiction", l.t("Contradictory payment terms", "Délais de paiement contradictoires"), HIGH,
                      l.t(f"The clause sets several payment periods ({listed}); it is unclear which one applies.",
                          f"La clause fixe plusieurs délais de paiement ({listed}) ; on ne sait pas lequel s'applique."),
                      l.t("Keep a single payment period and state late-payment interest.",
                          "Retenir un seul délai de paiement et prévoir des intérêts de retard.")))


_ARTICLE_REF = re.compile(
    r"article\s+(\d+)\s+(?:du\s+présent|du\s+contrat|de\s+ce\s+contrat|ci-dessus|ci-dessous|of\s+this|hereof|above|below)"
    r"|clause\s+(\d+)\s+(?:du\s+présent|of\s+this|hereof)", re.IGNORECASE)


def _missing_references(text, clauses, l, out):
    numbers = {c.number for c in clauses if c.number is not None}
    if len(numbers) < 3:
        return
    for c in clauses:
        for m in _ARTICLE_REF.finditer(c.text):
            n = int(m.group(1) or m.group(2))
            if n in numbers or n == c.number:
                continue
            out.append(_f(c, "contradiction", l.t("Reference to a missing article", "Renvoi à un article inexistant"), HIGH,
                          l.t(f"The clause refers to Article {n}, which the contract does not contain (it stops at Article {max(numbers)}).",
                              f"La clause renvoie à l'article {n}, qui n'existe pas dans le contrat (il s'arrête à l'article {max(numbers)})."),
                          l.t("Fix the reference or add the missing article.", "Corriger le renvoi ou ajouter l'article manquant.")))


_COMPANY = re.compile(r"\b((?:[A-ZÀ-Ý][\w&'-]*\s+){1,3})(SARL|SUARL|SAS|SA|Ltd|LLC|Inc|GmbH|Group|Groupe)\b")


def _inconsistent_parties(text, clauses, l, out):
    names = []
    for m in _COMPANY.finditer(text):
        name = f"{m.group(1).strip()} {m.group(2)}"
        if name not in names:
            names.append(name)
    groups: dict[str, list[str]] = {}
    for n in names:
        groups.setdefault(n.split(" ")[0], []).append(n)
    for group in groups.values():
        if len(group) > 1:
            out.append(Finding(l.t("Inconsistent party names", "Désignation des parties incohérente"), MEDIUM,
                               l.t(f"The same party is named in different ways: {', '.join(group)}. The contract may not bind the right legal entity.",
                                   f"Une même partie est désignée de plusieurs façons : {', '.join(group)}. Le contrat risque de ne pas engager la bonne personne morale."),
                               l.t("Use one exact legal name per party, with its registration number.",
                                   "Utiliser une seule dénomination exacte par partie, avec son numéro d'immatriculation."),
                               l.t("Parties", "Parties"), "contradiction"))


_BANK = re.compile(r"\b(?:la|une|cette|de\s+la)\s+banque\b|établissement\s+financier|\bthe\s+bank\b|financial\s+institution",
                   re.IGNORECASE)


def _bank_checks(text, clauses, l, out):
    account = _has(text, "convention de compte", "ouverture de compte", "compte de dépôt", "account agreement", "deposit account")
    if account:
        c = next((c for c in clauses if _has(c.text, "compte", "account")), clauses[0] if clauses else None)
        if c is not None:
            missing = []
            if not _has(text, "commission", "tarif", "frais", "fees"):
                missing.append(l.t("the list and amount of commissions", "la liste et le montant des commissions"))
            if not _has(text, "médiat", "mediat"):
                missing.append(l.t("the banking mediator and how to refer to it", "le médiateur bancaire et ses modalités de saisine"))
            if missing:
                out.append(_f(c, "bank_account", l.t("Account agreement", "Convention de compte"), HIGH,
                              l.t(f"The account agreement does not state {' or '.join(missing)}.",
                                  f"La convention de compte ne mentionne pas {' ni '.join(missing)}."),
                              l.t("Add the commission schedule and a mediation clause.",
                                  "Ajouter le barème des commissions et une clause de médiation bancaire.")))
        if not _has(text, "réclamation", "complaint"):
            c = next((c for c in clauses if _has(c.text, "client", "customer")), clauses[0] if clauses else None)
            if c is not None:
                out.append(_f(c, "bank_customer_complaints", l.t("No complaint procedure", "Pas de procédure de réclamation"), MEDIUM,
                              l.t("The customer agreement does not say how the client can complain or within what time the bank answers.",
                                  "La convention ne précise pas comment le client peut adresser une réclamation ni dans quel délai la banque y répond."),
                              l.t("Describe the complaint channel, the response time and, after it, the referral to the banking mediator.",
                                  "Décrire le canal de réclamation, le délai de réponse et, à défaut de solution, la saisine du médiateur bancaire.")))

    if not _BANK.search(text):
        return

    c = next((c for c in clauses if _has(c.text, "externalis", "sous-trait", "hébergement", "infogérance", "prestation de services",
                                         "services informatiques", "outsourc", "hosting", "managed services")), None)
    if c is not None:
        supervision = _has(text, "banque centrale", "BCT", "central bank")
        audit = _has(text, "audit", "contrôle sur place", "droit d'accès", "inspection")
        if not (supervision and audit):
            missing = []
            if not supervision:
                missing.append(l.t("access for the central bank's supervision", "l'accès de la Banque centrale pour sa supervision"))
            if not audit:
                missing.append(l.t("the bank's audit rights", "le droit d'audit de la banque"))
            out.append(_f(c, "bank_outsourcing", l.t("Bank outsourcing", "Externalisation bancaire"), HIGH,
                          l.t(f"The bank outsources part of its activity, but the contract does not provide {' or '.join(missing)}. The central bank must also be informed before signing.",
                              f"La banque externalise une partie de son activité, mais le contrat ne prévoit pas {' ni '.join(missing)}. La Banque centrale doit aussi être informée avant la signature."),
                          l.t("Add a clause guaranteeing the bank's audit rights and the central bank's supervision of the outsourced operations, and notify the central bank before signing.",
                              "Ajouter une clause garantissant le droit d'audit de la banque et la supervision par la Banque centrale des opérations externalisées, et informer la Banque centrale avant la signature.")))

    c = next((c for c in clauses if _has(c.text, "données", "informations", "fichiers", "data", "information")), None)
    if c is not None and not _has(text, "secret professionnel", "secret bancaire", "banking secrecy", "professional secrecy"):
        out.append(_f(c, "bank_secrecy", l.t("Banking secrecy", "Secret bancaire"), HIGH,
                      l.t("The provider will handle the bank's information, but the contract does not bind it to professional secrecy.",
                          "Le prestataire aura accès aux informations de la banque, mais le contrat ne le soumet pas au secret professionnel."),
                      l.t("Bind the provider and its staff to professional secrecy, including after the contract ends.",
                          "Soumettre le prestataire et son personnel au secret professionnel, y compris après la fin du contrat.")))

    c = next((c for c in clauses if _has(c.text, "actionnaire de la banque", "administrateur de la banque", "dirigeant de la banque",
                                         "filiale de la banque", "société du groupe", "société mère", "société apparentée", "partie liée",
                                         "parties liées", "shareholder of the bank", "director of the bank", "group company", "related party")), None)
    if c is not None and not _has(text, "conseil d'administration", "conseil de surveillance", "assemblée générale",
                                  "board of directors", "general meeting"):
        out.append(_f(c, "bank_related_party", l.t("Related-party agreement", "Convention réglementée"), HIGH,
                      l.t("The bank contracts with a person linked to it (shareholder, director or group company), but the contract does not mention the board's prior authorisation or the shareholders' approval.",
                          "La banque contracte avec une personne qui lui est liée (actionnaire, dirigeant ou société du groupe), mais le contrat ne mentionne ni l'autorisation préalable du conseil ni l'approbation de l'assemblée générale."),
                      l.t("Have the agreement authorised by the board before signing (the interested person not voting), approved by the general meeting, and notified to the central bank.",
                          "Faire autoriser la convention par le conseil avant la signature (sans vote de la personne intéressée), la soumettre à l'approbation de l'assemblée générale et en informer la Banque centrale.")))

    c = next((c for c in clauses if _has(c.text, "confidentiel", "ne pas divulguer", "ne divulguer", "à aucun tiers", "aucun tiers",
                                         "confidential", "not disclose", "no third party")), None)
    if c is not None and not _has(c.text, "banque centrale", "BCT", "autorités de contrôle", "autorité de contrôle",
                                  "autorités de supervision", "autorités compétentes", "autorité compétente", "central bank",
                                  "supervisory authorit", "competent authorit"):
        out.append(_f(c, "bank_supervisor_access", l.t("Confidentiality without a supervisor exception", "Confidentialité sans exception pour la BCT"), MEDIUM,
                      l.t("The confidentiality clause forbids disclosure to third parties without excepting the central bank and the supervisory authorities, to whom professional secrecy cannot be opposed.",
                          "La clause de confidentialité interdit toute communication aux tiers sans excepter la Banque centrale et les autorités de contrôle, auxquelles le secret professionnel n'est pas opposable."),
                      l.t("Add an exception for disclosures to the central bank, the supervisory authorities and any authority entitled by law.",
                          "Ajouter une exception pour les communications à la Banque centrale, aux autorités de contrôle et à toute autorité habilitée par la loi.")))

    onboarding = next((c for c in clauses if _has(c.text, "identification des clients", "identifier les clients", "entrée en relation",
                                                  "connaissance du client", "KYC", "pièces d'identité", "apporteur d'affaires",
                                                  "customer identification", "onboarding", "know your customer")), None)
    if onboarding is not None and not _has(text, "blanchiment", "financement du terrorisme", "money laundering", "terrorist financing"):
        out.append(_f(onboarding, "bank_aml", l.t("Customer identification by a third party", "Identification des clients confiée à un tiers"), HIGH,
                      l.t("A third party identifies the bank's customers, but the contract does not bind it to anti-money-laundering rules or give the bank the identification data.",
                          "Un tiers identifie les clients de la banque, mais le contrat ne le soumet pas aux règles de lutte contre le blanchiment et ne garantit pas à la banque l'accès aux données d'identification."),
                      l.t("Require the third party to be subject to anti-money-laundering rules and supervision, to apply the required due diligence, and to hand the bank the identification data without delay.",
                          "Exiger que le tiers soit soumis à une réglementation et à une surveillance en matière de lutte contre le blanchiment, qu'il applique les mesures de diligence requises et qu'il remette sans délai à la banque les données d'identification.")))

    retention = next((c for c in clauses
                      if (_has(c.text, "conserv", "archiv", "retain", "retention")
                          and any(int(m.group(1)) < 10 for m in _YEARS.finditer(c.text)))
                      or (_has(c.text, "détrui", "destruction", "supprim", "destroy", "delete")
                          and _has(c.text, "fin du contrat", "expiration", "terme du contrat", "end of the contract", "termination"))), None)
    if retention is not None and _has(retention.text, "client", "données", "documents", "dossiers", "records", "data", "customer"):
        out.append(_f(retention, "bank_aml", l.t("Records not kept ten years", "Conservation des documents inférieure à dix ans"), HIGH,
                      l.t("The clause has customer records destroyed at the end of the contract or kept for less than ten years, while the bank must keep them for at least ten years.",
                          "La clause prévoit la destruction des documents clients à la fin du contrat ou leur conservation pendant moins de dix ans, alors que la banque doit les conserver au moins dix ans."),
                      l.t("Keep (or return to the bank) all records needed to trace operations and identify customers for at least ten years after the operation or the account's closure.",
                          "Conserver (ou restituer à la banque) tous les documents permettant de retracer les opérations et d'identifier les clients pendant au moins dix ans après l'opération ou la clôture du compte.")))


_YEARS = re.compile(r"(\d{1,2})\)?\s*(?:ans|années|years)\b", re.IGNORECASE)

CHECKS = [_missing_termination, _missing_force_majeure, _personal_data, _consumer_online, _contradictory_payment,
          _missing_references, _inconsistent_parties, _bank_checks]


@dataclass(frozen=True)
class CheckInfo:
    rule_key: str
    label: tuple[str, str]  # (en, fr)
    severity: str           # the usual severity; the risk grid can override it


# Every built-in check, by id. The risk grid can switch each one off or change its severity.
CHECK_INFO: dict[str, CheckInfo] = {
    "missing_termination": CheckInfo("termination", ("No termination clause", "Aucune clause de résiliation"), HIGH),
    "missing_force_majeure": CheckInfo("force_majeure", ("No force majeure clause", "Aucune clause de force majeure"), LOW),
    "personal_data_safeguards": CheckInfo("personal_data", ("Personal data without consent, security or subcontracting terms, or sent abroad",
                                                           "Données personnelles sans consentement, sécurité ou sous-traitance, ou transfert à l'étranger"), HIGH),
    "consumer_withdrawal": CheckInfo("consumer_online", ("Online sale to consumers without a withdrawal right",
                                                        "Vente en ligne au consommateur sans droit de rétractation"), HIGH),
    "contradictory_payment": CheckInfo("contradiction", ("Several payment periods in one clause", "Plusieurs délais de paiement dans une clause"), HIGH),
    "missing_reference": CheckInfo("contradiction", ("Reference to an article the contract lacks", "Renvoi à un article inexistant"), HIGH),
    "party_names": CheckInfo("contradiction", ("A party named in different ways", "Une partie désignée de plusieurs façons"), MEDIUM),
    "bank_account_mentions": CheckInfo("bank_account", ("Account agreement without commissions or mediator", "Convention de compte sans commissions ni médiateur"), HIGH),
    "bank_complaints": CheckInfo("bank_customer_complaints", ("Customer agreement without a complaint procedure", "Convention client sans procédure de réclamation"), MEDIUM),
    "bank_outsourcing": CheckInfo("bank_outsourcing", ("Outsourcing without audit rights or central bank supervision", "Externalisation sans droit d'audit ni supervision BCT"), HIGH),
    "bank_secrecy": CheckInfo("bank_secrecy", ("Provider not bound to professional secrecy", "Prestataire non soumis au secret professionnel"), HIGH),
    "bank_related_party": CheckInfo("bank_related_party", ("Related party without board or shareholder approval", "Partie liée sans autorisation du conseil ni de l'assemblée"), HIGH),
    "bank_supervisor_access": CheckInfo("bank_supervisor_access", ("Confidentiality with no exception for the central bank", "Confidentialité sans exception pour la BCT"), MEDIUM),
    "aml_third_party": CheckInfo("bank_aml", ("Customers identified by a third party not bound by AML rules", "Clients identifiés par un tiers non soumis aux règles anti-blanchiment"), HIGH),
    "aml_retention": CheckInfo("bank_aml", ("Customer records kept less than ten years", "Documents clients conservés moins de dix ans"), HIGH),
}

_FUNCTION_CHECK = {
    "_missing_termination": "missing_termination", "_missing_force_majeure": "missing_force_majeure",
    "_personal_data": "personal_data_safeguards", "_consumer_online": "consumer_withdrawal",
    "_contradictory_payment": "contradictory_payment", "_missing_references": "missing_reference",
    "_inconsistent_parties": "party_names",
}


def _check_id(function, finding: Finding) -> str:
    if function.__name__ in _FUNCTION_CHECK:
        return _FUNCTION_CHECK[function.__name__]
    if finding.rule_key == "bank_account":
        return "bank_account_mentions"
    if finding.rule_key == "bank_customer_complaints":
        return "bank_complaints"
    if finding.rule_key == "bank_aml":
        return "aml_retention" if ("ten years" in finding.clause_type or "dix ans" in finding.clause_type) else "aml_third_party"
    return finding.rule_key or function.__name__


def checks_of(rule_key: str) -> list[str]:
    return [cid for cid, info in CHECK_INFO.items() if info.rule_key == rule_key]


def _run_checks(text, clauses, l, grid_rules) -> list[Finding]:
    """The built-in checks, each tagged with its id, then switched off or re-graded as the risk grid says."""
    settings = {}
    for rule in grid_rules:
        settings.update(rule.get("checks") or {})
    out: list[Finding] = []
    for function in CHECKS:
        found: list[Finding] = []
        function(text, clauses, l, found)
        for f in found:
            f.check = _check_id(function, f)
            setting = settings.get(f.check) or {}
            if setting.get("enabled", True) is False:
                continue
            if setting.get("severity") in (HIGH, MEDIUM, LOW):
                f.severity = setting["severity"]
            out.append(f)
    return out


# --------------------------------------------------------------------------- by meaning

_PER_DAY = re.compile(r"(\d+(?:[.,]\d+)?)\s*(?:%|pour\s*cent|percent)[^.]{0,60}?(?:par|per)\s+(?:jour|day)", re.IGNORECASE)


def _refine(finding: Finding, clause: Clause, l: _L) -> Finding:
    """Facts that change a meaning-based finding's severity: a penalty's cap, a renewal's notice period."""
    text = clause.text
    if finding.rule_key == "penalty":
        uncapped = _has(text, "sans plafond", "sans limite", "uncapped", "without cap", "without limit")
        capped = not uncapped and _has(text, "plafonn", "dans la limite de", "maximum", "capped", "cap of")
        if capped:
            finding.severity, finding.recommended_action = LOW, None
            finding.explanation = l.t("A capped penalty clause was detected.", "Une clause pénale plafonnée a été détectée.")
        elif uncapped:
            finding.severity = HIGH
            m = _PER_DAY.search(text)
            if m:
                rate = float(m.group(1).replace(",", "."))
                days = int(-(-100 // rate)) if rate > 0 else "?"
                finding.explanation = l.t(
                    f"The penalty has no cap: {m.group(1)}% of the price per day — after {days} days it exceeds the whole contract value.",
                    f"La pénalité n'a pas de plafond : {m.group(1)} % du prix par jour, elle dépasse la valeur totale du contrat au bout de {days} jours.")
    elif finding.rule_key == "auto_renewal" and not _has(text, "préavis", "notice", "dénonc"):
        finding.explanation = l.t("The contract renews automatically and no notice period to stop it is stated.",
                                  "Le contrat se renouvelle automatiquement et aucun délai de préavis pour l'arrêter n'est prévu.")
    return finding


# Risks that are mistakes inside a contract (contradictory figures, wrong references, a party named two ways): only the
# checks in code can see them. A clause that merely talks about contradictions or references is not one.
CODE_ONLY = {"contradiction"}


def meaning_rules(grid_rules: list[dict]) -> list[RuleExamples]:
    """The examples and counter-examples of each risk in the risk grid (confirmed or not)."""
    by_key = {r["key"]: r for r in grid_rules}
    rules = []
    for key in sorted((set(CATALOG) | set(by_key)) - CODE_ONLY):
        entry, grid = CATALOG.get(key), by_key.get(key, {})
        # Only what the risk grid shows is used: Harvey's own clauses were copied into it (source "harvey").
        examples = [e["text"] for e in grid.get("examples", [])]
        counters = [e["text"] for e in grid.get("counter_examples", [])] + STANDARD_CLAUSES
        if examples:
            rules.append(RuleExamples(key, grid.get("label") or (entry.label[1] if entry else key), examples, counters))
    return rules


# A clause can carry several risks: every risk this close to the clause's best match counts.
WITHIN = 0.04


def _meaning_findings(clauses, grid_rules, matcher, l, threshold):
    if matcher is None:
        return []
    matches = matcher.classify([(c.reference, c.text) for c in clauses], meaning_rules(grid_rules), threshold, within=WITHIN)
    by_ref = {c.reference: c for c in clauses}
    labels = {r["key"]: r.get("label") for r in grid_rules}
    out = []
    for m in matches:
        entry, clause = CATALOG.get(m.rule_key), by_ref[m.reference]
        title = (l.t(*entry.label) if entry else None) or labels.get(m.rule_key) or m.rule_key
        if entry is None:  # a risk created in the grid only: a lead, worded generically
            out.append(Finding(title, MEDIUM,
                               l.t(f"This sentence reads like an example of this risk in the risk grid: “{m.sentence}”",
                                   f"Cette phrase ressemble à un exemple de ce risque dans la grille des risques : « {m.sentence} »"),
                               l.t(f"Check the clause against the “{title}” risk.", f"Vérifier la clause au regard du risque « {title} »."),
                               m.reference, m.rule_key))
            continue
        finding = Finding(title, entry.severity, l.t(*entry.explanation), l.t(*entry.action) if entry.action else None,
                          m.reference, m.rule_key)
        out.append(_refine(finding, clause, l))
    return out


def _phrase_findings(clauses, grid_rules, l, existing):
    out = []
    for rule in grid_rules:
        for phrase in rule.get("phrases", []):
            folded = fold(phrase)
            if len(folded) < 4:
                continue
            for c in clauses:
                if any(f.rule_key == rule["key"] and f.contract_reference == c.reference for f in existing + out):
                    continue
                if folded in fold(c.text):
                    label = rule.get("label") or rule["key"]
                    out.append(_f(c, rule["key"], label, MEDIUM,
                                  l.t(f"The risk grid flags the wording “{phrase.strip()}” for this risk.",
                                      f"La grille des risques signale la formulation « {phrase.strip()} » pour ce risque."),
                                  l.t(f"Check this clause against the “{label}” risk.",
                                      f"Vérifier cette clause au regard du risque « {label} ».")))
    return out


def review(clauses: list[Clause], grid_rules: list[dict], matcher: ExampleMatcher | None,
           lang: str | None = None, threshold: float | None = None) -> tuple[list[Finding], str]:
    text = "\n\n".join(c.text for c in clauses)
    lang = lang if lang in ("fr", "en") else language(text)
    l = _L(lang == "fr")
    for c in clauses:
        if c.number is None:
            c.number = clause_number(c.reference, c.text)

    findings: list[Finding] = _run_checks(text, clauses, l, grid_rules)
    for f in _meaning_findings(clauses, grid_rules, matcher, l, threshold):
        if not any(x.rule_key == f.rule_key and x.contract_reference == f.contract_reference for x in findings):
            findings.append(f)
    findings += _phrase_findings(clauses, grid_rules, l, findings)
    order = {HIGH: 0, MEDIUM: 1, LOW: 2}
    findings.sort(key=lambda f: order.get(f.severity, 3))
    return findings, lang


def as_dicts(findings: list[Finding]) -> list[dict]:
    return [asdict(f) for f in findings]
