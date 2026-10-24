"""The risks Harvey finds by meaning: for each, what to tell the reviewer and the example clauses that carry it.

These examples were written for Harvey, not taken from real contracts. They are used as they stand until a lawyer
reviews them in the risk grid; examples and counter-examples added there are used alongside them. Counter-examples are
close but safe wordings: a sentence closer to one of them than to the risk does not match.
"""

from dataclasses import dataclass, field


@dataclass(frozen=True)
class CatalogRisk:
    label: tuple[str, str]          # (en, fr)
    severity: str                   # High | Medium | Low
    explanation: tuple[str, str]
    action: tuple[str, str] | None
    examples: list[str] = field(default_factory=list)
    counter_examples: list[str] = field(default_factory=list)


CATALOG: dict[str, CatalogRisk] = {
    "liability": CatalogRisk(
        ("Unlimited liability", "Responsabilité illimitée"), "High",
        ("The clause leaves a party's liability without a cap, or keeps whole categories of claims outside the cap.",
         "La clause laisse la responsabilité d'une partie sans plafond, ou laisse des catégories entières de réclamations hors du plafond."),
        ("Cap liability (e.g. at the contract price) and exclude indirect losses, except for gross negligence or fraud.",
         "Plafonner la responsabilité (par exemple au prix du contrat) et exclure les pertes indirectes, sauf faute lourde ou dol."),
        ["Le Prestataire est responsable sans limite de tous les dommages directs et indirects subis par le Client.",
         "La responsabilité du Fournisseur est illimitée et couvre l'ensemble des préjudices, y compris les pertes de chiffre d'affaires.",
         "Le Prestataire répondra de l'intégralité des conséquences dommageables de ses manquements, sans aucun plafond.",
         "Le plafond de responsabilité ne s'applique pas aux obligations de garantie, de confidentialité et de propriété intellectuelle.",
         "The Supplier shall be liable without limitation for all direct and indirect losses suffered by the Customer.",
         "The limitation of liability shall not apply to indemnification obligations or breach of confidentiality."],
        ["La responsabilité totale de chaque partie est plafonnée au montant payé au titre du contrat au cours des douze derniers mois.",
         "Sauf faute lourde ou dol, la responsabilité de chaque partie ne saurait excéder le prix du contrat.",
         "Each party's aggregate liability shall not exceed the fees paid in the twelve months preceding the claim."]),
    "indemnification": CatalogRisk(
        ("Indemnity", "Garantie / indemnisation"), "Medium",
        ("A party undertakes to indemnify the other against claims; check who bears the risk and whether it is limited.",
         "Une partie s'engage à garantir l'autre contre des réclamations ; vérifiez sur qui pèse le risque et s'il est limité."),
        ("Limit the indemnity to claims caused by the party's own breach, with a cap and a time limit.",
         "Limiter la garantie aux réclamations causées par un manquement de la partie, avec un plafond et une durée."),
        ["Le Prestataire garantit le Client contre toute réclamation de tiers, de quelque nature que ce soit, sans limite de montant ni de durée.",
         "La Banque indemnisera et tiendra indemne le Prestataire de toute action, perte ou frais liés à l'exécution du contrat.",
         "Le Fournisseur s'engage à dégager le Client de toute responsabilité et à le garantir de toute condamnation prononcée à son encontre.",
         "The Supplier shall indemnify and hold harmless the Customer from any claims of any kind arising from the services."],
        ["Chaque partie reste responsable de ses propres fautes dans les conditions du droit commun."]),
    "penalty": CatalogRisk(
        ("Penalty clause", "Clause pénale"), "Medium",
        ("A penalty is due for late or defective performance; check that it is capped.",
         "Une pénalité est due en cas de retard ou de mauvaise exécution ; vérifiez qu'elle est plafonnée."),
        ("Cap the total penalty (commonly 5 to 10% of the price).", "Plafonner le total des pénalités (couramment 5 à 10 % du prix)."),
        ["En cas de retard de livraison, le Prestataire versera une pénalité de 2 % du prix par jour de retard, sans plafond.",
         "Tout retard donnera lieu à l'application de pénalités calculées par jour calendaire de retard.",
         "À titre de clause pénale, le Fournisseur paiera une indemnité forfaitaire de 5 000 dinars par manquement constaté.",
         "In case of delay, the Supplier shall pay liquidated damages equal to 1% of the price per day of delay."],
        ["Les pénalités de retard sont plafonnées à 10 % du montant total du contrat et sont libératoires."]),
    "auto_renewal": CatalogRisk(
        ("Automatic renewal", "Reconduction tacite"), "Medium",
        ("The contract renews automatically; check the notice period and how to stop it.",
         "Le contrat se renouvelle automatiquement ; vérifiez le délai de préavis et la façon d'y mettre fin."),
        ("State a notice period (e.g. 3 months before the renewal date).", "Prévoir un délai de préavis (par exemple 3 mois avant l'échéance)."),
        ["Le contrat est conclu pour une durée d'un an renouvelable par tacite reconduction pour des périodes successives d'un an.",
         "À son échéance, le présent contrat sera reconduit tacitement sauf dénonciation par l'une des parties.",
         "Le contrat se renouvellera automatiquement pour une durée identique.",
         "This Agreement shall renew automatically for successive periods of one year unless either party gives notice of non-renewal."],
        ["Le contrat prend fin de plein droit à son terme, sans possibilité de reconduction tacite.",
         "Le contrat pourra être renouvelé par avenant écrit signé des deux parties."]),
    "exclusivity": CatalogRisk(
        ("Exclusivity / non-compete", "Exclusivité / non-concurrence"), "Medium",
        ("An exclusivity or non-compete restriction binds a party; check its scope, territory and duration.",
         "Une restriction d'exclusivité ou de non-concurrence lie une partie ; vérifiez son périmètre, son territoire et sa durée."),
        ("Limit exclusivity in scope, territory and duration.", "Limiter l'exclusivité en périmètre, territoire et durée."),
        ["Le Client s'engage à s'approvisionner exclusivement auprès du Fournisseur pendant toute la durée du contrat.",
         "Le Prestataire s'interdit de travailler pour un concurrent du Client sur le marché tunisien pendant deux ans après la fin du contrat.",
         "Le Distributeur bénéficie d'une exclusivité sur l'ensemble du territoire tunisien.",
         "The Supplier agrees not to compete with the Customer for two years after termination.",
         "The Customer shall purchase all its requirements exclusively from the Supplier and shall not engage any other supplier."],
        ["Les tribunaux de Tunis sont seuls compétents pour connaître de tout litige.",
         "Les livrables restent la propriété exclusive du Prestataire, qui concède au Client une licence non exclusive.",
         "The Supplier grants the Customer a non-exclusive, non-transferable license to use the software."]),
    "unilateral_change": CatalogRisk(
        ("Unilateral change of the contract", "Modification unilatérale du contrat"), "Medium",
        ("One party may change the contract, its scope or its price alone.",
         "Une partie peut modifier seule le contrat, son périmètre ou son prix."),
        ("Require a written amendment agreed by both parties, with price adjustment.",
         "Exiger un avenant écrit accepté par les deux parties, avec ajustement du prix."),
        ["Le Prestataire se réserve le droit de modifier à sa seule discrétion le périmètre des services, sans préavis.",
         "Le Fournisseur pourra modifier unilatéralement ses tarifs à tout moment.",
         "The Supplier may amend these terms at its sole discretion without notice."],
        ["Toute modification du contrat fera l'objet d'un avenant écrit signé par les deux parties."]),
    "payment": CatalogRisk(
        ("Late-payment interest excluded", "Intérêts de retard exclus"), "Medium",
        ("The clause excludes interest or costs on late payment: the creditor gives up the statutory interest.",
         "La clause exclut tout intérêt ou frais en cas de retard de paiement : le créancier renonce aux intérêts légaux."),
        ("Provide late-payment interest and recovery costs.", "Prévoir des intérêts de retard et des frais de recouvrement."),
        ["Aucun intérêt de retard ne sera dû en cas de paiement tardif des factures.",
         "Les retards de paiement ne donneront lieu à aucune pénalité ni intérêt.",
         "No interest shall accrue on late payments."],
        ["Tout retard de paiement entraîne de plein droit des intérêts au taux légal."]),
    "deposit": CatalogRisk(
        ("Deposit (arrhes / acompte)", "Arrhes / acompte"), "Low",
        ("The contract provides a deposit; check how it is credited, kept or returned.",
         "Le contrat prévoit des arrhes ou un acompte ; vérifiez leur imputation, leur conservation et leur restitution."),
        ("Keep the deposit only if the paying party is at fault.", "Ne conserver les arrhes qu'en cas de faute de la partie qui les a versées."),
        ["À la signature, le Client verse un acompte de 30 % du prix, définitivement acquis au Prestataire en toute hypothèse.",
         "Les arrhes versées restent acquises au Vendeur quelle que soit la cause de la résiliation.",
         "A non-refundable deposit of 20% of the price is payable on signature."]),
    "breach_notice": CatalogRisk(
        ("Termination for breach without formal notice", "Résiliation pour manquement sans mise en demeure"), "Medium",
        ("The contract can be ended for breach without first giving the other party formal notice to perform.",
         "Le contrat peut être résilié pour manquement sans mise en demeure préalable de l'autre partie."),
        ("Require formal notice with a period to cure before termination.", "Exiger une mise en demeure avec un délai pour remédier avant la résiliation."),
        ["En cas de manquement du Prestataire, le Client pourra résilier le contrat immédiatement et de plein droit, sans formalité.",
         "Le contrat sera résolu de plein droit en cas d'inexécution, sans qu'il soit besoin d'une mise en demeure.",
         "The Customer may terminate this Agreement immediately upon any breach by the Supplier."],
        ["En cas de manquement, le contrat pourra être résilié trente jours après une mise en demeure restée sans effet."]),
    "suspension": CatalogRisk(
        ("Suspension of performance", "Suspension de l'exécution"), "Low",
        ("One party may suspend performance; check it is tied to the other party's own failure to perform.",
         "Une partie peut suspendre l'exécution ; vérifiez que c'est lié à une inexécution de l'autre partie."),
        None,
        ["Le Prestataire pourra suspendre l'exécution des services à tout moment en cas de retard de paiement.",
         "The Supplier may suspend the services if any invoice remains unpaid."]),
    "works_guarantee": CatalogRisk(
        ("Works guarantee excluded", "Garantie de l'ouvrage exclue"), "High",
        ("A works contract that excludes the contractor's guarantees: the statutory guarantees cannot simply be removed.",
         "Un contrat d'ouvrage qui exclut les garanties de l'entrepreneur : les garanties légales ne peuvent pas simplement être écartées."),
        ("Remove the exclusion and state the guarantee periods.", "Supprimer l'exclusion et préciser les durées de garantie."),
        ["L'Entrepreneur ne donne aucune garantie sur les travaux réalisés après leur réception.",
         "Les ouvrages sont livrés en l'état, sans garantie d'aucune sorte de la part de l'Entrepreneur.",
         "The Contractor gives no warranty on the works after acceptance."]),
    "transport": CatalogRisk(
        ("Carrier's liability excluded", "Responsabilité du transporteur écartée"), "High",
        ("A transport contract with a clause excluding the carrier's liability.",
         "Un contrat de transport avec une clause écartant la responsabilité du transporteur."),
        ("Check the exclusion against the carrier's statutory liability.", "Confronter l'exclusion à la responsabilité légale du transporteur."),
        ["Le Transporteur décline toute responsabilité en cas de perte ou d'avarie des marchandises transportées.",
         "Le transporteur ne sera pas responsable des dommages subis par les marchandises pendant le transport.",
         "The Carrier shall not be liable for any loss of or damage to the goods during carriage."]),
    "electronic_signature": CatalogRisk(
        ("Electronic signature", "Signature électronique"), "Low",
        ("The contract relies on an electronic signature; check it is certified and how the signed document is kept.",
         "Le contrat prévoit une signature électronique ; vérifiez qu'elle est certifiée et comment le document signé est conservé."),
        ("Use a certified electronic signature and state how the signed version is archived.",
         "Utiliser une signature électronique certifiée et préciser l'archivage de la version signée."),
        ["Le présent contrat est signé électroniquement par les parties, qui reconnaissent la valeur de cette signature.",
         "Les parties conviennent que la signature électronique du contrat a la même valeur qu'une signature manuscrite.",
         "This Agreement may be signed electronically and such signature shall be binding."]),
    "commercial_agency": CatalogRisk(
        ("Commercial agency", "Agence commerciale"), "Medium",
        ("A commercial agency relationship; an open-ended agency cannot be ended without notice.",
         "Une relation d'agence commerciale ; une agence à durée indéterminée ne peut être rompue sans préavis."),
        ("State the notice period for termination.", "Prévoir le préavis de résiliation."),
        ["Le Mandant confie à l'Agent commercial la prospection et la négociation des ventes en son nom et pour son compte.",
         "L'Agent est chargé, à titre de profession habituelle, de négocier des contrats au nom du Mandant contre commission.",
         "The Principal appoints the Agent as its commercial agent to negotiate sales on its behalf."]),
    "electronic_payment": CatalogRisk(
        ("Electronic payment", "Paiement électronique"), "Low",
        ("Payment by electronic transfer: the issuer has information, security and record-keeping duties.",
         "Paiement par transfert électronique : l'émetteur a des obligations d'information, de sécurité et de conservation."),
        None,
        ["Le règlement s'effectue par paiement électronique au moyen d'une carte bancaire ou d'un portefeuille de monnaie électronique.",
         "Payments shall be made by electronic transfer or e-money."]),
    "resale_price": CatalogRisk(
        ("Imposed resale price", "Prix de revente imposé"), "High",
        ("The clause sets a minimum resale price or margin for the reseller, a prohibited practice; the clause is void by law.",
         "La clause fixe au revendeur un prix de revente ou une marge minimum, pratique interdite ; la clause est nulle de plein droit."),
        ("Replace the minimum price with a recommended or maximum resale price.",
         "Remplacer le prix minimum par un prix de revente conseillé ou maximum, en laissant le revendeur libre de fixer ses prix."),
        ["Le Distributeur s'engage à ne pas revendre les produits à un prix inférieur au prix public fixé par le Fournisseur.",
         "Le Revendeur appliquera une marge minimale de 25 % sur tous les produits.",
         "The Distributor shall not resell the products below the minimum resale price set by the Supplier."],
        ["Le Fournisseur communique un prix de revente conseillé ; le Distributeur reste libre de fixer ses prix."]),
    "market_sharing": CatalogRisk(
        ("Market sharing between competitors", "Répartition de marché entre concurrents"), "High",
        ("The clause shares markets, territories or customers between the parties, or has them set prices together.",
         "La clause répartit des marchés, des territoires ou des clients entre les parties, ou leur fait fixer leurs prix ensemble."),
        ("Remove the sharing or price-coordination commitment, or have it checked by competition counsel.",
         "Supprimer l'engagement de répartition ou de concertation sur les prix, ou le faire valider par un conseil en concurrence."),
        ["Les parties conviennent de se répartir les clients : la région du Nord est réservée à la société A, le Sud à la société B.",
         "Les parties s'engagent à ne pas se concurrencer et à aligner leurs prix de vente.",
         "The parties agree to allocate customers between them and not to compete with each other.",
         "Chacune des parties s'interdit de démarcher les clients situés sur le territoire réservé à l'autre partie.",
         "Le territoire du Nord est réservé à la première société et celui du Sud à la seconde."]),
    "termination": CatalogRisk(
        ("One-sided termination", "Résiliation unilatérale"), "High",
        ("Only one party may terminate; the other is bound for as long as the contract runs or renews.",
         "Une seule partie peut résilier ; l'autre reste engagée aussi longtemps que le contrat court ou se renouvelle."),
        ("Give both parties a termination right for breach and on notice at renewal.",
         "Ouvrir aux deux parties la résiliation pour manquement et à l'échéance, avec préavis."),
        ["Le Client ne dispose d'aucun droit de résiliation pendant toute la durée du contrat et de ses renouvellements.",
         "Seul le Prestataire pourra résilier le présent contrat ; le Client ne pourra en aucun cas y mettre fin.",
         "The Customer shall not be entitled to terminate this Agreement for any reason.",
         "Le Fournisseur peut mettre fin au contrat à tout moment, sans préavis ni indemnité, alors que le Client ne peut pas le résilier."],
        ["Chacune des parties pourra résilier le contrat moyennant un préavis de trois mois."]),
    "force_majeure": CatalogRisk(
        ("Extended force majeure", "Force majeure élargie"), "Low",
        ("The clause adds events to force majeure; check they meet the legal test (unforeseeable, unavoidable, making performance impossible).",
         "La clause ajoute des événements à la force majeure ; vérifiez qu'ils répondent à la définition légale (imprévisible, irrésistible, rendant l'exécution impossible)."),
        None,
        ["Sont considérés comme cas de force majeure les grèves, les pannes informatiques, les cyberattaques et les épidémies.",
         "Force majeure includes strikes, power failures, cyber attacks and epidemics."]),
}


