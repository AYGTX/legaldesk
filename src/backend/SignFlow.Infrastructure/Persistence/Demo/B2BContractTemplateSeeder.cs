using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Persistence.Demo;

/// <summary>
/// Seeds "Contrat B2B — Prestation de services", an HTML template assembled from the approved
/// French clauses of the clause library, with <c>{{#if}}</c> conditions so the clauses that end up
/// in a contract are driven by what the initiator ticks and enters.
///
/// The point is to let someone simulate the library: tick "données personnelles" and the data
/// protection clause appears; tick "responsabilité illimitée" and the capped liability clause is
/// swapped for the non-standard indemnification wording the AI review flags as high risk; raise the
/// contract value above the threshold and a performance guarantee article is added.
///
/// Clause bodies are read from the library at seed time rather than copied here, so the template
/// says exactly what the library says. HTML templates snapshot their content per request, so later
/// library edits only reach the template if it is re-saved — the same as any admin-built template.
/// </summary>
internal static class B2BContractTemplateSeeder
{
    public const string TemplateName = "Contrat B2B — Prestation de services";
    private const string RequestTypeName = "Contrat B2B";

    private record FieldSpec(
        string Key,
        string Label,
        HtmlTemplateFieldType Type,
        HtmlTemplateFieldRole Role = HtmlTemplateFieldRole.Initiator,
        bool Required = true,
        string? AutoFill = null,
        string? ApproverEmail = null);

    private static readonly FieldSpec[] Fields =
    [
        new("provider_company", "Prestataire (société)", HtmlTemplateFieldType.Text, AutoFill: "user.company"),
        new("provider_representative", "Représentant du prestataire", HtmlTemplateFieldType.Text, AutoFill: "user.fullName"),
        new("client_company", "Client (raison sociale)", HtmlTemplateFieldType.Text),
        new("client_registration", "Matricule fiscal du client", HtmlTemplateFieldType.Text),
        new("client_address", "Siège social du client", HtmlTemplateFieldType.Textarea),
        new("client_representative", "Représentant du client (nom et qualité)", HtmlTemplateFieldType.Text),
        new("contract_object", "Objet de la prestation", HtmlTemplateFieldType.Textarea),
        new("contract_value", "Montant total HT (TND)", HtmlTemplateFieldType.Number),
        new("start_date", "Date d'entrée en vigueur", HtmlTemplateFieldType.Date),
        new("duration_months", "Durée initiale (mois)", HtmlTemplateFieldType.Number),

        // The clause switches.
        new("auto_renewal", "Reconduction tacite", HtmlTemplateFieldType.Checkbox, Required: false),
        new("personal_data", "La prestation implique des données personnelles", HtmlTemplateFieldType.Checkbox, Required: false),
        new("ip_deliverables", "Des livrables spécifiques sont réalisés pour le client", HtmlTemplateFieldType.Checkbox, Required: false),
        new("uncapped_liability", "Accepter la responsabilité illimitée demandée par le client (non standard)", HtmlTemplateFieldType.Checkbox, Required: false),

        // Legal reviews and signs for the provider first, then the client signs.
        new("legal_signature", "Signature — Direction juridique (prestataire)", HtmlTemplateFieldType.Signature,
            HtmlTemplateFieldRole.Approver, ApproverEmail: "manager@legaldesk.tn"),
        new("client_signature", "Signature — Représentant du client", HtmlTemplateFieldType.Signature,
            HtmlTemplateFieldRole.Approver),
    ];

    /// <summary>The library clauses the template is built from, by title (French versions).</summary>
    private static class Clauses
    {
        public const string Confidentiality = "Confidentialité réciproque";
        public const string Liability = "Limitation de responsabilité (plafonnée)";
        public const string Termination = "Résiliation pour convenance";
        public const string DataProtection = "Protection des données à caractère personnel";
        public const string ForceMajeure = "Force majeure";
        public const string GoverningLaw = "Droit applicable et juridiction compétente";
        public const string Payment = "Conditions de paiement (30 jours)";
        public const string IntellectualProperty = "Propriété intellectuelle";
        public const string AutoRenewal = "Reconduction tacite";

