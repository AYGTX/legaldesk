using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Persistence.Demo;

/// <summary>A participant in a seeded contract's validation circuit.</summary>
internal record DemoParticipant(
    string Name,
    string Email,
    int Order,
    ApprovalType ApprovalType,
    string RoleLabel,
    WorkflowStepStatus Status,
    string? DeclineReason = null);

/// <summary>
/// The lifecycle envelope, expressed relative to the day the seeder runs so the portfolio keeps
/// hitting every rung of the alert ladder no matter when the demo database is created.
/// </summary>
internal record DemoTerm(int StartDaysAgo, int EndInDays, bool AutoRenews, int? NoticePeriodDays);

internal record DemoObligation(int DueInDays, string Type, string? Rationale, bool Confirmed);

/// <summary>A stored risk analysis, shaped like the full model review rather than the keyword fallback.</summary>
internal record DemoRisk(int Score, IReadOnlyList<RiskFinding> Findings, ContractProfile Profile);

/// <summary>
/// The verdict to record against one tracked regulation. Keyed by the regulation's
/// <c>Reference</c> in <see cref="DemoContract.Compliance"/> so the same verdict lands on both the
/// English and French registry entries for a given law.
/// </summary>
internal record DemoComplianceVerdict(
    string Status,
    string? Severity,
    string Explanation,
    string? WhyItMatters = null,
    string? RecommendedAction = null);

internal record DemoContract(
    string Title,
    string Description,
    string RequestTypeName,
    string OwnerEmail,
    DocumentStatus Status,
    int CreatedDaysAgo,
    string Reference,
    IReadOnlyList<(string Heading, string Text)> Body,
    IReadOnlyList<DemoParticipant> Participants,
    int? CompletedDaysAgo = null,
    DemoTerm? Term = null,
    DemoObligation? Obligation = null,
    DemoRisk? Risk = null,
    IReadOnlyDictionary<string, DemoComplianceVerdict>? Compliance = null,
    bool EvaluateReadiness = false);

/// <summary>
/// The demo contract portfolio: what a legal desk's LegalDesk instance looks like a year in.
///
/// It is built to exercise every capability the product claims, on a database that has just been
/// created: all six contract families, every document status, and a term on each executed contract
/// that lands on a different rung of the expiry alert ladder (T90 / T30 / T7 / notice deadline
/// passed / expired), so the lifecycle report and the risk map have something to say immediately.
///
/// The contract text is real wording, and each stored analysis says about that wording what a
/// reviewer would actually say — the "unlimited liability" finding sits on the one contract whose
/// text really does accept unlimited liability. A seeded finding that contradicts the file it
/// points at is the kind of thing that gets noticed in the room.
/// </summary>
internal static class DemoContractCatalogue
{
    internal const string ManagerEmail = "manager@legaldesk.tn";
    internal const string EmployeeEmail = "employee@legaldesk.tn";

    // Registry references, used to attach compliance verdicts to the seeded regulatory texts.
    private const string RefDataProtectionTn = "Loi organique n° 2004-63 du 27 juillet 2004";
    private const string RefGdpr = "Regulation (EU) 2016/679 (GDPR)";
    private const string RefGdprFr = "Règlement (UE) 2016/679 (RGPD)";
    private const string RefConsumer = "Loi n° 92-117 du 7 décembre 1992";
    private const string RefTelecom = "Loi n° 2001-1 du 15 janvier 2001";
    private const string RefCommercial = "Code de commerce";

    public static IReadOnlyList<DemoContract> All =>
    [
        FrameworkAgreementOrange(),
        NdaSopra(),
        DcwiInterconnection(),
        B2CFibreSubscription(),
        AmendmentOrange(),
        DatacenterMaintenance(),
        SystemsIntegration(),
        DcwiColocation(),
        B2CRemoteSupport(),
        BankOutsourcingAtlas()
    ];

