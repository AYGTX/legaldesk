using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Features.SignatureFields;
using SignFlow.Application.Features.SignatureFields.Commands;
using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Infrastructure.Persistence;
using SignFlow.Infrastructure.Services.Pdf;
using SignFlow.IntegrationTests.Infrastructure;
using SignFlow.IntegrationTests.Stubs;
using TextOrientation = UglyToad.PdfPig.Content.TextOrientation;

namespace SignFlow.IntegrationTests.Scenarios;

/// <summary>
/// Rework spec M3: fields in PDF points from the top-left of the page as displayed (R-M3-1), stamped where they were
/// placed whatever the page's rotation (R-M3-2), a layout checked before it is saved (R-M3-3) and refused when it was
/// saved by someone else meanwhile (R-M3-4), and field detection for the contract's owner (R-M3-7).
/// </summary>
public class FieldDesignerTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static async Task<(ScenarioFixture Fixture, Document Document, Recipient Signer, Recipient Reviewer)> DraftAsync()
    {
        var fixture = new ScenarioFixture(OwnerId);
        var db = (ApplicationDbContext)fixture.Db;
        var document = Document.Create("Supply agreement", "supply.pdf", "documents/supply.pdf", 10, 2, OwnerId);
        db.Documents.Add(document);
        var signer = Recipient.Create(document.Id, "Alice", "alice@partner.com", 1, ApprovalType.Signature);
        var reviewer = Recipient.Create(document.Id, "Rita", "rita@partner.com", 2, ApprovalType.Review);
        db.Recipients.AddRange(signer, reviewer);
        await db.SaveChangesAsync();
        ((NullPdfService)fixture.GetHandler<IPdfService>()).Pages = [new PageSize(595, 842), new PageSize(842, 595)];
        return (fixture, document, signer, reviewer);
    }

    private static FieldDto Field(FieldType type, int page, float x, float y, Guid? recipient, string? options = null, float w = 100, float h = 30) =>
        new(null, type, page, x, y, w, h, null, recipient, type.ToString(), null, null, null, null, null, null, null, Options: options);

    private static Task<int> Save(ScenarioFixture f, Document d, int? version, params FieldDto[] fields) =>
        f.GetHandler<SaveFieldsCommandHandler>().HandleAsync(new SaveFieldsCommand(d.Id, [.. fields], version));

    [Fact]
    public async Task A_layout_is_checked_before_anything_is_saved()
    {
        var (fixture, document, signer, reviewer) = await DraftAsync();
        using var _ = fixture;
        var stranger = Guid.NewGuid();

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Save(fixture, document, 0,
            Field(FieldType.Signature, 3, 10, 10, signer.Id),                    // page 3 of 2
            Field(FieldType.Text, 1, 550, 10, signer.Id),                        // runs off the right of a 595 pt page
            Field(FieldType.Text, 2, 700, 500, signer.Id),                       // inside the landscape page 2: fine
            Field(FieldType.Text, 1, 10, 10, stranger),                          // not a participant of this contract
            Field(FieldType.Signature, 1, 10, 100, reviewer.Id),                 // a reviewer does not sign
            Field(FieldType.Dropdown, 1, 10, 200, signer.Id),                    // no options
            Field(FieldType.Image, 1, 10, 300, signer.Id)));                     // not supported any more (R-M3-5)

        var errors = refused.Errors["fields"];
        Assert.Equal(6, errors.Length);
        Assert.Contains(errors, e => e.Contains("page 3 does not exist"));
        Assert.Contains(errors, e => e.Contains("outside page 1"));
        Assert.Contains(errors, e => e.Contains("not a participant"));
        Assert.Contains(errors, e => e.Contains("Rita reviews or approves"));
        Assert.Contains(errors, e => e.Contains("needs its options"));
        Assert.Contains(errors, e => e.Contains("not supported"));
        Assert.Empty(await fixture.Db.SignatureFields.ToListAsync());
    }

    [Fact]
    public async Task An_empty_layout_is_valid_and_a_stale_one_is_refused()
    {
        var (fixture, document, signer, _) = await DraftAsync();
        using var _ = fixture;

        Assert.Equal(1, await Save(fixture, document, 0));                                              // approvals only
        Assert.Equal(2, await Save(fixture, document, 1, Field(FieldType.Signature, 1, 400, 780, signer.Id)));

        // Two owners opened version 2; the first saves, the second is told to reload.
        Assert.Equal(3, await Save(fixture, document, 2, Field(FieldType.Signature, 1, 400, 700, signer.Id)));
        var stale = await Assert.ThrowsAsync<ConflictException>(() => Save(fixture, document, 2, Field(FieldType.Signature, 1, 10, 10, signer.Id)));
        Assert.Equal("layout.stale", stale.Code);
        Assert.Equal(700, (await fixture.Db.SignatureFields.SingleAsync()).Y);
    }

    [Fact]
    public async Task The_owner_detects_fields_on_their_own_contract_as_an_employee()
    {
        var (fixture, document, _, _) = await DraftAsync();
        using var _ = fixture;
        ((FakeCurrentUserService)fixture.GetHandler<ICurrentUserService>()).RolesOverride = ["Employee"];

        var handler = new DetectContractFieldsQueryHandler(fixture.Db, fixture.GetHandler<IStorageService>(), fixture.Harvey,
            fixture.GetHandler<ICurrentUserService>());
        var proposal = await handler.HandleAsync(new DetectContractFieldsQuery(document.Id));
        Assert.NotNull(proposal);

        ((FakeCurrentUserService)fixture.GetHandler<ICurrentUserService>()).UserIdOverride = Guid.NewGuid();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.HandleAsync(new DetectContractFieldsQuery(document.Id)));
    }

    // ------------------------------------------------------------------ stamping on rotated pages (R-M3-2)

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task A_field_at_the_displayed_bottom_right_corner_lands_on_that_corner_whatever_the_page_rotation(int rotate)
    {
        PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new AppFontResolver();
        var storage = new InMemoryStorageService();
        const double W = 595.28, H = 841.89;   // A4 portrait, as stored

        using (var blank = new PdfSharp.Pdf.PdfDocument())
        {
            var page = blank.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            page.Rotate = rotate;
            var source = new MemoryStream();
            blank.Save(source, closeStream: false);
            await storage.UploadAsync(source, "page.pdf", "application/pdf", "docs");
        }
        var path = storage.Files.Keys.Single();

        // The displayed page is landscape when turned a quarter.
        var (wd, hd) = rotate is 90 or 270 ? (H, W) : (W, H);
        await using var stamped = await new PdfStampService(storage, NullLogger<PdfStampService>.Instance).StampAsync(path,
            [new StampFieldDto(FieldType.Text, 1, (float)(wd - 120), (float)(hd - 20), 120, 20, "CORNERMARK", null)]);

        stamped.Position = 0;
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(stamped);
        var letters = pdf.GetPage(1).Letters;
        Assert.Equal("CORNERMARK", string.Concat(letters.Select(l => l.Value)));

        // PdfPig reports letters on the page as displayed (it applies /Rotate; on a quarter-turned A4 page x runs to 842),
        // origin bottom-left: the displayed bottom-right corner is (displayed width, 0) whatever the rotation.
        var corner = (wd, 0.0);
        foreach (var letter in letters)
        {
            var (cx, cy) = (letter.GlyphRectangle.Centroid.X, letter.GlyphRectangle.Centroid.Y);
            var distance = Math.Sqrt(Math.Pow(cx - corner.Item1, 2) + Math.Pow(cy - corner.Item2, 2));
            Assert.True(distance < 125, $"'{letter.Value}' at ({cx:0}, {cy:0}) is {distance:0} pt from the corner {corner}");
        }

        // And it reads upright on the displayed page, left to right, on the bottom line.
        Assert.All(letters, l => Assert.Equal(TextOrientation.Horizontal, l.TextOrientation));
        Assert.True(letters[^1].GlyphRectangle.Centroid.X > letters[0].GlyphRectangle.Centroid.X);
        Assert.All(letters, l => Assert.InRange(l.GlyphRectangle.Centroid.Y, 0, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void The_box_of_a_field_in_the_displayed_corner_reaches_the_page_corner_within_a_point(int rotate)
    {
        const double W = 595.28, H = 841.89;
        var (wd, hd) = rotate is 90 or 270 ? (H, W) : (W, H);
        var p = PageGeometry.Place(rotate, W, H, wd - 120, hd - 20, 120, 20, 0);

        // Half the box, turned by the drawing angle, from the centre: the far corner of the box in the page's space.
        var a = p.Angle * Math.PI / 180;
        var (dx, dy) = (60 * Math.Cos(a) - 10 * Math.Sin(a), 60 * Math.Sin(a) + 10 * Math.Cos(a));
        var farCorner = (p.CenterX + dx, p.CenterY + dy);
        // In the drawing space (top-left origin): 0° (W, H); 90° (W, 0); 180° (0, 0); 270° (0, H).
        var expected = rotate switch { 90 => (W, 0.0), 180 => (0.0, 0.0), 270 => (0.0, H), _ => (W, H) };
        Assert.True(Math.Abs(farCorner.Item1 - expected.Item1) < 1 && Math.Abs(farCorner.Item2 - expected.Item2) < 1,
            $"far corner {farCorner} expected {expected}");
    }
}