        public static readonly string[] All =
            [Confidentiality, Liability, Termination, DataProtection, ForceMajeure, GoverningLaw, Payment, IntellectualProperty, AutoRenewal];
    }

    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // Idempotent by name: an existing database keeps whatever an admin changed in the editor.
        if (await context.HtmlTemplates.AnyAsync(t => t.Name == TemplateName, cancellationToken))
            return;

        var owner = await userManager.FindByEmailAsync("admin@legaldesk.tn");
        var requestType = await context.RequestTypes.FirstOrDefaultAsync(t => t.Name == RequestTypeName, cancellationToken);
        if (owner is null || requestType is null)
        {
            logger.LogWarning("B2B contract template skipped — admin user or '{Type}' request type not found", RequestTypeName);
            return;
        }

        var approvers = new Dictionary<string, Guid>();
        foreach (var email in Fields.Select(f => f.ApproverEmail).OfType<string>().Distinct())
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                logger.LogWarning("B2B contract template skipped — approver {Email} not found", email);
                return;
            }
            approvers[email] = user.Id;
        }

        var library = (await context.Clauses
                .Where(c => Clauses.All.Contains(c.Title))
                .SelectMany(c => c.Versions.Where(v => v.Language == "fr" && v.Status == ClauseStatus.Approved), (c, v) => new { c.Title, v.Body })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.Title).ToDictionary(g => g.Key, g => g.First().Body);
        var missing = Clauses.All.Where(t => !library.ContainsKey(t)).ToList();
        if (missing.Count > 0)
        {
            logger.LogWarning("B2B contract template skipped — library clauses not found: {Missing}", string.Join(", ", missing));
            return;
        }

        var template = HtmlTemplate.Create(
            TemplateName,
            BuildHtml(library),
            owner.Id,
            description: "Contrat de prestation de services entre entreprises, assemblé à partir des clauses approuvées de la bibliothèque. " +
                         "Les cases à cocher et le montant activent ou retirent des clauses : données personnelles, propriété intellectuelle, " +
                         "reconduction tacite, garantie de bonne exécution, responsabilité illimitée (non standard).",
            workflowType: WorkflowType.Sequential,
            requestTypeId: requestType.Id);
        template.SetCreatedBy(owner.Id.ToString());
        context.HtmlTemplates.Add(template);

        for (var i = 0; i < Fields.Length; i++)
        {
            var f = Fields[i];
            context.HtmlTemplateFields.Add(HtmlTemplateField.Create(
                template.Id, f.Key, f.Label, f.Type, f.Role,
                isRequired: f.Required,
                order: i + 1,
                autoFillFrom: f.AutoFill,
                preAssignedApproverId: f.ApproverEmail is null ? null : approvers[f.ApproverEmail]));
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded HTML template '{Name}' from {Count} library clauses", TemplateName, library.Count);
    }

    internal static string BuildHtml(IReadOnlyDictionary<string, string> library)
    {
        var html = new StringBuilder();

        html.Append("""
            <h1 style="text-align:center">Contrat de prestation de services</h1>
            <p style="text-align:center"><em>Contrat B2B</em></p>

            <h2>Entre les soussignés</h2>
            <p><strong>{{provider_company}}</strong>, représentée par {{provider_representative}}, ci-après dénommée « le Prestataire »,</p>
            <p>et</p>
            <p><strong>{{client_company}}</strong>, matricule fiscal {{client_registration}}, dont le siège social est situé {{client_address}},
            représentée par {{client_representative}}, ci-après dénommée « le Client »,</p>
            <p>ci-après dénommées ensemble « les Parties ».</p>

            <h2>Article 1 — Objet</h2>
            <p>Le présent contrat a pour objet de définir les conditions dans lesquelles le Prestataire réalise pour le Client la prestation suivante :</p>
            <p>{{contract_object}}</p>

            <h2>Article 2 — Prix</h2>
            <p>En contrepartie de la prestation, le Client verse au Prestataire la somme de <strong>{{contract_value}} TND hors taxes</strong>,
            majorée de la TVA au taux en vigueur.</p>

            """);

        html.Append("""
            {{#if contract_value >= 100000}}
            <h3>Garantie de bonne exécution</h3>
            <p>Le montant du contrat étant égal ou supérieur à 100 000 TND, le Prestataire remet au Client, dans les quinze (15) jours
            suivant l'entrée en vigueur, une garantie bancaire de bonne exécution à première demande égale à dix pour cent (10 %) du montant hors taxes,
            valable jusqu'à la réception définitive de la prestation.</p>
            {{/if}}

            """);

        html.Append($$$"""
            <h2>Article 3 — Conditions de paiement</h2>
            {{{Body(library, Clauses.Payment)}}}

            <h2>Article 4 — Durée</h2>
            {{#if auto_renewal}}
            <p>Le présent contrat entre en vigueur le {{start_date}}.</p>
            {{{Body(library, Clauses.AutoRenewal)}}}
            {{#else}}
            <p>Le présent contrat entre en vigueur le {{start_date}} pour une durée ferme de {{duration_months}} mois. Il prend fin de plein droit
            à l'échéance de ce terme, sans tacite reconduction ; toute prolongation fait l'objet d'un avenant écrit.</p>
            {{/if}}

            <h2>Article 5 — Responsabilité</h2>
            {{#if uncapped_liability}}
            <h3>Indemnisation</h3>
            <p>Le Prestataire s'engage à indemniser, défendre et garantir le Client contre toute réclamation, perte, dommage et dépense de quelque nature
            que ce soit résultant de l'exécution du présent contrat, sans limitation de montant ni de durée.</p>
            {{#else}}
            {{{Body(library, Clauses.Liability)}}}
            {{/if}}

            <h2>Article 6 — Confidentialité</h2>
            {{{Body(library, Clauses.Confidentiality)}}}

            <h2>Article 7 — Propriété intellectuelle</h2>
            {{#if ip_deliverables}}
            {{{Body(library, Clauses.IntellectualProperty)}}}
            {{#else}}
            <p>La prestation ne donne lieu à la réalisation d'aucun livrable spécifique. Chaque partie conserve l'intégralité de ses droits de
            propriété intellectuelle ; aucune cession ni licence n'est consentie au titre du présent contrat.</p>
            {{/if}}

            <h2>Article 8 — Données à caractère personnel</h2>
            {{#if personal_data}}
            {{{Body(library, Clauses.DataProtection)}}}
            {{#else}}
            <p>Les Parties déclarent que l'exécution du présent contrat n'implique aucun traitement de données à caractère personnel. Si tel devait
            être le cas, elles concluront préalablement un avenant précisant les conditions de ce traitement.</p>
            {{/if}}

            <h2>Article 9 — Force majeure</h2>
            {{{Body(library, Clauses.ForceMajeure)}}}

            <h2>Article 10 — Résiliation</h2>
            {{{Body(library, Clauses.Termination)}}}

            <h2>Article 11 — Droit applicable et litiges</h2>
            {{{Body(library, Clauses.GoverningLaw)}}}

            <p>Fait en deux exemplaires originaux, à Tunis.</p>

            <table style="width:100%;margin-top:24px">
              <tr>
                <td style="width:50%;vertical-align:top"><strong>Pour le Prestataire</strong><br>{{provider_representative}}<br>{{legal_signature}}</td>
                <td style="width:50%;vertical-align:top"><strong>Pour le Client</strong><br>{{client_representative}}<br>{{client_signature}}</td>
              </tr>
            </table>
            """);

        return html.ToString();
    }

    /// <summary>
    /// A library clause body without its own <c>&lt;h3&gt;</c> title — the template supplies the
    /// numbered article heading, so keeping the clause's heading would print the title twice.
    /// </summary>
    private static string Body(IReadOnlyDictionary<string, string> library, string title)
        => System.Text.RegularExpressions.Regex.Replace(library[title], @"^\s*<h3>.*?</h3>", string.Empty);
}
