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
        rows.append({"name": category.name, "fr": category.fr, "status": status,
                     "risks": [{"key": r["key"], "label": r["label"]} for r in present]})
    counts = {s: sum(1 for r in rows if r["status"] == s) for s in (COVERED, PENDING, ONLY_EXTRACTED, MISSING)}
    return {"rows": rows, "counts": counts, "total": len(rows), "source": CUAD_SOURCE}
