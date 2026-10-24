"""More example and counter-example clauses per risk, written for Harvey (not taken from real contracts). Added to the
catalogue's own, so each risk starts with about six examples and four counter-examples in the risk grid."""

MORE_EXAMPLES: dict[str, list[str]] = {
    "liability": [
        "Le Fournisseur supportera l'ensemble des conséquences financières de ses manquements, quel qu'en soit le montant.",
        "Aucune limitation de responsabilité ne s'applique au Prestataire au titre du présent contrat.",
    ],
    "indemnification": [
        "Le Prestataire prendra à sa charge toutes les sommes que le Client serait condamné à payer à un tiers, y compris les frais d'avocat.",
        "Le Client garantit le Fournisseur contre tout recours, quelle qu'en soit la cause, pendant toute la durée du contrat et au-delà.",
    ],
    "penalty": [
        "Le non-respect des niveaux de service entraîne l'application de pénalités de 500 dinars par incident, cumulables sans limite.",
        "Toute inexécution donnera lieu au paiement d'une indemnité forfaitaire égale à 20 % du prix total du contrat.",
    ],
    "auto_renewal": [
        "Faute de dénonciation par lettre recommandée, le contrat est prorogé de plein droit pour une nouvelle période d'égale durée.",
        "Le présent abonnement est reconduit chaque année automatiquement à sa date anniversaire.",
    ],
    "exclusivity": [
        "Pendant la durée du contrat, le Client s'interdit de confier des prestations similaires à tout autre prestataire.",
        "Le Fournisseur s'engage à ne pas vendre les mêmes produits à des concurrents du Distributeur sur le territoire.",
    ],
    "unilateral_change": [
        "Le Prestataire peut faire évoluer les conditions générales à tout moment ; la nouvelle version s'applique dès sa mise en ligne.",
        "Les tarifs pourront être révisés par le Fournisseur en cours de contrat sans l'accord du Client.",
        "La Banque se réserve le droit de modifier le contenu des prestations par simple notification au Prestataire.",
    ],
    "payment": [
        "Le Client renonce à réclamer des intérêts moratoires en cas de retard de paiement du Fournisseur.",
        "Le paiement des factures interviendra à 180 jours, sans pénalité de retard.",
        "Aucun frais de recouvrement ne pourra être réclamé au Client en cas de paiement tardif.",
    ],
    "deposit": [
        "Le Client verse à la commande un acompte de 40 % du prix, qui reste acquis au Fournisseur en cas d'annulation pour quelque cause que ce soit.",
        "En cas de résiliation, les sommes versées d'avance ne seront pas remboursées, même si la résiliation est due au Prestataire.",
        "Les arrhes versées par le Client ne pourront en aucun cas être restituées.",
    ],
    "breach_notice": [
        "Tout manquement du Prestataire autorise la Banque à mettre fin au contrat avec effet immédiat.",
        "Le contrat est résilié de plein droit dès la constatation d'un retard de paiement, sans formalité préalable.",
        "En cas de faute, la partie lésée peut résilier le contrat par simple notification, sans délai de régularisation.",
    ],
    "suspension": [
        "Le Fournisseur se réserve le droit d'interrompre le service à sa discrétion, sans préavis ni indemnité.",
        "En cas de désaccord sur une facture, le Prestataire peut suspendre ses prestations jusqu'au règlement intégral.",
        "Le Prestataire pourra suspendre l'accès au logiciel pour tout motif qu'il jugera légitime.",
        "The Provider may suspend the Services at any time for any reason.",
    ],
    "works_guarantee": [
        "La réception des travaux vaut acceptation définitive et décharge l'Entrepreneur de toute garantie.",
        "L'Entrepreneur ne répond pas des vices cachés affectant l'ouvrage, même apparus dans l'année suivant la réception.",
        "Aucune réclamation relative aux malfaçons ne sera recevable après la remise des clés.",
    ],
    "transport": [
        "Les marchandises voyagent aux risques exclusifs de l'expéditeur, le transporteur étant dégagé de toute responsabilité.",
        "L'indemnité due par le transporteur en cas de perte est limitée à un dinar par kilogramme, quelle que soit la faute.",
        "Le transporteur n'est pas tenu des retards de livraison, quelle qu'en soit la durée.",
    ],
    "electronic_signature": [
        "Les parties acceptent que le contrat soit conclu par voie électronique au moyen d'un simple clic d'acceptation.",
        "La signature scannée apposée sur le contrat vaut signature originale entre les parties.",
        "Le contrat est réputé signé dès l'envoi d'un courriel d'acceptation par le Client.",
    ],
    "commercial_agency": [
        "L'Agent prospecte la clientèle et conclut les ventes au nom du Fabricant, moyennant une commission de 5 % sur le chiffre d'affaires.",
        "Le Mandant pourra mettre fin au mandat de l'Agent à tout moment, sans préavis ni indemnité.",
        "L'agent commercial représente la société pour la vente de ses produits sur tout le territoire tunisien.",
    ],
    "electronic_payment": [
        "Les paiements sont effectués exclusivement par carte bancaire via la plateforme en ligne du Prestataire.",
        "Le Client règle ses factures par prélèvement électronique sur son compte de paiement.",
        "Le Prestataire accepte les règlements par monnaie électronique et portefeuille mobile.",
        "Le Client autorise des débits récurrents sur sa carte bancaire pour chaque échéance.",
    ],
    "resale_price": [
        "Les prix de vente aux consommateurs ne pourront être inférieurs à ceux figurant sur la liste publiée par le Fabricant.",
        "Le Distributeur s'interdit toute remise ou promotion faisant baisser le prix en dessous du prix catalogue.",
        "Le Revendeur s'engage à respecter les prix minimums de vente communiqués chaque trimestre par le Fournisseur.",
    ],
    "market_sharing": [
        "Chaque partie s'engage à ne pas répondre aux appels d'offres lancés dans la zone de l'autre partie.",
        "Les parties fixent ensemble les prix de vente pratiqués auprès de leurs clients respectifs.",
        "Les parties se concerteront avant toute modification de leurs tarifs.",
    ],
    "termination": [
        "Le contrat ne peut être résilié par le Client avant le terme de chaque période de renouvellement.",
        "Le Prestataire est seul habilité à mettre fin au présent accord.",
        "Le Client ne pourra pas résilier le contrat, même en cas de manquement du Prestataire.",
    ],
    "force_majeure": [
        "Constituent des cas de force majeure les pannes de fournisseurs, les difficultés d'approvisionnement et la hausse des prix des matières premières.",
        "Toute défaillance d'un sous-traitant du Prestataire est assimilée à un cas de force majeure.",
        "Le Fournisseur est exonéré en cas de perturbation de ses systèmes informatiques ou de ses réseaux.",
        "Les retards dus aux grèves internes du personnel du Prestataire sont considérés comme des cas de force majeure.",
    ],
}