    // ── 1. Auto-renewing framework agreement whose notice window has already closed ──────────────
    // The scenario legal teams actually lose money on, and the reason the notice deadline is a
    // first-class concept in this product.
    private static DemoContract FrameworkAgreementOrange() => new(
        Title: "Contrat-cadre de services — Orange Business Tunisie",
        Description: "Contrat-cadre pluriannuel de fourniture de services managés. Reconduction tacite par périodes de douze mois.",
        RequestTypeName: "Contrat B2B",
        OwnerEmail: ManagerEmail,
        Status: DocumentStatus.Completed,
        CreatedDaysAgo: 400,
        CompletedDaysAgo: 392,
        Reference: "Réf. LD-2024-B2B-014 — Contrat-cadre",
        Body:
        [
            ("Article 1 — Objet",
             "Le présent contrat-cadre a pour objet de définir les conditions dans lesquelles le Prestataire fournit au Client des services managés d'infrastructure et de connectivité, ainsi que les prestations d'assistance associées. Les commandes particulières sont passées par bons de commande successifs qui se réfèrent au présent contrat-cadre et en reprennent les conditions."),
            ("Article 2 — Durée et reconduction",
             "Le présent contrat est conclu pour une durée initiale de trente-six (36) mois à compter de sa date de signature. Il se renouvelle ensuite par reconduction tacite pour des périodes successives de douze (12) mois, sauf dénonciation par l'une des parties notifiée par lettre recommandée avec accusé de réception au moins trente (30) jours avant l'échéance de la période en cours."),
            ("Article 3 — Conditions financières",
             "La rémunération du Prestataire est établie sur la base du bordereau de prix annexé. Les factures sont payables à quarante-cinq (45) jours fin de mois. Tout retard de paiement donne lieu, de plein droit, à l'application de pénalités calculées au taux d'intérêt légal majoré de deux points, sans qu'une mise en demeure préalable soit nécessaire."),
            ("Article 4 — Exclusivité",
             "Pendant toute la durée du présent contrat, le Client s'engage à confier au Prestataire l'intégralité de ses besoins en services managés d'infrastructure entrant dans le périmètre défini à l'article 1, à l'exclusion de tout autre prestataire."),
            ("Article 5 — Confidentialité",
             "Chaque partie s'engage à conserver confidentielles toutes les informations communiquées par l'autre partie dans le cadre du présent contrat et à ne pas les divulguer à un tiers sans l'accord écrit préalable de la partie émettrice. Cette obligation survit à la résiliation du contrat pendant une durée de cinq (5) ans."),
            ("Article 6 — Responsabilité",
             "Sauf en cas de faute lourde, de dol ou de dommage corporel, la responsabilité globale de chaque partie au titre du présent contrat ne saurait excéder le montant total des sommes versées au cours des douze (12) mois précédant le fait générateur. Aucune des parties ne saurait être tenue responsable des dommages indirects ou consécutifs."),
            ("Article 7 — Protection des données à caractère personnel",
             "Chaque partie traite les données à caractère personnel échangées au titre du présent contrat conformément à la loi organique n° 2004-63 du 27 juillet 2004 relative à la protection des données à caractère personnel et, lorsque le traitement entre dans son champ d'application territorial, au règlement (UE) 2016/679. Les parties mettent en œuvre les mesures techniques et organisationnelles appropriées et s'informent sans délai injustifié de toute violation de données."),
            ("Article 8 — Résiliation",
             "Chaque partie peut résilier le présent contrat en cas de manquement grave de l'autre partie à ses obligations, non réparé dans un délai de trente (30) jours à compter de la mise en demeure. La résiliation est sans effet sur les droits et obligations nés antérieurement à sa date d'effet."),
            ("Article 9 — Loi applicable et juridiction compétente",
             "Le présent contrat est régi par le droit tunisien. Tout différend relatif à sa formation, son exécution ou son interprétation relève de la compétence exclusive des tribunaux de Tunis.")
        ],
        Participants:
        [
            new("Sonia Ben Amor", ManagerEmail, 1, ApprovalType.Approval, "Direction juridique", WorkflowStepStatus.Completed),
            new("Karim Trabelsi", "k.trabelsi@orange-business.tn", 2, ApprovalType.Signature, "Signataire — Orange Business Tunisie", WorkflowStepStatus.Completed)
        ],
        Term: new DemoTerm(StartDaysAgo: 392, EndInDays: 12, AutoRenews: true, NoticePeriodDays: 30),
        Obligation: new DemoObligation(DueInDays: 12, "Renewal", null, Confirmed: true),
        Risk: new DemoRisk(
            Score: 42,
            Findings:
            [
                new RiskFinding(
                    "Reconduction tacite", "Medium",
                    "L'article 2 prévoit une reconduction tacite par périodes de douze mois, la dénonciation devant intervenir au plus tard trente jours avant l'échéance.",
                    FindingType: "Imbalance",
                    WhyItMatters: "Un préavis de trente jours sur un engagement reconduit pour douze mois laisse une fenêtre de sortie étroite : passée cette date, le Client est réengagé pour une année entière.",
                    RecommendedAction: "Porter le préavis de dénonciation à quatre-vingt-dix jours, ou inscrire l'échéance dans le suivi contractuel dès la signature.",
                    ContractReference: "Article 2"),
                new RiskFinding(
                    "Exclusivité", "Medium",
                    "L'article 4 impose une exclusivité totale sur le périmètre des services managés, sans contrepartie tarifaire ni clause de sortie propre.",
                    FindingType: "Imbalance",
                    WhyItMatters: "Une exclusivité sans engagement de volume ni de prix côté Prestataire prive le Client de toute mise en concurrence pendant la durée du contrat.",
                    RecommendedAction: "Assortir l'exclusivité d'un engagement de niveau de prix révisable, ou la limiter aux seules prestations effectivement commandées.",
                    ProposedLanguage: "L'exclusivité prévue au présent article est subordonnée au maintien par le Prestataire des conditions tarifaires figurant au bordereau de prix annexé. Elle cesse de plein droit en cas de révision tarifaire non acceptée par le Client.",
                    ContractReference: "Article 4"),
                new RiskFinding(
                    "Pénalités de retard", "Low",
                    "L'article 3 applique des pénalités de retard de plein droit, sans mise en demeure préalable.",
                    FindingType: "WeakClause",
                    WhyItMatters: "La clause est conforme aux usages commerciaux, mais son automaticité mérite d'être connue des équipes comptables.",
                    RecommendedAction: "Aucune action juridique requise ; signaler l'échéance de paiement à quarante-cinq jours au service financier.",
                    ContractReference: "Article 3")
            ],
            Profile: new ContractProfile(
                "Contrat-cadre de services", "Client / Orange Business Tunisie",
                "Fourniture de services managés d'infrastructure et de connectivité",
                "Tunisie", "Droit tunisien", "36 mois, reconductible tacitement par périodes de 12 mois")),
        Compliance: new Dictionary<string, DemoComplianceVerdict>
        {
            [RefDataProtectionTn] = new("Addressed", null,
                "L'article 7 vise expressément la loi organique n° 2004-63 et met à la charge des deux parties des mesures techniques et organisationnelles ainsi qu'une obligation de notification des violations."),
            [RefGdpr] = new("Unclear", null,
                "Le contrat renvoie au règlement (UE) 2016/679 « lorsque le traitement entre dans son champ d'application territorial », sans préciser si des personnes concernées situées dans l'UE sont visées ni prévoir d'accord de sous-traitance distinct.",
                WhyItMatters: "En l'absence d'accord de sous-traitance et de garanties de transfert, l'applicabilité du RGPD ne peut être écartée sur la seule foi de cette réserve.",
                RecommendedAction: "Documenter si des données de personnes situées dans l'UE sont traitées et, le cas échéant, annexer un accord de sous-traitance."),
            [RefCommercial] = new("Addressed", null,
                "L'article 3 fixe un délai de paiement déterminé et l'article 9 désigne le droit tunisien et les tribunaux de Tunis, conformément aux règles applicables entre commerçants.")
        },
        EvaluateReadiness: false);

