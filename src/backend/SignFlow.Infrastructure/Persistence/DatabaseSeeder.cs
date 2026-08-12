using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        // Roles
        string[] roles = ["Administrator", "Manager", "Employee", "ExternalSigner"];
        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        }

        // Admin user
        if (await userManager.FindByEmailAsync("admin@legaldesk.tn") is null)
        {
            var admin = new ApplicationUser
            {
                UserName = "admin@legaldesk.tn",
                Email = "admin@legaldesk.tn",
                FirstName = "System",
                LastName = "Administrator",
                Department = "Legal",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(admin, "Test123!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, "Administrator");
        }

        // Demo manager
        if (await userManager.FindByEmailAsync("manager@legaldesk.tn") is null)
        {
            var manager = new ApplicationUser
            {
                UserName = "manager@legaldesk.tn",
                Email = "manager@legaldesk.tn",
                FirstName = "Demo",
                LastName = "Manager",
                Company = "DCWI",
                Department = "Legal",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(manager, "Test123!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(manager, "Manager");
        }

        // Demo employee
        if (await userManager.FindByEmailAsync("employee@legaldesk.tn") is null)
        {
            var employee = new ApplicationUser
            {
                UserName = "employee@legaldesk.tn",
                Email = "employee@legaldesk.tn",
                FirstName = "Demo",
                LastName = "Employee",
                Company = "DCWI",
                Department = "Procurement",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(employee, "Test123!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(employee, "Employee");
        }

        await context.SaveChangesAsync();

        await BackfillDepartmentsAsync(context);
        await SeedRequestTypesAsync(context);
        await SeedClausesAsync(context, userManager);
        await SeedRegulatoryTextsAsync(context, userManager);
    }

    private static async Task BackfillDepartmentsAsync(ApplicationDbContext context)
    {
        var departmentsByEmail = new Dictionary<string, string>
        {
            ["admin@legaldesk.tn"] = "Legal",
            ["manager@legaldesk.tn"] = "Legal",
            ["employee@legaldesk.tn"] = "Procurement"
        };

        var usersMissingDepartment = await context.Users
            .Where(u => u.Department == null)
            .ToListAsync();

        foreach (var user in usersMissingDepartment)
        {
            user.Department = user.Email is not null && departmentsByEmail.TryGetValue(user.Email, out var dept)
                ? dept
                : "General";
        }

        if (usersMissingDepartment.Count > 0)
            await context.SaveChangesAsync();
    }

    private static async Task SeedRequestTypesAsync(ApplicationDbContext context)
    {
        // (Name, Description, SlaHours, Icon, Color)
        // Contract families, not HR requests: these drive the SLA clock and the
        // "by contract type" breakdown on the reports page.
        var defaults = new (string Name, string Description, int SlaHours, string Icon, string Color)[]
        {
            ("Contrat B2B", "Contrat commercial entre entreprises. Revue juridique obligatoire avant signature.", 168, "handshake", "#6366f1"),
            ("Contrat B2C", "Contrat de service destiné aux clients particuliers. Vérification de conformité consommateur.", 72, "person", "#22c55e"),
            ("Contrat DCWI", "Contrat lié à l'activité DCWI. Revue réglementaire télécoms requise.", 120, "cell_tower", "#0ea5e9"),
            ("NDA", "Accord de confidentialité. Circuit court, signature des deux parties.", 48, "lock", "#f59e0b"),
            ("Avenant", "Modification d'un contrat existant. Référence au contrat d'origine obligatoire.", 96, "edit_document", "#8b5cf6"),
            ("Contrat fournisseur", "Contrat d'achat ou de prestation. Validation achats puis juridique.", 168, "local_shipping", "#ef4444"),
        };

        var existing = await context.RequestTypes
            .Select(t => t.Name)
            .ToListAsync();

        var toAdd = defaults
            .Where(d => !existing.Contains(d.Name))
            .Select(d => RequestType.Create(d.Name, d.Description, d.SlaHours, d.Icon, d.Color))
            .ToList();

        if (toAdd.Count > 0)
        {
            context.RequestTypes.AddRange(toAdd);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds the shared clause library with standard contract wording in both working languages.
    /// Idempotent by (Title, Language) so an existing dev database keeps its curated edits.
    /// </summary>
    private static async Task SeedClausesAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        var author = await userManager.FindByEmailAsync("admin@legaldesk.tn");
        if (author is null) return;

        // (Title, Category, Language, RiskLevel, Body, Guidance, Approved)
        var defaults = new (string Title, ClauseCategory Category, string Language, string? RiskLevel, string Body, string Guidance, bool Approved)[]
        {
            ("Mutual confidentiality", ClauseCategory.Confidentiality, "en", "Low",
                "<h3>Confidentiality</h3><p>Each party shall keep confidential all information disclosed by the other party in connection with this Agreement, and shall not disclose it to any third party without the prior written consent of the disclosing party. This obligation survives termination of this Agreement for a period of five (5) years.</p>",
                "Standard mutual position. Use for most commercial contracts. If the counterparty requests a shorter survival period, three (3) years is the accepted fallback.",
                true),

            ("Confidentialité réciproque", ClauseCategory.Confidentiality, "fr", "Low",
                "<h3>Confidentialité</h3><p>Chaque partie s'engage à conserver confidentielles toutes les informations communiquées par l'autre partie dans le cadre du présent contrat et à ne pas les divulguer à un tiers sans l'accord écrit préalable de la partie émettrice. Cette obligation survit à la résiliation du contrat pendant une durée de cinq (5) ans.</p>",
                "Position réciproque standard. À utiliser pour la plupart des contrats commerciaux. Si le cocontractant demande une durée plus courte, trois (3) ans constitue le repli accepté.",
                true),

            ("Limitation of liability (capped)", ClauseCategory.Liability, "en", "Medium",
                "<h3>Limitation of Liability</h3><p>Save in cases of gross negligence, wilful misconduct or bodily injury, each party's aggregate liability arising out of or in connection with this Agreement shall not exceed the total amounts paid or payable under this Agreement during the twelve (12) months preceding the event giving rise to the claim. Neither party shall be liable for any indirect or consequential loss.</p>",
                "Preferred position: always cap liability. Never accept an uncapped liability clause without Legal sign-off — the AI review flags uncapped liability as high risk.",
                true),

            ("Limitation de responsabilité (plafonnée)", ClauseCategory.Liability, "fr", "Medium",
                "<h3>Limitation de responsabilité</h3><p>Sauf en cas de faute lourde, de dol ou de dommage corporel, la responsabilité globale de chaque partie au titre du présent contrat ne saurait excéder le montant total des sommes versées ou dues au titre du présent contrat au cours des douze (12) mois précédant le fait générateur. Aucune des parties ne saurait être tenue responsable des dommages indirects ou consécutifs.</p>",
                "Position privilégiée : toujours plafonner la responsabilité. Ne jamais accepter une responsabilité illimitée sans validation du service juridique — la revue IA signale la responsabilité illimitée comme risque élevé.",
                true),

            ("Termination for convenience", ClauseCategory.Termination, "en", "Low",
                "<h3>Termination</h3><p>Either party may terminate this Agreement at any time, without cause, by giving the other party sixty (60) days' prior written notice. Termination shall not affect any rights or obligations accrued prior to the effective date of termination.</p>",
                "Always ensure a contract contains an exit route. A contract with no termination clause is flagged as high risk by the AI review.",
                true),

            ("Résiliation pour convenance", ClauseCategory.Termination, "fr", "Low",
                "<h3>Résiliation</h3><p>Chaque partie peut résilier le présent contrat à tout moment, sans motif, moyennant un préavis écrit de soixante (60) jours adressé à l'autre partie. La résiliation est sans effet sur les droits et obligations nés antérieurement à sa date d'effet.</p>",
                "Veiller à ce que tout contrat comporte une porte de sortie. Un contrat dépourvu de clause de résiliation est signalé comme risque élevé par la revue IA.",
                true),

            ("Personal data protection", ClauseCategory.DataProtection, "en", "Medium",
                "<h3>Protection of Personal Data</h3><p>Each party undertakes to process personal data exchanged under this Agreement in accordance with Tunisian Organic Law No. 2004-63 of 27 July 2004 on the protection of personal data and, where the processing falls within its territorial scope, Regulation (EU) 2016/679 (GDPR). The parties shall implement appropriate technical and organisational measures and shall notify each other without undue delay of any personal data breach.</p>",
                "Mandatory in any contract involving personal data. Where the counterparty is an EU entity or data subjects are in the EU, add a separate data processing agreement.",
                true),

            ("Protection des données à caractère personnel", ClauseCategory.DataProtection, "fr", "Medium",
                "<h3>Protection des données à caractère personnel</h3><p>Chaque partie s'engage à traiter les données à caractère personnel échangées au titre du présent contrat conformément à la loi organique n° 2004-63 du 27 juillet 2004 relative à la protection des données à caractère personnel et, lorsque le traitement entre dans son champ d'application territorial, au règlement (UE) 2016/679 (RGPD). Les parties mettent en œuvre les mesures techniques et organisationnelles appropriées et s'informent mutuellement sans délai injustifié de toute violation de données.</p>",
                "Obligatoire dans tout contrat impliquant des données personnelles. Lorsque le cocontractant est une entité de l'UE ou que les personnes concernées s'y trouvent, ajouter un accord de sous-traitance distinct.",
                true),

            ("Force majeure", ClauseCategory.ForceMajeure, "en", "Low",
                "<h3>Force Majeure</h3><p>Neither party shall be liable for any failure or delay in performing its obligations where such failure or delay results from an event beyond its reasonable control, including natural disaster, war, civil unrest, epidemic, or an act of a public authority. The affected party shall notify the other within fifteen (15) days. If the event continues for more than ninety (90) days, either party may terminate this Agreement without indemnity.</p>",
                "Standard wording aligned with Article 283 of the Tunisian Code of Obligations and Contracts.",
                true),

            ("Force majeure", ClauseCategory.ForceMajeure, "fr", "Low",
                "<h3>Force majeure</h3><p>Aucune des parties ne saurait être tenue responsable d'un manquement ou d'un retard dans l'exécution de ses obligations résultant d'un événement échappant à son contrôle raisonnable, notamment catastrophe naturelle, guerre, troubles civils, épidémie ou fait du prince. La partie affectée en informe l'autre dans un délai de quinze (15) jours. Si l'événement se prolonge au-delà de quatre-vingt-dix (90) jours, chaque partie peut résilier le contrat sans indemnité.</p>",
                "Rédaction standard alignée sur l'article 283 du Code des obligations et des contrats tunisien.",
                true),

            ("Governing law and jurisdiction", ClauseCategory.GoverningLaw, "en", "Low",
                "<h3>Governing Law and Jurisdiction</h3><p>This Agreement is governed by and construed in accordance with Tunisian law. Any dispute arising out of or in connection with this Agreement which cannot be settled amicably within thirty (30) days shall be submitted to the exclusive jurisdiction of the Court of First Instance of Tunis.</p>",
                "Default position for domestic contracts. For cross-border contracts, check whether arbitration (CCAT or ICC) is preferable before departing from this wording.",
                true),

            ("Droit applicable et juridiction compétente", ClauseCategory.GoverningLaw, "fr", "Low",
                "<h3>Droit applicable et juridiction compétente</h3><p>Le présent contrat est régi par le droit tunisien et interprété conformément à celui-ci. Tout différend né du présent contrat ou s'y rapportant qui ne pourrait être réglé à l'amiable dans un délai de trente (30) jours sera soumis à la compétence exclusive du Tribunal de première instance de Tunis.</p>",
                "Position par défaut pour les contrats de droit interne. Pour les contrats transfrontaliers, vérifier si l'arbitrage (CCAT ou CCI) est préférable avant de s'écarter de cette rédaction.",
                true),

            ("Payment terms (30 days)", ClauseCategory.Payment, "en", "Low",
                "<h3>Payment Terms</h3><p>Invoices are payable within thirty (30) days of the invoice date. Any sum not paid when due shall bear late payment interest at the legal rate in force, applied automatically and without prior formal notice, together with a fixed recovery indemnity.</p>",
                "Align the payment period with the counterparty's contract type. Do not accept periods above sixty (60) days without Finance approval.",
                true),

            ("Conditions de paiement (30 jours)", ClauseCategory.Payment, "fr", "Low",
                "<h3>Conditions de paiement</h3><p>Les factures sont payables dans un délai de trente (30) jours à compter de leur date d'émission. Toute somme non réglée à l'échéance porte intérêts de retard au taux légal en vigueur, appliqués de plein droit et sans mise en demeure préalable, outre une indemnité forfaitaire de recouvrement.</p>",
                "Aligner le délai de paiement sur le type de contrat du cocontractant. Ne pas accepter de délai supérieur à soixante (60) jours sans l'accord de la direction financière.",
                true),

            ("Intellectual property ownership", ClauseCategory.IntellectualProperty, "en", "Medium",
                "<h3>Intellectual Property</h3><p>Each party retains ownership of the intellectual property rights it held prior to this Agreement. Any deliverable specifically created for the Client under this Agreement shall become the Client's property upon full payment of the corresponding price, excluding any pre-existing components which remain the property of the Provider and are licensed to the Client on a non-exclusive basis.</p>",
                "Watch for wording assigning background IP. Pre-existing components must always be carved out and licensed rather than assigned.",
                true),

            ("Propriété intellectuelle", ClauseCategory.IntellectualProperty, "fr", "Medium",
                "<h3>Propriété intellectuelle</h3><p>Chaque partie conserve la propriété des droits de propriété intellectuelle qu'elle détenait antérieurement au présent contrat. Tout livrable spécifiquement réalisé pour le Client au titre du présent contrat devient sa propriété au paiement intégral du prix correspondant, à l'exclusion des éléments préexistants qui demeurent la propriété du Prestataire et sont concédés au Client à titre non exclusif.</p>",
                "Attention aux rédactions cédant les droits antérieurs. Les éléments préexistants doivent toujours être exclus de la cession et faire l'objet d'une licence.",
                true),

            ("Automatic renewal", ClauseCategory.Compliance, "en", "High",
                "<h3>Term and Renewal</h3><p>This Agreement is concluded for an initial term of twelve (12) months and shall automatically renew for successive periods of the same duration unless terminated by either party by written notice given at least ninety (90) days before the end of the current term.</p>",
                "High risk: an auto-renewal that is missed silently extends the commitment. Always register the notice deadline as a tracked obligation on the contract so the renewal window is raised in advance.",
                true),

            ("Reconduction tacite", ClauseCategory.Compliance, "fr", "High",
                "<h3>Durée et reconduction</h3><p>Le présent contrat est conclu pour une durée initiale de douze (12) mois et se renouvelle par tacite reconduction pour des périodes successives de même durée, sauf dénonciation par l'une des parties notifiée par écrit au moins quatre-vingt-dix (90) jours avant le terme de la période en cours.</p>",
                "Risque élevé : une reconduction tacite non surveillée prolonge silencieusement l'engagement. Toujours enregistrer la date limite de dénonciation comme obligation suivie sur le contrat afin que la fenêtre de renouvellement soit signalée à l'avance.",
                true),

            ("Unlimited indemnification (non-standard)", ClauseCategory.Liability, "en", "High",
                "<h3>Indemnification</h3><p>The Provider shall indemnify, defend and hold harmless the Client against any and all claims, losses, damages and expenses of any nature arising out of the performance of this Agreement, without limitation as to amount or duration.</p>",
                "Non-standard wording kept in the library for reference only — this is the position counterparties sometimes propose. It is deliberately left unapproved: do not insert it without an explicit Legal decision.",
                false),
        };

        var existing = await context.Clauses
            .Select(c => new { c.Title, c.Language })
            .ToListAsync();

        var toAdd = new List<Clause>();
        foreach (var d in defaults)
        {
            if (existing.Any(e => e.Title == d.Title && e.Language == d.Language)) continue;

            var clause = Clause.Create(d.Title, d.Category, d.Body, author.Id, d.Language, d.Guidance, d.RiskLevel);
            if (d.Approved) clause.SetStatus(ClauseStatus.Approved);
            clause.SetCreatedBy(author.Id.ToString());
            toAdd.Add(clause);
        }

        if (toAdd.Count > 0)
        {
            context.Clauses.AddRange(toAdd);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds the regulatory watch registry with real Tunisian and EU texts relevant to the
    /// client's B2B/B2C/DCWI scope, all published so compliance analysis has signal immediately.
    /// Idempotent by (Title, Language) so an existing dev database keeps its curated edits.
    /// </summary>
    private static async Task SeedRegulatoryTextsAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        var author = await userManager.FindByEmailAsync("admin@legaldesk.tn");
        if (author is null) return;

        var dcwi = await context.RequestTypes.FirstOrDefaultAsync(t => t.Name == "Contrat DCWI");

        // (Title, Category, Jurisdiction, Language, Reference, Summary, Keywords, EffectiveDate, RequestTypeId)
        var defaults = new (string Title, RegulatoryCategory Category, RegulatoryJurisdiction Jurisdiction, string Language,
            string Reference, string Summary, string Keywords, DateTime EffectiveDate, Guid? RequestTypeId)[]
        {
            ("Personal data protection law", RegulatoryCategory.DataProtection, RegulatoryJurisdiction.Tunisia, "en",
                "Loi organique n° 2004-63 du 27 juillet 2004",
                "<p>Tunisia's organic law on the protection of personal data: requires a lawful basis and INPDP declaration or authorisation before processing personal data, and imposes security and breach-notification duties on data controllers.</p>",
                "personal data,inpdp,data protection,data controller,data subject,data breach",
                new DateTime(2004, 7, 27, 0, 0, 0, DateTimeKind.Utc), null),

            ("Loi sur la protection des données personnelles", RegulatoryCategory.DataProtection, RegulatoryJurisdiction.Tunisia, "fr",
                "Loi organique n° 2004-63 du 27 juillet 2004",
                "<p>Loi organique tunisienne relative à la protection des données à caractère personnel : impose une base légale et une déclaration ou autorisation INPDP avant tout traitement, ainsi que des obligations de sécurité et de notification des violations pour les responsables de traitement.</p>",
                "données personnelles,inpdp,protection des données,responsable de traitement,violation de données",
                new DateTime(2004, 7, 27, 0, 0, 0, DateTimeKind.Utc), null),

            ("EU General Data Protection Regulation", RegulatoryCategory.DataProtection, RegulatoryJurisdiction.EU, "en",
                "Regulation (EU) 2016/679 (GDPR)",
                "<p>Applies whenever a contract involves the personal data of individuals in the EU, regardless of where the processor is established. Requires a documented lawful basis, data-subject rights handling, and cross-border transfer safeguards.</p>",
                "gdpr,personal data,data subject,eu,cross-border transfer,data processing agreement",
                new DateTime(2018, 5, 25, 0, 0, 0, DateTimeKind.Utc), null),

            ("Règlement général sur la protection des données", RegulatoryCategory.DataProtection, RegulatoryJurisdiction.EU, "fr",
                "Règlement (UE) 2016/679 (RGPD)",
                "<p>S'applique dès qu'un contrat implique des données personnelles de personnes situées dans l'UE, quel que soit le lieu d'établissement du sous-traitant. Exige une base légale documentée, la gestion des droits des personnes concernées et des garanties pour les transferts hors UE.</p>",
                "rgpd,données personnelles,personne concernée,ue,transfert hors ue,accord de sous-traitance",
                new DateTime(2018, 5, 25, 0, 0, 0, DateTimeKind.Utc), null),

            ("Consumer protection code", RegulatoryCategory.Consumer, RegulatoryJurisdiction.Tunisia, "en",
                "Loi n° 92-117 du 7 décembre 1992",
                "<p>Governs contracts with individual consumers: mandatory pre-contractual information, a right to a clear and legible contract, and prohibition of unfair or abusive clauses.</p>",
                "consumer,unfair clause,pre-contractual information,withdrawal,b2c",
                new DateTime(1992, 12, 7, 0, 0, 0, DateTimeKind.Utc), null),

            ("Code de la protection du consommateur", RegulatoryCategory.Consumer, RegulatoryJurisdiction.Tunisia, "fr",
                "Loi n° 92-117 du 7 décembre 1992",
                "<p>Encadre les contrats conclus avec des consommateurs particuliers : information précontractuelle obligatoire, droit à un contrat clair et lisible, interdiction des clauses abusives.</p>",
                "consommateur,clause abusive,information précontractuelle,rétractation,b2c",
                new DateTime(1992, 12, 7, 0, 0, 0, DateTimeKind.Utc), null),

            ("Telecommunications code", RegulatoryCategory.Telecom, RegulatoryJurisdiction.Sectoral, "en",
                "Loi n° 2001-1 du 15 janvier 2001",
                "<p>Sectoral framework for telecommunications activity: licensing obligations, service-quality/SLA commitments toward subscribers, and INT regulatory-authority reporting duties applicable to DCWI contracts.</p>",
                "telecommunications,license,service quality,sla,regulatory authority,dcwi",
                new DateTime(2001, 1, 15, 0, 0, 0, DateTimeKind.Utc), dcwi?.Id),

            ("Code des télécommunications", RegulatoryCategory.Telecom, RegulatoryJurisdiction.Sectoral, "fr",
                "Loi n° 2001-1 du 15 janvier 2001",
                "<p>Cadre sectoriel de l'activité de télécommunications : obligations de licence, engagements de qualité de service envers les abonnés et obligations de reporting auprès de l'autorité de régulation, applicables aux contrats DCWI.</p>",
                "télécommunications,licence,qualité de service,autorité de régulation,dcwi",
                new DateTime(2001, 1, 15, 0, 0, 0, DateTimeKind.Utc), dcwi?.Id),

            ("Commercial code — obligations between traders", RegulatoryCategory.Commercial, RegulatoryJurisdiction.Tunisia, "en",
                "Code de commerce",
                "<p>Baseline rules for contracts between businesses (B2B): commercial registration requirements, payment-term defaults, and the presumption of solidarity between commercial co-debtors.</p>",
                "commercial,trader,b2b,payment term,commercial registration",
                new DateTime(1959, 1, 1, 0, 0, 0, DateTimeKind.Utc), null),

            ("Code de commerce — obligations entre commerçants", RegulatoryCategory.Commercial, RegulatoryJurisdiction.Tunisia, "fr",
                "Code de commerce",
                "<p>Règles de base des contrats entre entreprises (B2B) : obligations d'immatriculation au registre du commerce, délais de paiement par défaut, présomption de solidarité entre codébiteurs commerciaux.</p>",
                "commerçant,b2b,délai de paiement,registre du commerce,solidarité",
                new DateTime(1959, 1, 1, 0, 0, 0, DateTimeKind.Utc), null),
        };

        var existing = await context.RegulatoryTexts
            .Select(r => new { r.Title, r.Language })
            .ToListAsync();

        var toAdd = new List<RegulatoryText>();
        foreach (var d in defaults)
        {
            if (existing.Any(e => e.Title == d.Title && e.Language == d.Language)) continue;

            var text = RegulatoryText.Create(
                d.Title, d.Category, d.Jurisdiction, d.Summary, d.Keywords, author.Id,
                d.Language, d.Reference, d.EffectiveDate, d.RequestTypeId);
            text.SetStatus(RegulatoryTextStatus.Published);
            text.SetCreatedBy(author.Id.ToString());
            toAdd.Add(text);
        }

        if (toAdd.Count > 0)
        {
            context.RegulatoryTexts.AddRange(toAdd);
            await context.SaveChangesAsync();
        }
    }
}
