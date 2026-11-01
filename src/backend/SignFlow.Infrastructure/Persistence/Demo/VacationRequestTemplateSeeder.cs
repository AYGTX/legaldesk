using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;

namespace SignFlow.Infrastructure.Persistence.Demo;

/// <summary>
/// Seeds a ready-to-use "Demande de congé" PDF template: a drawn A4 form plus its configured fields
/// (requester auto-fill, leave details, manager decision and signature, HR visa and signature).
///
/// The form and the field placements come from the same <see cref="Layout"/>, so every field sits
/// exactly on the box drawn for it. Layout rectangles are in PDF points (top-left origin); stored
/// field coordinates are PDF points from the top-left of the page, the model every client and the stamper share
/// (R-M3-1).
/// </summary>
internal static class VacationRequestTemplateSeeder
{
    public const string TemplateName = "Demande de congé";
    private const string RequestTypeName = "Demande de congé";

    private const double Margin = 50;
    private const double LabelX = Margin;
    private const double BoxX = 210;
    private const double FullBoxWidth = 595 - Margin - BoxX; // A4 width is 595 pt
    private const double RowHeight = 20;

    private record Section(string Title, double Y);

    private record FieldSpec(
        string Key,
        string Label,
        PdfFieldType Type,
        PdfFieldFillRole Role,
        double Y,
        double Width = FullBoxWidth,
        double Height = RowHeight,
        bool Required = true,
        string? AutoFill = null,
        string[]? Options = null,
        int Stage = 1,
        string? ApproverEmail = null);

    private static readonly Section[] Sections =
    [
        new("1. Demandeur", 112),
        new("2. Congé demandé", 254),
        new("3. Avis du responsable hiérarchique", 504),
        new("4. Visa des Ressources humaines", 676),
    ];

    private static readonly FieldSpec[] Layout =
    [
        // 1. Requester — auto-filled from the profile, never typed.
        new("employee_name", "Nom et prénom", PdfFieldType.Text, PdfFieldFillRole.System, 128, AutoFill: "user.fullName"),
        new("department", "Département", PdfFieldType.Text, PdfFieldFillRole.System, 156, AutoFill: "user.department", Required: false),
        new("email", "E-mail", PdfFieldType.Text, PdfFieldFillRole.System, 184, AutoFill: "user.email"),
        new("request_date", "Date de la demande", PdfFieldType.Date, PdfFieldFillRole.System, 212, Width: 140, AutoFill: "system.today"),

        // 2. Leave details — filled by the requester.
        new("leave_type", "Type de congé", PdfFieldType.Dropdown, PdfFieldFillRole.Requester, 270,
            Options: ["Congé annuel", "Congé maladie", "Congé sans solde", "Congé exceptionnel", "Récupération"]),
        new("start_date", "Date de début", PdfFieldType.Date, PdfFieldFillRole.Requester, 298, Width: 140),
        new("end_date", "Date de fin (incluse)", PdfFieldType.Date, PdfFieldFillRole.Requester, 326, Width: 140),
        new("days_count", "Nombre de jours ouvrés", PdfFieldType.Number, PdfFieldFillRole.Requester, 354, Width: 80),
        new("backup_person", "Remplaçant(e) pendant l'absence", PdfFieldType.Text, PdfFieldFillRole.Requester, 382, Required: false),
        new("reachable_at", "Joignable au (téléphone)", PdfFieldType.Text, PdfFieldFillRole.Requester, 410, Width: 180, Required: false),
        new("reason", "Motif / commentaire", PdfFieldType.Text, PdfFieldFillRole.Requester, 438, Height: 40, Required: false),

        // 3. Line manager — first approver.
        new("manager_decision", "Décision", PdfFieldType.Dropdown, PdfFieldFillRole.Approver, 520, Width: 180,
            Options: ["Accordé", "Refusé", "Report demandé"], Stage: 1),
        new("manager_comment", "Commentaire", PdfFieldType.Text, PdfFieldFillRole.Approver, 548, Height: 36, Required: false, Stage: 1),
        new("manager_signature", "Signature du responsable", PdfFieldType.Signature, PdfFieldFillRole.Approver, 594,
            Width: 220, Height: 60, Stage: 1, ApproverEmail: "manager@legaldesk.tn"),

        // 4. HR — second approver.
        new("hr_balance", "Solde restant après congé (jours)", PdfFieldType.Number, PdfFieldFillRole.Approver, 692, Width: 80, Stage: 2),
        new("hr_signature", "Visa et signature RH", PdfFieldType.Signature, PdfFieldFillRole.Approver, 720,
            Width: 220, Height: 60, Stage: 2, ApproverEmail: "admin@legaldesk.tn"),
    ];

    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IStorageService storage,
        IPdfService pdfService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // Idempotent by name: an existing database keeps whatever an admin changed in the designer.
        if (await context.PdfTemplates.AnyAsync(t => t.Name == TemplateName, cancellationToken))
            return;