MORE_COUNTER_EXAMPLES: dict[str, list[str]] = {
    "liability": [
        "La responsabilité du Prestataire est limitée aux dommages directs et ne peut excéder le montant annuel du contrat.",
    ],
    "indemnification": [
        "Le Prestataire garantit le Client contre les réclamations de tiers résultant de sa propre faute, dans la limite du prix annuel.",
        "La garantie due par chaque partie est limitée aux dommages directs qu'elle a causés et à une durée de deux ans.",
        "Each party indemnifies the other only for losses caused by its own breach, up to the annual fees.",
    ],
    "penalty": [
        "Les pénalités de retard sont dues après mise en demeure et ne peuvent dépasser 5 % du montant de la commande.",
        "Le montant cumulé des pénalités est plafonné à 10 % du prix ; au-delà, le Client peut résilier le contrat.",
        "Penalties are capped at 10% of the annual contract value.",
    ],
    "auto_renewal": [
        "Le contrat expire à son terme ; toute poursuite des relations nécessite un nouvel accord écrit.",
        "À l'échéance, les parties se rencontreront pour décider ensemble d'un éventuel renouvellement.",
    ],
    "exclusivity": [
        "Le Client reste libre de faire appel à d'autres prestataires pour des services similaires.",
    ],
    "unilateral_change": [
        "Toute modification des prix ou des prestations doit être acceptée par écrit par les deux parties.",
        "Le Prestataire peut proposer des évolutions ; elles ne s'appliquent qu'après signature d'un avenant.",
        "Le prix est ferme et non révisable pendant toute la durée du contrat.",
    ],
    "payment": [
        "Les factures sont payables à 30 jours ; tout retard donne lieu à des intérêts au taux légal.",
        "En cas de retard de paiement, des pénalités et une indemnité forfaitaire de recouvrement sont dues.",
        "Late payments bear interest at the statutory rate.",
    ],
    "deposit": [
        "L'acompte versé s'impute sur le prix et est restitué en cas de résiliation imputable au Prestataire.",
        "Les arrhes sont restituées au double si le Vendeur renonce à la vente.",
        "The deposit is credited against the final invoice and refunded if the Supplier cancels.",
    ],
    "breach_notice": [
        "En cas de manquement, la partie lésée adresse une mise en demeure ; le contrat est résilié si le manquement persiste trente jours.",
        "La résiliation pour faute n'intervient qu'après une mise en demeure restée sans effet pendant quinze jours.",
    ],
    "suspension": [
        "Le Prestataire peut suspendre ses prestations en cas de non-paiement persistant quinze jours après une mise en demeure.",
        "La suspension n'est possible qu'en cas d'inexécution de l'autre partie, après notification écrite.",
        "Le service ne peut être interrompu que pour maintenance planifiée, annoncée sept jours à l'avance.",
    ],
    "works_guarantee": [
        "L'Entrepreneur reste tenu des garanties légales après la réception des travaux.",
        "La garantie de parfait achèvement court pendant un an à compter de la réception.",
        "L'Entrepreneur garantit l'ouvrage contre les vices de construction pendant dix ans.",
    ],
    "transport": [
        "Le transporteur répond de la perte et des avaries de la marchandise depuis sa prise en charge jusqu'à la livraison.",
        "Le transporteur est responsable des retards de livraison qui lui sont imputables.",
        "Les marchandises sont assurées par le transporteur pendant toute la durée du transport.",
    ],
    "electronic_signature": [
        "Le contrat est signé au moyen d'une signature électronique certifiée délivrée par un prestataire agréé.",
        "Le contrat est établi en deux exemplaires originaux signés à la main par les représentants des parties.",
        "La version signée électroniquement est archivée de façon sécurisée pendant dix ans.",
    ],
    "commercial_agency": [
        "Le Distributeur achète les produits pour son propre compte et les revend en son nom.",
        "Le Prestataire exécute les services de façon indépendante et ne représente pas le Client auprès des tiers.",
        "Le contrat d'agence peut être résilié moyennant un préavis de six mois et le versement de l'indemnité due à l'agent.",
    ],
    "electronic_payment": [
        "Les factures sont réglées par chèque ou par virement bancaire classique.",
        "Le paiement intervient en espèces à la livraison, contre reçu.",
        "Le prix est payable par lettre de change à 60 jours.",
    ],
    "resale_price": [
        "Le Revendeur détermine librement ses prix de revente.",
        "Le Fabricant peut fixer un prix de revente maximum, que le Distributeur peut pratiquer à la baisse.",
    ],
    "market_sharing": [
        "Chaque partie fixe ses prix et choisit ses clients de façon indépendante.",
        "Les parties coopèrent uniquement pour la logistique, sans échanger d'informations sur leurs prix ou leurs clients.",
        "Le Distributeur reçoit un territoire exclusif pour la revente des produits du Fournisseur.",
    ],
    "termination": [
        "Le Client peut résilier le contrat à l'échéance moyennant un préavis de deux mois.",
        "En cas de manquement grave de l'une des parties, l'autre peut résilier après mise en demeure.",
    ],
    "force_majeure": [
        "La force majeure s'entend de tout événement imprévisible, irrésistible et extérieur aux parties rendant l'exécution impossible.",
        "La partie empêchée notifie l'événement sans délai ; si l'empêchement dure plus de trois mois, chaque partie peut résilier.",
        "Les grèves internes au Prestataire et les pannes de ses fournisseurs ne constituent pas des cas de force majeure.",
    ],
}