# Ordinary, balanced clauses. Every risk treats them as counter-examples, so a standard clause is not read as a risk
# merely because it is about the same subject.
STANDARD_CLAUSES: list[str] = [
    "En cas de manquement grave, le contrat peut être résilié après une mise en demeure restée sans effet pendant trente jours.",
    "Chacune des parties peut résilier le contrat moyennant un préavis écrit de trois mois.",
    "Les factures sont payables par virement à trente jours à compter de leur réception.",
    "Le présent contrat a pour objet la fourniture de services par le Prestataire au Client.",
    "Le présent contrat est régi par le droit tunisien ; les tribunaux de Tunis sont compétents.",
    "Chaque partie s'engage à garder confidentielles les informations reçues de l'autre partie.",
    "Les parties s'efforceront de régler à l'amiable tout différend relatif au présent contrat.",
    "Les livrables et les droits de propriété intellectuelle restent la propriété du Prestataire, qui concède au Client une licence d'utilisation.",
    "Le Fournisseur confie au Distributeur la revente de ses produits auprès de sa clientèle.",
    "Contrat conclu entre la société Alpha et la société Beta pour la fourniture de produits en Tunisie.",
    "All intellectual property in the deliverables remains the property of the Supplier.",
    "Either party may terminate this Agreement for material breach not cured within thirty days of written notice.",
    "Invoices are payable within thirty days of receipt.",
]


def seed_clauses(key: str) -> tuple[list[str], list[str]]:
    """Every example and counter-example Harvey ships for a risk: copied once into the risk grid, where they can be
    read, removed or added to like any other."""
    from app.services.risk_catalog_more import MORE_COUNTER_EXAMPLES, MORE_EXAMPLES

    entry = CATALOG.get(key)
    if entry is None:
        return [], []
    return entry.examples + MORE_EXAMPLES.get(key, []), entry.counter_examples + MORE_COUNTER_EXAMPLES.get(key, [])


def seeded_grid() -> list[dict]:
    """The risk grid as Harvey seeds it, for tests and tools that run without a database."""
    grid = []
    for key, entry in CATALOG.items():
        examples, counters = seed_clauses(key)
        grid.append({"key": key, "label": entry.label[1], "phrases": [],
                     "examples": [{"text": t, "source": "harvey"} for t in examples],
                     "counter_examples": [{"text": t, "source": "harvey"} for t in counters]})
    return grid