    // ── 2. NDA expiring inside a week — the T7 rung ──────────────────────────────────────────────
    private static DemoContract NdaSopra() => new(
        Title: "Accord de confidentialité — Sopra Banking Software",
        Description: "NDA réciproque encadrant les échanges préalables à un projet d'intégration bancaire.",
        RequestTypeName: "NDA",
        OwnerEmail: ManagerEmail,
        Status: DocumentStatus.Completed,
        CreatedDaysAgo: 152,
        CompletedDaysAgo: 150,
        Reference: "Réf. LD-2025-NDA-031 — Accord de confidentialité réciproque",
        Body:
        [
            ("Article 1 — Objet",
             "Les parties souhaitent échanger des informations confidentielles dans le cadre de l'étude d'un projet d'intégration de plateforme bancaire. Le présent accord définit les conditions dans lesquelles ces informations sont communiquées, utilisées et protégées."),
            ("Article 2 — Informations confidentielles",
             "Constitue une information confidentielle toute information, de quelque nature et sur quelque support que ce soit, communiquée par une partie à l'autre à l'occasion des échanges visés à l'article 1, à l'exception des informations déjà publiques, déjà connues du destinataire sans obligation de confidentialité, ou développées de manière indépendante."),
            ("Article 3 — Engagement de confidentialité",
             "Chaque partie s'engage à conserver confidentielles les informations reçues, à ne les utiliser que pour les besoins du projet décrit à l'article 1, et à n'en donner accès qu'aux membres de son personnel ayant à en connaître, eux-mêmes tenus d'une obligation de confidentialité équivalente."),
            ("Article 4 — Durée",
             "Le présent accord prend effet à sa date de signature pour une durée de six (6) mois. L'obligation de confidentialité survit à son terme pendant une durée de cinq (5) ans."),
            ("Article 5 — Restitution",
             "À l'expiration du présent accord ou à première demande, chaque partie restitue ou détruit l'ensemble des supports contenant des informations confidentielles reçues de l'autre partie, et en atteste par écrit."),
            ("Article 6 — Absence d'engagement",
             "Le présent accord ne vaut ni promesse de contracter, ni engagement d'exclusivité, ni transfert de droit de propriété intellectuelle. Il peut être résilié par chaque partie moyennant un préavis écrit de quinze (15) jours."),
            ("Article 7 — Loi applicable",
             "Le présent accord est régi par le droit tunisien. Les parties s'efforceront de régler amiablement tout différend avant toute action contentieuse devant les tribunaux de Tunis.")
        ],
        Participants:
        [
            new("Sonia Ben Amor", ManagerEmail, 1, ApprovalType.Signature, "Signataire — Direction juridique", WorkflowStepStatus.Completed),
            new("Hélène Dubois", "h.dubois@soprabanking.example", 2, ApprovalType.Signature, "Signataire — Sopra Banking Software", WorkflowStepStatus.Completed)
        ],
        Term: new DemoTerm(StartDaysAgo: 150, EndInDays: 5, AutoRenews: false, NoticePeriodDays: null),
        Obligation: new DemoObligation(DueInDays: 5, "Expiry",
            "Durée de six mois relevée à l'article 4 ; l'obligation de confidentialité survit cinq ans au terme.", Confirmed: false),
        Risk: new DemoRisk(
            Score: 18,
            Findings:
            [
                new RiskFinding(
                    "Restitution des informations", "Low",
                    "L'article 5 prévoit la restitution ou la destruction des supports à l'expiration, mais ne fixe pas de délai pour l'attestation écrite qui l'accompagne.",
                    FindingType: "Ambiguity",
                    WhyItMatters: "Sans délai, l'obligation d'attester est difficile à faire respecter au terme de l'accord — c'est-à-dire précisément au moment où elle compte.",
                    RecommendedAction: "Fixer un délai de trente jours pour la restitution et l'attestation.",
                    ProposedLanguage: "La restitution ou la destruction, ainsi que l'attestation écrite qui l'accompagne, interviennent dans un délai de trente (30) jours à compter de l'expiration du présent accord ou de la demande.",
                    ContractReference: "Article 5")
            ],
            Profile: new ContractProfile(
                "Accord de confidentialité réciproque", "Client / Sopra Banking Software",
                "Échanges préalables à un projet d'intégration de plateforme bancaire",
                "Tunisie", "Droit tunisien", "6 mois, confidentialité survivant 5 ans")));

    // ── 3. In-flight DCWI contract with an open regulatory gap — the pre-flight review demo ──────
    private static DemoContract DcwiInterconnection() => new(
        Title: "Contrat d'interconnexion DCWI — Tunisie Telecom",
        Description: "Interconnexion des réseaux et acheminement du trafic voix et données. En cours de validation juridique.",
        RequestTypeName: "Contrat DCWI",
        OwnerEmail: ManagerEmail,
        Status: DocumentStatus.InProgress,
        CreatedDaysAgo: 9,
        Reference: "Réf. LD-2026-DCWI-007 — Convention d'interconnexion",
        Body:
        [
            ("Article 1 — Objet",
             "La présente convention a pour objet de définir les conditions techniques, financières et juridiques de l'interconnexion des réseaux des parties, ainsi que les modalités d'acheminement réciproque du trafic voix et données entre ces réseaux."),
            ("Article 2 — Points d'interconnexion",
             "L'interconnexion est réalisée aux points d'interconnexion figurant en annexe technique. Toute création, modification ou suppression d'un point d'interconnexion fait l'objet d'un avenant à la présente convention."),
            ("Article 3 — Conditions financières",
             "Les prestations d'interconnexion sont facturées sur la base des tarifs figurant au catalogue d'interconnexion annexé. Les factures sont payables à trente (30) jours date de facture. Les tarifs sont révisés annuellement d'un commun accord entre les parties."),
            ("Article 4 — Qualité de service",
             "Chaque partie s'engage à maintenir un taux de disponibilité mensuel des liens d'interconnexion au moins égal à 99,7 %. En cas de manquement constaté sur deux mois consécutifs, la partie défaillante présente un plan d'action correctif dans un délai de quinze (15) jours."),
            ("Article 5 — Confidentialité",
             "Les parties conservent confidentielles les informations techniques, commerciales et de trafic échangées au titre de la présente convention, et ne les utilisent que pour les besoins de son exécution."),
            ("Article 6 — Durée",
             "La présente convention est conclue pour une durée de vingt-quatre (24) mois à compter de sa date d'entrée en vigueur. Elle peut être dénoncée par chaque partie moyennant un préavis écrit de quatre-vingt-dix (90) jours."),
            ("Article 7 — Loi applicable",
             "La présente convention est régie par le droit tunisien. Tout différend relève de la compétence des tribunaux de Tunis, après tentative de règlement amiable.")
        ],
        Participants:
        [
            new("Sonia Ben Amor", ManagerEmail, 1, ApprovalType.Review, "Revue juridique", WorkflowStepStatus.Completed),
            new("Mohamed Gharbi", "m.gharbi@legaldesk.tn", 2, ApprovalType.Approval, "Conformité réglementaire", WorkflowStepStatus.InProgress),
            new("Leila Mansouri", "l.mansouri@tunisietelecom.example", 3, ApprovalType.Signature, "Signataire — Tunisie Telecom", WorkflowStepStatus.Pending)
        ],
        Risk: new DemoRisk(
            Score: 55,
            Findings:
            [
                new RiskFinding(
                    "Révision tarifaire", "High",
                    "L'article 3 subordonne la révision annuelle des tarifs à un « commun accord » sans mécanisme applicable en cas de désaccord.",
                    FindingType: "Ambiguity",
                    WhyItMatters: "Sur une convention de vingt-quatre mois, un désaccord tarifaire sans procédure de sortie ni recours à expertise bloque l'exécution sans que la partie lésée dispose d'un remède.",
                    RecommendedAction: "Prévoir un mécanisme de règlement du désaccord tarifaire : indexation de repli, recours à un tiers expert, ou faculté de résiliation ouverte.",
                    ProposedLanguage: "À défaut d'accord des parties sur la révision tarifaire dans un délai de soixante (60) jours à compter de la demande, les tarifs en vigueur sont reconduits et chaque partie peut résilier la présente convention moyennant un préavis de quatre-vingt-dix (90) jours.",
                    ContractReference: "Article 3"),
                new RiskFinding(
                    "Qualité de service", "Medium",
                    "L'article 4 fixe un engagement de disponibilité de 99,7 % mais ne l'assortit d'aucune conséquence financière : le seul remède prévu est un plan d'action correctif.",
                    FindingType: "WeakClause",
                    WhyItMatters: "Un engagement de niveau de service sans pénalité associée n'est pas dissuasif et se révèle difficile à opposer à la partie défaillante.",
                    RecommendedAction: "Assortir le seuil de disponibilité d'avoirs proportionnels au manquement constaté.",
                    ContractReference: "Article 4")
            ],
            Profile: new ContractProfile(
                "Convention d'interconnexion de réseaux", "Client / Tunisie Telecom",
                "Interconnexion des réseaux et acheminement réciproque du trafic voix et données",
                "Tunisie", "Droit tunisien", "24 mois, préavis de dénonciation de 90 jours")),
        Compliance: new Dictionary<string, DemoComplianceVerdict>
        {
            [RefTelecom] = new("Gap", "High",
                "La convention traite de la qualité de service et des points d'interconnexion, mais ne comporte aucune stipulation relative aux obligations de licence des parties ni au reporting auprès de l'autorité de régulation.",
                WhyItMatters: "Le code des télécommunications met ces obligations à la charge des opérateurs interconnectés ; leur absence du contrat laisse la répartition des responsabilités indéterminée en cas de contrôle.",
                RecommendedAction: "Ajouter un article rappelant les titres d'exploitation de chaque partie et désignant celle qui assure les déclarations auprès de l'autorité de régulation."),
            [RefDataProtectionTn] = new("Gap", "High",
                "La convention porte sur l'acheminement de trafic voix et données mais ne comporte aucune stipulation relative à la protection des données à caractère personnel : l'article 5 ne couvre que la confidentialité commerciale entre les parties.",
                WhyItMatters: "Les données de trafic permettent d'identifier les abonnés ; leur traitement relève de la loi organique n° 2004-63, dont les obligations ne sont ici réparties entre les parties par aucune clause.",
                RecommendedAction: "Insérer un article dédié désignant le responsable de traitement, les mesures de sécurité et la procédure de notification des violations."),
            [RefGdpr] = new("Unclear", null,
                "Rien dans la convention n'indique si du trafic à destination ou en provenance de l'Union européenne est acheminé, ni si des données de personnes situées dans l'UE sont traitées."),
            [RefCommercial] = new("Addressed", null,
                "L'article 3 fixe un délai de paiement déterminé et l'article 7 désigne le droit et la juridiction applicables entre commerçants.")
        },
        EvaluateReadiness: true);