        var owner = await userManager.FindByEmailAsync("admin@legaldesk.tn");
        if (owner is null)
        {
            logger.LogWarning("Vacation request template skipped — admin user not found");
            return;
        }

        var approvers = new Dictionary<string, Guid>();
        foreach (var email in Layout.Select(f => f.ApproverEmail).OfType<string>().Distinct())
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                logger.LogWarning("Vacation request template skipped — approver {Email} not found", email);
                return;
            }
            approvers[email] = user.Id;
        }

        var requestType = await context.RequestTypes.FirstOrDefaultAsync(t => t.Name == RequestTypeName, cancellationToken);
        if (requestType is null)
        {
            requestType = RequestType.Create(RequestTypeName,
                "Demande d'absence d'un collaborateur. Avis du responsable hiérarchique puis visa RH.",
                48, "beach_access", "#14b8a6");
            context.RequestTypes.Add(requestType);
        }

        using var pdf = RenderForm();
        const string fileName = "demande-de-conge.pdf";
        var storagePath = await storage.UploadAsync(pdf, fileName, "application/pdf", $"pdf-templates/{owner.Id}", cancellationToken);
        pdf.Position = 0;
        var pageCount = await pdfService.GetPageCountAsync(pdf, cancellationToken);

        var template = PdfTemplate.Create(
            TemplateName, storagePath, fileName, pdf.Length, pageCount, owner.Id,
            requestTypeId: requestType.Id,
            description: "Formulaire de demande de congé : le demandeur renseigne la période, le responsable hiérarchique donne son avis et signe, puis les RH visent.",
            workflowType: WorkflowType.Sequential);

        // Seeded wording is the organisation's reviewed starting set: approved version 1 (R-M6-5).

        template.ImportApproved();
        template.SetCreatedBy(owner.Id.ToString());
        context.PdfTemplates.Add(template);

        for (var i = 0; i < Layout.Length; i++)
        {
            var f = Layout[i];
            context.PdfTemplateFields.Add(PdfTemplateField.Create(
                template.Id, f.Key, f.Label, f.Type, f.Role,
                page: 1,
                x: (float)BoxX,
                y: (float)f.Y,
                width: (float)f.Width,
                height: (float)f.Height,
                isRequired: f.Required,
                order: i + 1,
                autoFillFrom: f.AutoFill,
                optionsJson: f.Options is null ? null : JsonSerializer.Serialize(f.Options),
                preAssignedApproverId: f.ApproverEmail is null ? null : approvers[f.ApproverEmail],
                workflowStage: f.Stage));
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded PDF template '{Name}' with {Count} fields", TemplateName, Layout.Length);
    }

    /// <summary>Draws the blank form: header, section titles, and a labelled box per field.</summary>
    internal static MemoryStream RenderForm()
    {
        using var document = new PdfDocument();
        document.Info.Title = TemplateName;

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;

        const string family = Services.Pdf.AppFontResolver.FamilyName;
        var titleFont = new XFont(family, 18, XFontStyleEx.Bold);
        var subtitleFont = new XFont(family, 9, XFontStyleEx.Italic);
        var sectionFont = new XFont(family, 11, XFontStyleEx.Bold);
        var labelFont = new XFont(family, 9.5, XFontStyleEx.Regular);
        var footerFont = new XFont(family, 7.5, XFontStyleEx.Italic);

        var accent = XColor.FromArgb(20, 184, 166);
        var boxPen = new XPen(XColor.FromArgb(160, 160, 160), 0.75);
        var boxFill = new XSolidBrush(XColor.FromArgb(250, 250, 250));

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(new XSolidBrush(accent), 0, 0, page.Width.Point, 8);
            gfx.DrawString("DEMANDE DE CONGÉ", titleFont, XBrushes.Black, new XPoint(Margin, 60));
            gfx.DrawString("Formulaire RH — à compléter par le demandeur, puis avis du responsable et visa RH.",
                subtitleFont, XBrushes.Gray, new XPoint(Margin, 78));

            foreach (var section in Sections)
            {
                gfx.DrawString(section.Title, sectionFont, new XSolidBrush(accent), new XPoint(Margin, section.Y));
                gfx.DrawLine(new XPen(accent, 0.75), Margin, section.Y + 4, page.Width.Point - Margin, section.Y + 4);
            }

            foreach (var f in Layout)
            {
                // Label baseline aligned with the first text line of its box.
                var labelText = f.Required && f.Role != PdfFieldFillRole.System ? $"{f.Label} *" : f.Label;
                gfx.DrawString(labelText, labelFont, XBrushes.Black, new XPoint(LabelX, f.Y + 13.5));
                gfx.DrawRectangle(boxPen, boxFill, BoxX, f.Y, f.Width, f.Height);
            }

            gfx.DrawString("* Champ obligatoire. Toute demande doit être déposée au moins 15 jours avant la date de début, sauf congé maladie.",
                footerFont, XBrushes.Gray, new XPoint(Margin, 812));
        }

        var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        stream.Position = 0;
        return stream;
    }
}
