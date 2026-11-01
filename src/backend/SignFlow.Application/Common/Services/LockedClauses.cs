using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Domain.Enums;

namespace SignFlow.Application.Common.Services;

/// <summary>A library clause a contract carries, at the version it carries (R-M6-7).</summary>
public record UsedClause(Guid ClauseId, string Title, int Version);

/// <summary>
/// Library clauses inside a template (R-M6-7): inserted as a block that names the clause and its version, and locked —
/// the block must keep that version's approved wording word for word. A drafter who wants other wording drafts a new
/// version of the clause, which a lawyer approves; they do not edit it in the template.
/// </summary>
public static class LockedClauses
{
    private static readonly HtmlParser Parser = new();
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The (clause, version) pairs the HTML's locked blocks name, in order.</summary>
    public static List<(Guid ClauseId, int Version, string InnerHtml)> Blocks(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];
        var document = Parser.ParseDocument($"<body>{html}</body>");
        var blocks = new List<(Guid, int, string)>();
        foreach (var element in document.QuerySelectorAll("[data-clause-id][data-clause-version]"))
        {
            if (Guid.TryParse(element.GetAttribute("data-clause-id"), out var id)
                && int.TryParse(element.GetAttribute("data-clause-version"), out var version))
                blocks.Add((id, version, element.InnerHtml));
        }
        return blocks;
    }

    /// <summary>Refuses a template whose locked block names an unknown or unapproved wording, or changes its words.</summary>
    public static async Task CheckAsync(IApplicationDbContext db, string html, CancellationToken ct)
    {
        var blocks = Blocks(html);
        if (blocks.Count == 0) return;

        var ids = blocks.Select(b => b.ClauseId).Distinct().ToList();
        var versions = await db.ClauseVersions.AsNoTracking()
            .Where(v => ids.Contains(v.ClauseId))
            .Join(db.Clauses, v => v.ClauseId, c => c.Id, (v, c) => new { v.ClauseId, v.Number, v.Body, v.Status, c.Title })
            .ToListAsync(ct);

        foreach (var (clauseId, number, inner) in blocks)
        {
            var version = versions.FirstOrDefault(v => v.ClauseId == clauseId && v.Number == number)
                ?? throw new ValidationException("A library clause in this template no longer exists. Insert it again from the library.");
            // A wording withdrawn since stays readable in the template it is in, but a draft was never approved.
            if (version.Status == ClauseStatus.Draft)
                throw new ValidationException($"\"{version.Title}\" v{number} is not approved: only approved library wording can be inserted.");
            if (Normalise(inner) != Normalise(SafeHtml.Clean(version.Body)))
                throw new ConflictException("clause.lockedEdited",
                    $"The library clause \"{version.Title}\" was edited in the template. Library wording is changed by drafting a new version of the clause.",
                    new Dictionary<string, object?> { ["title"] = version.Title });
        }
    }

    /// <summary>What the contract records: each library clause it carries, once, with its title and version.</summary>
    public static async Task<List<UsedClause>> UsedAsync(IApplicationDbContext db, string? html, CancellationToken ct)
    {
        var blocks = Blocks(html).Select(b => (b.ClauseId, b.Version)).Distinct().ToList();
        if (blocks.Count == 0) return [];
        var ids = blocks.Select(b => b.ClauseId).ToList();
        var titles = await db.Clauses.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
        return [.. blocks.Select(b => new UsedClause(b.ClauseId, titles.GetValueOrDefault(b.ClauseId) ?? "?", b.Version))];
    }

    public static string? Serialise(IReadOnlyCollection<UsedClause> clauses) =>
        clauses.Count == 0 ? null : JsonSerializer.Serialize(clauses);

    public static List<UsedClause> Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<UsedClause>>(json) ?? [];

    /// <summary>The same wording whatever the line breaks and spaces, and however the sanitiser serialised it.</summary>
    private static string Normalise(string html) =>
        Spaces.Replace(SafeHtml.Clean(html), " ").Replace("> <", "><").Trim();
}