    // ── 4. B2C subscription expiring inside 30 days — the T30 rung ──────────────────────────────
    private static DemoContract B2CFibreSubscription() => new(
        Title: "Contrat d'abonnement fibre résidentielle — offre Domicile 1 Gb/s",
        Description: "Contrat type de fourniture d'accès Internet à destination des clients particuliers.",
        RequestTypeName: "Contrat B2C",
        OwnerEmail: EmployeeEmail,
        Status: DocumentStatus.Completed,
        CreatedDaysAgo: 200,
        CompletedDaysAgo: 198,
        Reference: "Réf. LD-2025-B2C-206 — Conditions particulières d'abonnement",
        Body:
        [
            ("Article 1 — Objet",
             "Le présent contrat a pour objet la fourniture au Client, consommateur au sens de la loi n° 92-117 du 7 décembre 1992, d'un service d'accès à Internet à très haut débit par fibre optique, ainsi que la mise à disposition des équipements terminaux nécessaires."),
            ("Article 2 — Information précontractuelle",
             "Le Client reconnaît avoir reçu, préalablement à la conclusion du présent contrat et sur support durable, les caractéristiques essentielles du service, le prix total toutes taxes comprises, la durée de l'engagement, les modalités de résiliation et les conditions d'exercice du droit de rétractation."),
            ("Article 3 — Durée et reconduction",
             "Le présent contrat est conclu pour une durée d'engagement de douze (12) mois à compter de la date de mise en service. À l'issue de cette période, il se poursuit pour une durée indéterminée, chaque partie pouvant y mettre fin moyennant un préavis de quinze (15) jours."),
            ("Article 4 — Prix et facturation",
             "L'abonnement est facturé mensuellement au tarif figurant aux conditions particulières, toutes taxes comprises. Toute évolution tarifaire est notifiée au Client au moins un (1) mois avant son entrée en vigueur ; le Client peut alors résilier sans frais dans un délai de trente (30) jours."),
            ("Article 5 — Droit de rétractation",
             "Le Client dispose d'un délai de sept (7) jours à compter de la conclusion du contrat à distance pour exercer son droit de rétractation, sans avoir à motiver sa décision ni à supporter d'autres frais que ceux de renvoi des équipements."),
            ("Article 6 — Protection des données à caractère personnel",
             "Les données à caractère personnel du Client sont traitées conformément à la loi organique n° 2004-63 du 27 juillet 2004, aux seules fins de la fourniture du service, de la facturation et de l'assistance. Le Client dispose d'un droit d'accès, de rectification et d'opposition qu'il exerce auprès du service client."),
            ("Article 7 — Résiliation",
             "Le Client peut résilier le présent contrat à tout moment après la période d'engagement, par lettre recommandée ou depuis son espace client, moyennant un préavis de quinze (15) jours. La résiliation avant le terme de l'engagement donne lieu au paiement des mensualités restant dues."),
            ("Article 8 — Réclamations et loi applicable",
             "Toute réclamation est adressée au service client, qui y répond dans un délai de trente (30) jours. Le présent contrat est régi par le droit tunisien.")
        ],
        Participants:
        [
            new("Nizar Ayari", EmployeeEmail, 1, ApprovalType.Approval, "Conformité consommateur", WorkflowStepStatus.Completed),
            new("Amine Chaouch", "amine.chaouch@example.tn", 2, ApprovalType.Signature, "Client — particulier", WorkflowStepStatus.Completed)
        ],
        Term: new DemoTerm(StartDaysAgo: 198, EndInDays: 26, AutoRenews: true, NoticePeriodDays: 15),
        Obligation: new DemoObligation(DueInDays: 26, "Renewal", null, Confirmed: true),
        Risk: new DemoRisk(
            Score: 22,
            Findings:
            [
                new RiskFinding(
                    "Indemnité de résiliation anticipée", "Medium",
                    "L'article 7 met à la charge du consommateur l'intégralité des mensualités restant dues en cas de résiliation avant le terme de l'engagement.",
                    FindingType: "Imbalance",
                    WhyItMatters: "Une indemnité égale à la totalité des échéances restantes, sans plafond ni proratisation, est susceptible d'être analysée comme une clause abusive dans un contrat de consommation.",
                    RecommendedAction: "Plafonner l'indemnité ou la proratiser en fonction de la durée d'engagement restant à courir.",
                    ProposedLanguage: "En cas de résiliation à l'initiative du Client avant le terme de la période d'engagement, l'indemnité due est égale au quart des mensualités restant à courir, dans la limite de trois (3) mensualités.",
                    ContractReference: "Article 7")
            ],
            Profile: new ContractProfile(
                "Contrat d'abonnement de services de communications électroniques", "Opérateur / Client particulier",
                "Fourniture d'un accès Internet très haut débit par fibre optique",
                "Tunisie", "Droit tunisien", "12 mois d'engagement, puis durée indéterminée")),
        Compliance: new Dictionary<string, DemoComplianceVerdict>
        {
            [RefConsumer] = new("Addressed", null,
                "Le contrat comporte l'information précontractuelle sur support durable (article 2), le droit de rétractation (article 5), un préavis de résiliation encadré (article 7) et une procédure de réclamation (article 8)."),
            [RefDataProtectionTn] = new("Addressed", null,
                "L'article 6 identifie les finalités du traitement et rappelle les droits d'accès, de rectification et d'opposition du Client ainsi que leurs modalités d'exercice."),
            [RefGdpr] = new("Unclear", null,
                "Le contrat s'adresse à des consommateurs résidents ; rien n'indique si des personnes concernées situées dans l'UE sont susceptibles d'être visées."),
            [RefCommercial] = new("Unclear", null,
                "Le contrat est conclu avec un consommateur et non entre commerçants ; les règles visées n'ont pas vocation à s'y appliquer directement.")
        });

