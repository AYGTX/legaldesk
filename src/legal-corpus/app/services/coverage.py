"""How the risk grid covers CUAD, the reference set of 41 contract clause categories (The Atticus Project, from
category_descriptions.csv in github.com/The-Atticus-Project/cuad). A planning aid: which standard clause types
LegalDesk already reviews, which it only extracts, and which no risk covers yet."""

from dataclasses import dataclass

CUAD_SOURCE = "https://github.com/The-Atticus-Project/cuad/blob/main/category_descriptions.csv"

# What LegalDesk does with a category when no risk of the grid applies: its contract profile and lifecycle already
# read parties and dates.
EXTRACTED = "extracted"


@dataclass(frozen=True)
class Category:
    name: str             # exact CUAD name
    fr: str               # French label
    keys: tuple[str, ...]  # risk-grid keys that review it
    extracted: bool = False


CATEGORIES: list[Category] = [
    Category("Document Name", "Intitulé du document", (), extracted=True),
    Category("Parties", "Parties", ("contradiction",), extracted=True),
    Category("Agreement Date", "Date de signature", (), extracted=True),
    Category("Effective Date", "Date d'entrée en vigueur", (), extracted=True),
    Category("Expiration Date", "Date d'expiration", (), extracted=True),
    Category("Renewal Term", "Durée de renouvellement", ("auto_renewal",)),
    Category("Notice Period to Terminate Renewal", "Préavis de non-renouvellement", ("auto_renewal",)),
    Category("Governing Law", "Loi applicable", ()),
    Category("Most Favored Nation", "Clause du client le plus favorisé", ()),
    Category("Non-Compete", "Non-concurrence", ("exclusivity",)),
    Category("Exclusivity", "Exclusivité", ("exclusivity",)),
    Category("No-Solicit of Customers", "Non-sollicitation de clientèle", ("market_sharing",)),
    Category("Competitive Restriction Exception", "Exception aux restrictions de concurrence", ()),
    Category("No-Solicit of Employees", "Non-sollicitation du personnel", ()),
    Category("Non-Disparagement", "Non-dénigrement", ()),
    Category("Termination for Convenience", "Résiliation pour convenance", ("termination", "breach_notice")),
    Category("Rofr/Rofo/Rofn", "Droit de préférence / de première offre", ()),
    Category("Change of Control", "Changement de contrôle", ()),
    Category("Anti-Assignment", "Incessibilité du contrat", ()),
    Category("Revenue/Profit Sharing", "Partage de revenus ou de bénéfices", ()),
    Category("Price Restrictions", "Restrictions de prix", ("resale_price", "unilateral_change")),
    Category("Minimum Commitment", "Engagement minimum", ()),
    Category("Volume Restriction", "Restriction de volume", ()),
    Category("IP Ownership Assignment", "Cession de la propriété intellectuelle", ()),
    Category("Joint IP Ownership", "Copropriété intellectuelle", ()),
    Category("License Grant", "Concession de licence", ()),
    Category("Non-Transferable License", "Licence incessible", ()),
    Category("Affiliate License-Licensor", "Licence des affiliés (concédant)", ()),
    Category("Affiliate License-Licensee", "Licence des affiliés (licencié)", ()),
    Category("Unlimited/All-You-Can-Eat-License", "Licence illimitée", ()),
    Category("Irrevocable or Perpetual License", "Licence irrévocable ou perpétuelle", ()),
    Category("Source Code Escrow", "Dépôt du code source", ()),
    Category("Post-Termination Services", "Services après la fin du contrat", ()),
    Category("Audit Rights", "Droit d'audit", ("bank_outsourcing",)),
    Category("Uncapped Liability", "Responsabilité non plafonnée", ("liability", "indemnification")),
    Category("Cap on Liability", "Plafond de responsabilité", ("liability",)),
    Category("Liquidated Damages", "Clause pénale / dommages forfaitaires", ("penalty",)),
    Category("Warranty Duration", "Durée de garantie", ("works_guarantee",)),
    Category("Insurance", "Assurance", ()),
    Category("Covenant Not to Sue", "Renonciation à agir", ()),
    Category("Third Party Beneficiary", "Stipulation pour autrui", ()),
]

COVERED, PENDING, ONLY_EXTRACTED, MISSING = "covered", "pending", "extracted", "missing"