    // ── 5. Draft amendment — nothing analysed yet, so the pre-flight gate can be run live ────────
    private static DemoContract AmendmentOrange() => new(
        Title: "Avenant n° 2 au contrat-cadre Orange Business Tunisie",
        Description: "Extension du périmètre des services managés et révision du bordereau de prix. Brouillon en cours de rédaction.",
        RequestTypeName: "Avenant",
        OwnerEmail: ManagerEmail,
        Status: DocumentStatus.Draft,
        CreatedDaysAgo: 3,
        Reference: "Réf. LD-2026-AVN-002 — Avenant au contrat-cadre LD-2024-B2B-014",
        Body:
        [
            ("Préambule",
             "Les parties ont conclu le contrat-cadre de services référencé LD-2024-B2B-014 portant sur la fourniture de services managés d'infrastructure. Elles souhaitent en étendre le périmètre aux services de sauvegarde externalisée et réviser en conséquence le bordereau de prix annexé."),
            ("Article 1 — Objet de l'avenant",
             "Le présent avenant a pour objet d'étendre le périmètre défini à l'article 1 du contrat-cadre aux prestations de sauvegarde externalisée et de restauration, et de substituer au bordereau de prix initial le bordereau figurant en annexe du présent avenant."),
            ("Article 2 — Périmètre étendu",
             "Les prestations de sauvegarde externalisée comprennent la copie quotidienne des données désignées par le Client, leur conservation sur une durée de trente (30) jours glissants et leur restauration à première demande dans un délai de quatre (4) heures ouvrées."),
            ("Article 3 — Conditions financières révisées",
             "Le bordereau de prix annexé au présent avenant se substitue au bordereau initial à compter de sa date d'entrée en vigueur. Les autres conditions financières du contrat-cadre demeurent inchangées."),
            ("Article 4 — Entrée en vigueur",
             "Le présent avenant entre en vigueur à sa date de signature par les deux parties. Toutes les stipulations du contrat-cadre non modifiées par le présent avenant demeurent applicables.")
        ],
        Participants:
        [
            new("Sonia Ben Amor", ManagerEmail, 1, ApprovalType.Approval, "Direction juridique", WorkflowStepStatus.Pending),
            new("Karim Trabelsi", "k.trabelsi@orange-business.tn", 2, ApprovalType.Signature, "Signataire — Orange Business Tunisie", WorkflowStepStatus.Pending)
        ]);

    // ── 6. Supplier contract whose term has already lapsed — the Expired rung ────────────────────
    private static DemoContract DatacenterMaintenance() => new(
        Title: "Contrat de maintenance datacenter — Tunisie Networks",
        Description: "Maintenance préventive et corrective des installations techniques du datacenter. Terme échu.",
        RequestTypeName: "Contrat fournisseur",
        OwnerEmail: EmployeeEmail,
        Status: DocumentStatus.Completed,
        CreatedDaysAgo: 420,
        CompletedDaysAgo: 415,
        Reference: "Réf. LD-2024-FOU-088 — Contrat de maintenance",
        Body:
        [
            ("Article 1 — Objet",
             "Le présent contrat a pour objet la maintenance préventive et corrective des installations techniques du datacenter du Client : groupes électrogènes, onduleurs, climatisation de précision et système de détection incendie."),
            ("Article 2 — Prestations de maintenance préventive",
             "Le Prestataire réalise quatre (4) visites de maintenance préventive par an, selon le calendrier arrêté d'un commun accord en début de période. Chaque visite donne lieu à un rapport d'intervention remis au Client dans les cinq (5) jours ouvrés."),
            ("Article 3 — Maintenance corrective et délais d'intervention",
             "En cas d'incident, le Prestataire intervient sur site dans un délai de quatre (4) heures pour les incidents bloquants et de vingt-quatre (24) heures pour les incidents non bloquants, vingt-quatre heures sur vingt-quatre et sept jours sur sept."),
            ("Article 4 — Durée",
             "Le présent contrat est conclu pour une durée de vingt-quatre (24) mois à compter de sa date d'effet. Il ne fait l'objet d'aucune reconduction tacite : son renouvellement suppose la conclusion d'un nouveau contrat, la dénonciation devant intervenir soixante (60) jours avant le terme."),
            ("Article 5 — Conditions financières",
             "La redevance forfaitaire annuelle couvre les visites préventives et les interventions correctives, à l'exclusion des pièces de rechange facturées sur devis préalablement accepté par le Client."),
            ("Article 6 — Confidentialité et sécurité des accès",
             "Le personnel du Prestataire intervenant sur site est soumis aux règles de sécurité et de confidentialité du Client. La liste nominative des intervenants habilités est tenue à jour et communiquée au Client."),
            ("Article 7 — Résiliation",
             "Chaque partie peut résilier le présent contrat en cas de manquement grave non réparé dans les trente (30) jours suivant la mise en demeure. Le Client peut en outre y mettre fin de plein droit en cas de dépassement répété des délais d'intervention prévus à l'article 3."),
            ("Article 8 — Loi applicable",
             "Le présent contrat est régi par le droit tunisien et relève de la compétence des tribunaux de Tunis.")
        ],
        Participants:
        [
            new("Nizar Ayari", EmployeeEmail, 1, ApprovalType.Approval, "Validation achats", WorkflowStepStatus.Completed),
            new("Sonia Ben Amor", ManagerEmail, 2, ApprovalType.Approval, "Direction juridique", WorkflowStepStatus.Completed),
            new("Foued Belhaj", "f.belhaj@tunisienetworks.example", 3, ApprovalType.Signature, "Signataire — Tunisie Networks", WorkflowStepStatus.Completed)
        ],
        Term: new DemoTerm(StartDaysAgo: 410, EndInDays: -20, AutoRenews: false, NoticePeriodDays: 60),
        Obligation: new DemoObligation(DueInDays: -20, "Expiry", null, Confirmed: true),
        Risk: new DemoRisk(
            Score: 30,
            Findings:
            [
                new RiskFinding(
                    "Pièces de rechange", "Medium",
                    "L'article 5 exclut les pièces de rechange du forfait sans plafonner leur coût annuel ni encadrer les délais de remise des devis.",
                    FindingType: "WeakClause",
                    WhyItMatters: "Sur des installations critiques, un devis non plafonné et sans délai de remise transforme un contrat forfaitaire en dépense ouverte au moment même où l'urgence prive le Client de tout pouvoir de négociation.",
                    RecommendedAction: "Plafonner le coût annuel des pièces et imposer un délai de remise de devis en situation d'incident bloquant.",
                    ContractReference: "Article 5")
            ],
            Profile: new ContractProfile(
                "Contrat de maintenance d'installations techniques", "Client / Tunisie Networks",
                "Maintenance préventive et corrective des installations techniques du datacenter",
                "Tunisie", "Droit tunisien", "24 mois, sans reconduction tacite")));

    // ── 7. Contract sent back for changes on a blocking finding — the readiness gate demo ────────
    private static DemoContract SystemsIntegration() => new(
        Title: "Contrat de prestation d'intégration SI — Delta Consulting",
        Description: "Intégration du système d'information de facturation. Renvoyé pour modification par la direction juridique.",
        RequestTypeName: "Contrat B2B",
        OwnerEmail: EmployeeEmail,
        Status: DocumentStatus.ChangesRequested,
        CreatedDaysAgo: 20,
        Reference: "Réf. LD-2026-B2B-041 — Contrat de prestation",
        Body:
        [
            ("Article 1 — Objet",
             "Le présent contrat a pour objet la réalisation par le Prestataire des travaux d'intégration du système d'information de facturation du Client, comprenant la reprise des données existantes, le paramétrage et la recette fonctionnelle."),
            ("Article 2 — Modalités d'exécution",
             "Les travaux sont exécutés selon le planning figurant en annexe. Le Prestataire affecte à la prestation les profils désignés dans son offre et ne peut les remplacer qu'après information écrite du Client."),
            ("Article 3 — Recette",
             "La recette est prononcée par le Client dans un délai de quinze (15) jours suivant la mise à disposition de chaque lot. À défaut de réserve écrite dans ce délai, la recette est réputée acquise."),
            ("Article 4 — Conditions financières",
             "La prestation est rémunérée au forfait selon l'échéancier annexé. Le Prestataire peut suspendre l'exécution des travaux en cas d'impayé constaté après mise en demeure restée sans effet pendant quinze (15) jours."),
            ("Article 5 — Responsabilité",
             "Le Client assume une responsabilité illimitée au titre de tout dommage résultant de l'utilisation des livrables, et s'engage à garantir le Prestataire contre toute réclamation de tiers s'y rapportant. Le Prestataire détermine à sa seule discrétion l'étendue des corrections qu'il apporte aux livrables."),
            ("Article 6 — Propriété intellectuelle",
             "Les développements spécifiques réalisés dans le cadre du présent contrat sont cédés au Client au fur et à mesure de leur recette. Les composants préexistants du Prestataire font l'objet d'une licence d'utilisation non exclusive et non cessible."),
            ("Article 7 — Confidentialité",
             "Chaque partie conserve confidentielles les informations reçues de l'autre à l'occasion de l'exécution du présent contrat et ne les utilise que pour les besoins de celle-ci."),
            ("Article 8 — Loi applicable",
             "Le présent contrat est régi par le droit tunisien. Tout différend relève de la compétence des tribunaux de Tunis.")
        ],
        Participants:
        [
            new("Nizar Ayari", EmployeeEmail, 1, ApprovalType.Review, "Maîtrise d'ouvrage", WorkflowStepStatus.Completed),
            new("Sonia Ben Amor", ManagerEmail, 2, ApprovalType.Approval, "Direction juridique", WorkflowStepStatus.ChangesRequested,
                DeclineReason: "L'article 5 fait peser une responsabilité illimitée sur le Client et laisse au Prestataire la seule appréciation de ses corrections. À reprendre avant transmission au signataire."),
            new("Julien Marchand", "j.marchand@delta-consulting.example", 3, ApprovalType.Signature, "Signataire — Delta Consulting", WorkflowStepStatus.Pending)
        ],
        Risk: new DemoRisk(
            Score: 78,
            Findings:
            [
                new RiskFinding(
                    "Responsabilité illimitée", "Critical",
                    "L'article 5 met à la charge du Client une responsabilité illimitée pour tout dommage résultant de l'utilisation des livrables, assortie d'une obligation de garantie contre les réclamations de tiers, sans contrepartie ni plafond.",
                    FindingType: "Imbalance",
                    WhyItMatters: "L'exposition financière du Client n'est bornée ni en montant ni dans le temps, alors que la maîtrise technique des livrables appartient au Prestataire. C'est le déséquilibre le plus lourd que puisse comporter un contrat de prestation.",
                    RecommendedAction: "Remplacer la clause par un plafond de responsabilité réciproque assis sur les sommes versées au titre du contrat.",
                    ProposedLanguage: "Sauf en cas de faute lourde, de dol ou de dommage corporel, la responsabilité globale de chaque partie au titre du présent contrat ne saurait excéder le montant total des sommes versées au cours des douze (12) mois précédant le fait générateur. Aucune des parties ne saurait être tenue responsable des dommages indirects ou consécutifs.",
                    ContractReference: "Article 5"),
                new RiskFinding(
                    "Appréciation discrétionnaire", "High",
                    "L'article 5 laisse au Prestataire le soin de déterminer « à sa seule discrétion » l'étendue des corrections apportées aux livrables.",
                    FindingType: "Imbalance",
                    WhyItMatters: "Une obligation de correction dont le débiteur fixe seul l'étendue n'est pas une obligation : le Client ne dispose d'aucun critère objectif pour en exiger l'exécution.",
                    RecommendedAction: "Rattacher les corrections à la conformité aux spécifications annexées plutôt qu'à l'appréciation du Prestataire.",
                    ContractReference: "Article 5"),
                new RiskFinding(
                    "Recette tacite", "Medium",
                    "L'article 3 répute la recette acquise à défaut de réserve écrite dans les quinze jours suivant la mise à disposition de chaque lot.",
                    FindingType: "WeakClause",
                    WhyItMatters: "Un délai de quinze jours par lot suppose une disponibilité continue des équipes de recette ; à défaut, des livrables non conformes sont réputés acceptés.",
                    RecommendedAction: "Porter le délai à trente jours ou suspendre son cours pendant les périodes de fermeture convenues.",
                    ContractReference: "Article 3")
            ],
            Profile: new ContractProfile(
                "Contrat de prestation de services informatiques", "Client / Delta Consulting",
                "Intégration du système d'information de facturation, reprise de données et recette",
                "Tunisie", "Droit tunisien", "Durée liée à l'exécution du planning annexé")),
        EvaluateReadiness: true);