def report(rules: list[dict]) -> dict:
    """Each category with its status against the grid: covered by an approved risk, covered by a risk awaiting
    approval, only extracted by LegalDesk, or not covered."""
    by_key = {r["key"]: r for r in rules}
    rows = []
    for category in CATEGORIES:
        present = [by_key[k] for k in category.keys if k in by_key]
        if any(r.get("confirmed_by") for r in present):
            status = COVERED
        elif present:
            status = PENDING
        elif category.extracted:
            status = ONLY_EXTRACTED
        else:
            status = MISSING
        question = QUESTIONS.get(category.name, ("", ""))
        rows.append({"name": category.name, "fr": category.fr, "status": status,
                     "question_en": question[0], "question_fr": question[1],
                     "risks": [{"key": r["key"], "label": r["label"]} for r in present]})
    counts = {s: sum(1 for r in rows if r["status"] == s) for s in (COVERED, PENDING, ONLY_EXTRACTED, MISSING)}
    return {"rows": rows, "counts": counts, "total": len(rows), "source": CUAD_SOURCE}


# The question CUAD's lawyers answer for each category (category_descriptions.csv), and a French translation.
QUESTIONS: dict[str, tuple[str, str]] = {
    "Document Name": ("The name of the contract.", "L'intitulé du contrat."),
    "Parties": ("The two or more parties who signed the contract.", "Les deux parties (ou plus) qui ont signé le contrat."),
    "Agreement Date": ("The date of the contract.", "La date du contrat."),
    "Effective Date": ("The date when the contract is effective.", "La date à laquelle le contrat prend effet."),
    "Expiration Date": ("On what date will the contract's initial term expire?", "À quelle date la durée initiale du contrat expire-t-elle ?"),
    "Renewal Term": ("What is the renewal term after the initial term expires, including automatic and unilateral extensions?",
                     "Quelle est la durée de renouvellement à l'issue de la durée initiale, y compris les prorogations automatiques ou unilatérales ?"),
    "Notice Period to Terminate Renewal": ("What is the notice period required to terminate renewal?",
                                           "Quel préavis faut-il respecter pour empêcher le renouvellement ?"),
    "Governing Law": ("Which state's or country's law governs the interpretation of the contract?",
                      "Quelle loi régit l'interprétation du contrat ?"),
    "Most Favored Nation": ("If a third party gets better terms, is the buyer entitled to those better terms?",
                            "Si un tiers obtient de meilleures conditions, l'acheteur a-t-il droit aux mêmes ?"),
    "Non-Compete": ("Is a party restricted from competing with the counterparty or operating in a geography, business or technology sector?",
                    "Une partie est-elle empêchée de concurrencer l'autre ou d'exercer dans une zone, une activité ou un secteur ?"),
    "Exclusivity": ("Is there an exclusive dealing commitment with the counterparty, during or after the contract?",
                    "Existe-t-il un engagement d'exclusivité envers l'autre partie, pendant ou après le contrat ?"),
    "No-Solicit of Customers": ("Is a party restricted from soliciting the counterparty's customers or partners?",
                                "Une partie est-elle empêchée de démarcher les clients ou partenaires de l'autre ?"),
    "Competitive Restriction Exception": ("Exceptions or carve-outs to non-compete, exclusivity and no-solicit of customers.",
                                          "Les exceptions aux clauses de non-concurrence, d'exclusivité et de non-sollicitation de clientèle."),
    "No-Solicit of Employees": ("Is a party restricted from soliciting or hiring the counterparty's employees or contractors?",
                                "Une partie est-elle empêchée de débaucher ou d'embaucher le personnel ou les prestataires de l'autre ?"),
    "Non-Disparagement": ("Is a party required not to disparage the counterparty?", "Une partie s'engage-t-elle à ne pas dénigrer l'autre ?"),
    "Termination for Convenience": ("Can a party terminate the contract without cause, by notice alone?",
                                    "Une partie peut-elle résilier le contrat sans motif, par simple préavis ?"),
    "Rofr/Rofo/Rofn": ("Does a party have a right of first refusal, first offer or first negotiation?",
                       "Une partie bénéficie-t-elle d'un droit de préférence, de première offre ou de première négociation ?"),
    "Change of Control": ("Can a party terminate, or is consent or notice required, if the other undergoes a change of control?",
                          "Une partie peut-elle résilier, ou son accord est-il requis, en cas de changement de contrôle de l'autre ?"),
    "Anti-Assignment": ("Is consent or notice required if the contract is assigned to a third party?",
                        "L'accord de l'autre partie est-il requis pour céder le contrat à un tiers ?"),
    "Revenue/Profit Sharing": ("Is a party required to share revenue or profit with the counterparty?",
                               "Une partie doit-elle partager ses revenus ou bénéfices avec l'autre ?"),
    "Price Restrictions": ("Is a party restricted in raising or reducing its prices?", "Une partie est-elle limitée dans la hausse ou la baisse de ses prix ?"),
    "Minimum Commitment": ("Is there a minimum order or minimum amount a party must buy per period?",
                           "Existe-t-il une commande ou un volume minimum à acheter par période ?"),
    "Volume Restriction": ("Is there a fee increase or consent requirement if use exceeds a threshold?",
                           "Un dépassement de seuil d'utilisation entraîne-t-il un surcoût ou une autorisation ?"),
    "IP Ownership Assignment": ("Does intellectual property created by one party become the counterparty's property?",
                                "La propriété intellectuelle créée par une partie devient-elle la propriété de l'autre ?"),
    "Joint IP Ownership": ("Is intellectual property jointly owned by the parties?", "La propriété intellectuelle est-elle détenue conjointement par les parties ?"),
    "License Grant": ("Does the contract grant a licence from one party to the other?", "Le contrat concède-t-il une licence d'une partie à l'autre ?"),
    "Non-Transferable License": ("Is a party limited in transferring the licence to a third party?",
                                 "Une partie est-elle limitée dans la cession de la licence à un tiers ?"),
    "Affiliate License-Licensor": ("Is a licence granted by the licensor's affiliates, or does it include their intellectual property?",
                                   "Une licence est-elle concédée par les sociétés affiliées du concédant, ou inclut-elle leur propriété intellectuelle ?"),
    "Affiliate License-Licensee": ("Is a licence granted to the licensee and its affiliates?", "Une licence est-elle concédée au licencié et à ses sociétés affiliées ?"),
    "Unlimited/All-You-Can-Eat-License": ("Does a party get an enterprise, all-you-can-eat or unlimited usage licence?",
                                          "Une partie bénéficie-t-elle d'une licence illimitée ou « entreprise » ?"),
    "Irrevocable or Perpetual License": ("Is a licence irrevocable or perpetual?", "Une licence est-elle irrévocable ou perpétuelle ?"),
    "Source Code Escrow": ("Must a party deposit its source code in escrow, released on events such as insolvency?",
                           "Une partie doit-elle déposer son code source auprès d'un tiers, libéré en cas d'insolvabilité notamment ?"),
    "Post-Termination Services": ("Is a party bound by obligations after the contract ends (transition, payment, transfer, wind-down)?",
                                  "Une partie reste-t-elle tenue d'obligations après la fin du contrat (transition, paiement, transfert, réversibilité) ?"),
    "Audit Rights": ("Can a party audit the counterparty's books, records or premises?",
                     "Une partie peut-elle auditer les comptes, registres ou locaux de l'autre ?"),
    "Uncapped Liability": ("Is a party's liability uncapped for a breach, including specific breaches such as IP or confidentiality?",
                           "La responsabilité d'une partie est-elle illimitée en cas de manquement, y compris pour certains manquements (PI, confidentialité) ?"),
    "Cap on Liability": ("Is liability capped, including time limits to bring claims or a maximum recovery?",
                         "La responsabilité est-elle plafonnée, y compris par un délai pour agir ou un montant maximum ?"),
    "Liquidated Damages": ("Does a clause award liquidated damages for breach or a termination fee?",
                           "Une clause prévoit-elle des dommages forfaitaires en cas de manquement ou une indemnité de résiliation ?"),
    "Warranty Duration": ("What is the duration of any warranty against defects or errors?", "Quelle est la durée de la garantie contre les défauts ou erreurs ?"),
    "Insurance": ("Must a party maintain insurance for the benefit of the counterparty?",
                  "Une partie doit-elle maintenir une assurance au profit de l'autre ?"),
    "Covenant Not to Sue": ("Is a party restricted from contesting the counterparty's IP or bringing unrelated claims?",
                            "Une partie renonce-t-elle à contester la propriété intellectuelle de l'autre ou à agir contre elle ?"),
    "Third Party Beneficiary": ("Is there a non-party who benefits from the contract and can enforce it?",
                                "Un tiers au contrat en bénéficie-t-il et peut-il en exiger l'exécution ?"),
}