    // ── 8. Colocation contract still comfortably in force — the T90 rung ─────────────────────────
    private static DemoContract DcwiColocation() => new(
        Title: "Contrat d'hébergement et de colocation DCWI — Carthage Datacenter",
        Description: "Hébergement d'équipements en baies dédiées et fourniture d'énergie secourue.",
        RequestTypeName: "Contrat DCWI",
        OwnerEmail: ManagerEmail,
        Status: DocumentStatus.Completed,
        CreatedDaysAgo: 110,
        CompletedDaysAgo: 105,
        Reference: "Réf. LD-2025-DCWI-052 — Contrat d'hébergement",
        Body:
        [
            ("Article 1 — Objet",
             "Le présent contrat a pour objet la mise à disposition du Client d'emplacements en baies dédiées au sein du datacenter de Carthage, la fourniture de l'énergie secourue et de la climatisation associées, ainsi que les prestations d'accès et de surveillance."),
            ("Article 2 — Emplacements et capacité",
             "Les emplacements mis à disposition, leur puissance électrique allouée et leur capacité de refroidissement figurent en annexe technique. Toute demande de capacité supplémentaire fait l'objet d'un avenant."),
            ("Article 3 — Disponibilité et engagements de service",
             "Le Prestataire s'engage sur un taux de disponibilité annuel de l'alimentation électrique de 99,98 %. Le dépassement de ce seuil ouvre droit, sur demande du Client, à des avoirs calculés selon le barème figurant en annexe."),
            ("Article 4 — Accès aux installations",
             "L'accès aux emplacements est réservé aux personnes préalablement habilitées et figurant sur la liste nominative tenue à jour par le Client. Chaque accès est enregistré et conservé pendant douze (12) mois."),
            ("Article 5 — Durée et reconduction",
             "Le présent contrat est conclu pour une durée de trente-six (36) mois. Il se renouvelle par reconduction tacite pour des périodes de douze (12) mois, sauf dénonciation notifiée au moins soixante (60) jours avant l'échéance."),
            ("Article 6 — Conditions financières",
             "La redevance mensuelle couvre la mise à disposition des emplacements et la puissance électrique allouée. La consommation excédant la puissance allouée est facturée au réel selon le barème annexé."),
            ("Article 7 — Protection des données à caractère personnel",
             "Les données d'accès et de vidéosurveillance sont traitées conformément à la loi organique n° 2004-63 du 27 juillet 2004, aux seules fins de sécurité des installations, et conservées pendant la durée strictement nécessaire à cette finalité."),
            ("Article 8 — Résiliation et loi applicable",
             "Chaque partie peut résilier le contrat en cas de manquement grave non réparé dans les trente (30) jours suivant la mise en demeure. Le présent contrat est régi par le droit tunisien.")
        ],
        Participants:
        [
            new("Sonia Ben Amor", ManagerEmail, 1, ApprovalType.Approval, "Direction juridique", WorkflowStepStatus.Completed),
            new("Nizar Ayari", EmployeeEmail, 1, ApprovalType.Approval, "Validation achats", WorkflowStepStatus.Completed),
            new("Rania Ferchichi", "r.ferchichi@carthage-dc.example", 2, ApprovalType.Signature, "Signataire — Carthage Datacenter", WorkflowStepStatus.Completed)
        ],
        Term: new DemoTerm(StartDaysAgo: 105, EndInDays: 78, AutoRenews: true, NoticePeriodDays: 60),
        Obligation: new DemoObligation(DueInDays: 18, "Renewal review",
            "Échéance de dénonciation déduite de la durée de trente-six mois et du préavis de soixante jours relevés à l'article 5.", Confirmed: false),
        Risk: new DemoRisk(
            Score: 26,
            Findings:
            [
                new RiskFinding(
                    "Reconduction tacite", "Medium",
                    "L'article 5 prévoit une reconduction tacite par périodes de douze mois, sous réserve d'une dénonciation notifiée soixante jours avant l'échéance.",
                    FindingType: "Imbalance",
                    WhyItMatters: "Le préavis est plus confortable que sur un contrat-cadre classique, mais il court sur un engagement d'hébergement dont la sortie suppose une migration physique des équipements.",
                    RecommendedAction: "Inscrire l'échéance de dénonciation dans le suivi contractuel et anticiper le délai de migration au-delà des soixante jours de préavis.",
                    ContractReference: "Article 5"),
                new RiskFinding(
                    "Facturation de la consommation excédentaire", "Low",
                    "L'article 6 facture au réel la consommation dépassant la puissance allouée, sans seuil d'alerte préalable.",
                    FindingType: "WeakClause",
                    WhyItMatters: "Le Client découvre le dépassement à la facturation plutôt qu'au moment où il pourrait encore agir sur sa consommation.",
                    RecommendedAction: "Demander la mise en place d'une alerte au franchissement de 90 % de la puissance allouée.",
                    ContractReference: "Article 6")
            ],
            Profile: new ContractProfile(
                "Contrat d'hébergement et de colocation", "Client / Carthage Datacenter",
                "Mise à disposition d'emplacements en baies dédiées, énergie secourue et climatisation",
                "Tunisie", "Droit tunisien", "36 mois, reconductible tacitement par périodes de 12 mois")));

    // ── 9. Rejected contract — the outcome the reporting page has to be able to show ─────────────
    private static DemoContract B2CRemoteSupport() => new(
        Title: "Contrat d'assistance technique à distance — offre Sérénité",
        Description: "Offre d'assistance à distance pour clients particuliers. Refusé par la direction juridique.",
        RequestTypeName: "Contrat B2C",
        OwnerEmail: EmployeeEmail,
        Status: DocumentStatus.Rejected,
        CreatedDaysAgo: 45,
        Reference: "Réf. LD-2026-B2C-118 — Conditions générales d'assistance",
        Body:
        [
            ("Article 1 — Objet",
             "Le présent contrat a pour objet la fourniture au Client d'une prestation d'assistance technique à distance portant sur ses équipements informatiques et sa connexion Internet, dans les limites du forfait souscrit."),
            ("Article 2 — Modalités de l'assistance",
             "L'assistance est fournie par téléphone et par prise de contrôle à distance, du lundi au samedi de 8 h à 20 h. Le Client autorise expressément le technicien à accéder à son équipement pour la durée de l'intervention."),
            ("Article 3 — Durée et reconduction",
             "Le présent contrat est conclu pour une durée de douze (12) mois. Il est reconduit tacitement pour la même durée, sauf dénonciation adressée par lettre recommandée au plus tard trois (3) mois avant l'échéance."),
            ("Article 4 — Prix",
             "Le forfait est facturé mensuellement. Le Prestataire se réserve la faculté de modifier le tarif à tout moment, la modification étant réputée acceptée à défaut d'opposition du Client dans les huit (8) jours de sa notification."),
            ("Article 5 — Limitation de responsabilité",
             "Le Prestataire n'est tenu à aucune obligation de résultat. Sa responsabilité ne peut être engagée au titre d'une perte de données survenue pendant ou à la suite d'une intervention, quelle qu'en soit la cause."),
            ("Article 6 — Loi applicable",
             "Le présent contrat est régi par le droit tunisien.")
        ],
        Participants:
        [
            new("Nizar Ayari", EmployeeEmail, 1, ApprovalType.Review, "Rédaction — offre grand public", WorkflowStepStatus.Completed),
            new("Sonia Ben Amor", ManagerEmail, 2, ApprovalType.Approval, "Direction juridique", WorkflowStepStatus.Rejected,
                DeclineReason: "Trois clauses abusives au regard du droit de la consommation : préavis de dénonciation de trois mois, acceptation tacite d'une hausse tarifaire en huit jours, exonération totale au titre des pertes de données. Refus en l'état.")
        ],
        Risk: new DemoRisk(
            Score: 85,
            Findings:
            [
                new RiskFinding(
                    "Exonération de responsabilité", "Critical",
                    "L'article 5 exclut toute responsabilité du Prestataire au titre des pertes de données, « quelle qu'en soit la cause », y compris donc en cas de faute.",
                    FindingType: "Imbalance",
                    WhyItMatters: "Une exonération totale de responsabilité, y compris en cas de faute, stipulée dans un contrat de consommation, est de nature à être réputée non écrite comme clause abusive.",
                    RecommendedAction: "Limiter l'exonération aux cas de force majeure et de faute du Client, et prévoir une obligation de sauvegarde préalable à l'intervention.",
                    ProposedLanguage: "Le Prestataire procède, préalablement à toute intervention susceptible d'affecter les données du Client, à une sauvegarde de celles-ci. Sa responsabilité ne peut être écartée en cas de faute dans l'exécution de cette obligation.",
                    ContractReference: "Article 5"),
                new RiskFinding(
                    "Modification unilatérale du prix", "Critical",
                    "L'article 4 permet au Prestataire de modifier le tarif à tout moment, la hausse étant réputée acceptée à défaut d'opposition du Client dans les huit jours.",
                    FindingType: "Imbalance",
                    WhyItMatters: "Une modification unilatérale du prix assortie d'une acceptation tacite en huit jours, sans faculté de résiliation sans frais, est caractéristique d'une clause abusive en droit de la consommation.",
                    RecommendedAction: "Notifier toute évolution tarifaire un mois à l'avance et ouvrir au Client un droit de résiliation sans frais pendant trente jours.",
                    ContractReference: "Article 4"),
                new RiskFinding(
                    "Préavis de dénonciation", "High",
                    "L'article 3 impose un préavis de dénonciation de trois mois par lettre recommandée sur un engagement de douze mois reconduit tacitement.",
                    FindingType: "Imbalance",
                    WhyItMatters: "Un préavis de trois mois sur un contrat de consommation reconduit tacitement restreint fortement la faculté du consommateur de mettre fin à son engagement.",
                    RecommendedAction: "Ramener le préavis à un mois et ouvrir la dénonciation par tout moyen écrit, y compris depuis l'espace client.",
                    ContractReference: "Article 3")
            ],
            Profile: new ContractProfile(
                "Contrat d'assistance technique", "Prestataire / Client particulier",
                "Assistance technique à distance sur équipements informatiques et connexion Internet",
                "Tunisie", "Droit tunisien", "12 mois, reconductible tacitement")));

    /// <summary>
    /// French-language registry entries carry a translated <c>Reference</c> for the same law, so a
    /// verdict keyed on one has to be found under the other. Keeps a seeded analysis covering
    /// "every regulation exactly once", which is what the compliance mapper guarantees at runtime.
    /// </summary>
    public static string NormaliseReference(string reference) =>
        reference == RefGdprFr ? RefGdpr : reference;

    // ── 10. A bank outsourcing its core system — the bank client's demo, reviewed live ──────────
    // No stored analysis on purpose: the presenter runs Harvey's review in the room. Each article holds
    // one defect, and the review must find each on its clause (BankDemoContractTests, same wording):
    // outsourcing without audit or central-bank access, client data abroad without banking secrecy, an
    // uncapped indemnity, one-sided termination, a price change at will, and a reference to an article 12
    // that does not exist. Uppercase headings, as the reader needs them to name each clause.
    private static DemoContract BankOutsourcingAtlas() => new(
        Title: "Contrat d'exploitation — Banque de l'Atlas / Nuage Numérique SARL",
        Description: "Infogérance et hébergement du système bancaire central. En cours de revue juridique avant signature.",
        RequestTypeName: "Contrat B2B",
        OwnerEmail: ManagerEmail,
        Status: DocumentStatus.InProgress,
        CreatedDaysAgo: 3,
        Reference: "Réf. LD-2026-BQ-003",
        Body:
        [
            ("ARTICLE 1 - OBJET",
             "La Banque confie au Prestataire l'infogérance et l'hébergement de son système bancaire central, y compris la plateforme de banque en ligne et les applications de gestion des comptes de la clientèle. Le Prestataire assure l'exploitation, la maintenance et la sauvegarde de ces systèmes."),
            ("ARTICLE 2 - DURÉE ET RECONDUCTION",
             "Le présent Contrat est conclu pour une durée initiale de trois (3) ans. Il se renouvelle ensuite par reconduction tacite pour des périodes successives de trois (3) ans, sauf dénonciation notifiée au moins six (6) mois avant l'échéance."),
            ("ARTICLE 3 - PRIX",
             "Les prestations sont rémunérées selon le barème annexé. Le Prestataire peut modifier ses tarifs et le périmètre des prestations à sa seule discrétion et sans préavis."),
            ("ARTICLE 4 - DONNÉES DES CLIENTS",
             "Le Prestataire héberge les fichiers et les données personnelles des clients de la Banque sur ses serveurs situés en France et peut les communiquer à ses propres sous-traitants établis à l'étranger. Le Prestataire traite ces informations avec un soin raisonnable."),
            ("ARTICLE 5 - RESPONSABILITÉ",
             "La Banque indemnisera et garantira le Prestataire contre toutes réclamations, pertes et dépenses de toute nature liées aux prestations, sans limite de montant ou de durée."),
            ("ARTICLE 6 - RÉSILIATION",
             "Le Prestataire peut résilier le présent Contrat à tout moment, sans préavis ni indemnité. La Banque n'a aucun droit de résiliation."),
            ("ARTICLE 7 - LITIGES",
             "Les litiges seront résolus tel qu'indiqué à l'Article 12 du présent Contrat. Fait à Tunis, en deux (2) exemplaires originaux.")
        ],
        Participants:
        [
            new("Sonia Ben Amor", ManagerEmail, 1, ApprovalType.Review, "Revue juridique", WorkflowStepStatus.InProgress),
            new("Mohamed Gharbi", "m.gharbi@legaldesk.tn", 2, ApprovalType.Approval, "Conformité bancaire", WorkflowStepStatus.Pending),
            new("Rania Khelifi", "r.khelifi@nuage-numerique.example", 3, ApprovalType.Signature, "Signataire — Nuage Numérique SARL", WorkflowStepStatus.Pending)
        ]);
}
