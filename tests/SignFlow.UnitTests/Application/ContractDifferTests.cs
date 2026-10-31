using SignFlow.Application.Common.Services;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// The redline is what a reviewer trusts instead of re-reading both drafts, so the cases here are
/// the specification: a change that the differ fails to surface is a change that gets signed
/// unnoticed. Contract prose is used throughout rather than "a/b/c" lines — the pairing heuristic
/// is tuned for it, and toy input would not exercise it.
/// </summary>
public class ContractDifferTests
{
    private const string ArticleOne = "Article 1 — Objet. Le présent contrat définit les conditions de fourniture des services.";
    private const string ArticleTwo = "Article 2 — Durée. Le contrat est conclu pour une durée de douze (12) mois.";
    private const string ArticleThree = "Article 3 — Prix. Le montant total s'élève à 45 000 EUR hors taxes.";

    private static string Doc(params string[] clauses) => string.Join("\n\n", clauses);

    private static ClauseDiff Single(ContractDiff diff, ClauseChangeType change) =>
        Assert.Single(diff.Clauses, c => c.Change == change);

    // ---------------------------------------------------------------- no change

    [Fact]
    public void Reports_no_changes_when_both_versions_are_identical()
    {
        var text = Doc(ArticleOne, ArticleTwo, ArticleThree);

        var diff = ContractDiffer.Compare(text, text);

        Assert.False(diff.HasChanges);
        Assert.Equal(3, diff.UnchangedCount);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(0, diff.ModifiedCount);
    }

    [Fact]
    public void Ignores_re_wrapping_that_did_not_change_a_word()
    {
        // The same clause extracted at a different line width. A line-level diff would call this a
        // rewritten article; nothing was renegotiated.
        var previous = "Article 2 — Durée.\nLe contrat est conclu pour une\ndurée de douze (12) mois.";
        var current = "Article 2 — Durée. Le contrat est\nconclu pour une durée de douze (12) mois.";

        var diff = ContractDiffer.Compare(previous, current);

        Assert.False(diff.HasChanges);
        Assert.Equal(1, diff.UnchangedCount);
    }

    // ---------------------------------------------------------------- insertions and deletions

    [Fact]
    public void Reports_an_inserted_clause_as_added()
    {
        var previous = Doc(ArticleOne, ArticleTwo);
        var current = Doc(ArticleOne, ArticleTwo, ArticleThree);

        var diff = ContractDiffer.Compare(previous, current);

        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(0, diff.ModifiedCount);

        var added = Single(diff, ClauseChangeType.Added);
        Assert.Equal(ArticleThree, added.CurrentText);
        Assert.Null(added.PreviousText);
        Assert.All(added.CurrentWords, w => Assert.True(w.IsChanged));
    }

    [Fact]
    public void Reports_a_deleted_clause_as_removed()
    {
        var previous = Doc(ArticleOne, ArticleTwo, ArticleThree);
        var current = Doc(ArticleOne, ArticleThree);

        var diff = ContractDiffer.Compare(previous, current);

        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(0, diff.AddedCount);

        var removed = Single(diff, ClauseChangeType.Removed);
        Assert.Equal(ArticleTwo, removed.PreviousText);
        Assert.Null(removed.CurrentText);
    }

    [Fact]
    public void Keeps_clauses_in_reading_order()
    {
        var previous = Doc(ArticleOne, ArticleThree);
        var current = Doc(ArticleOne, ArticleTwo, ArticleThree);

        var diff = ContractDiffer.Compare(previous, current);

        Assert.Collection(
            diff.Clauses,
            c => Assert.Equal(ArticleOne, c.CurrentText),
            c => Assert.Equal(ArticleTwo, c.CurrentText),
            c => Assert.Equal(ArticleThree, c.CurrentText));
    }

    // ---------------------------------------------------------------- amendments

    [Fact]
    public void Reports_an_amended_clause_as_modified_rather_than_a_removal_plus_an_addition()
    {
        var previous = Doc(ArticleOne, ArticleTwo);
        var current = Doc(
            ArticleOne,
            "Article 2 — Durée. Le contrat est conclu pour une durée de vingt-quatre (24) mois.");

        var diff = ContractDiffer.Compare(previous, current);

        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
    }

    [Fact]
    public void Marks_only_the_words_that_actually_changed()
    {
        var previous = "Le contrat est conclu pour une durée de douze mois.";
        var current = "Le contrat est conclu pour une durée de vingt-quatre mois.";

        var diff = ContractDiffer.Compare(previous, current);

        var modified = Single(diff, ClauseChangeType.Modified);
        Assert.Equal(["douze"], modified.PreviousWords.Where(w => w.IsChanged).Select(w => w.Text));
        Assert.Equal(["vingt-quatre"], modified.CurrentWords.Where(w => w.IsChanged).Select(w => w.Text));

        // The untouched words are still carried, in order, so the UI can render the whole clause
        // from the marks alone rather than re-splitting the text itself.
        Assert.Equal(previous.Split(' ').Length, modified.PreviousWords.Count);
        Assert.Equal(current.Split(' ').Length, modified.CurrentWords.Count);
    }

    [Fact]
    public void Treats_a_capitalisation_only_difference_as_unchanged_words()
    {
        // Re-typesetting shifts case; a redline full of that noise stops being read. The clause
        // still reports as modified because its text differs, but no word is marked.
        var diff = ContractDiffer.Compare(
            "Le PRESTATAIRE accepte une responsabilité illimitée au titre du présent contrat.",
            "Le prestataire accepte une responsabilité illimitée au titre du présent contrat.");

        var modified = Single(diff, ClauseChangeType.Modified);
        Assert.DoesNotContain(modified.CurrentWords, w => w.IsChanged);
    }

    [Fact]
    public void Reports_a_wholly_rewritten_clause_as_a_removal_and_an_addition()
    {
        // Below the similarity threshold there is no shared clause left to redline, and pretending
        // otherwise would produce a word-level mess instead of "this was replaced".
        var previous = Doc(ArticleOne, ArticleTwo);
        var current = Doc(
            ArticleOne,
            "Article 2 — Résiliation. Chaque partie peut mettre fin au contrat par lettre recommandée.");

        var diff = ContractDiffer.Compare(previous, current);

        Assert.Equal(0, diff.ModifiedCount);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
    }

    [Fact]
    public void Pairs_an_amendment_with_its_counterpart_when_a_new_clause_lands_beside_it()
    {
        var previous = Doc(ArticleOne, ArticleThree);
        var current = Doc(
            ArticleOne,
            "Article 3 — Prix. Le montant total s'élève à 52 000 EUR hors taxes.",
            "Article 4 — Confidentialité. Les parties s'engagent à préserver la confidentialité.");

        var diff = ContractDiffer.Compare(previous, current);

        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);

        var modified = Single(diff, ClauseChangeType.Modified);
        Assert.Contains("45 000", modified.PreviousText);
        Assert.Contains("52 000", modified.CurrentText);
    }

    // ---------------------------------------------------------------- edges

    [Fact]
    public void Treats_a_first_version_with_no_readable_text_as_an_all_new_contract()
    {
        // A scanned PDF extracts as nothing. Reporting every clause as added is honest; reporting
        // "no changes" would be a silent false negative on the one screen that must not have one.
        var diff = ContractDiffer.Compare(null, Doc(ArticleOne, ArticleTwo));

        Assert.Equal(2, diff.AddedCount);
        Assert.Equal(0, diff.UnchangedCount);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Reports_nothing_when_neither_version_has_readable_text()
    {
        var diff = ContractDiffer.Compare("   ", "\n\n");

        Assert.Empty(diff.Clauses);
        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Splits_on_clause_headings_when_the_extractor_gives_no_blank_lines()
    {
        // PDF extraction routinely returns a contract with no blank lines at all. Without heading
        // detection the whole document would collapse into one clause and every edit would read as
        // a single enormous modification.
        var previous = $"{ArticleOne}\n{ArticleTwo}\n{ArticleThree}";
        var current = $"{ArticleOne}\n{ArticleThree}";

        var diff = ContractDiffer.Compare(previous, current);

        Assert.Equal(2, diff.UnchangedCount);
        Assert.Equal(1, diff.RemovedCount);
    }

    [Fact]
    public void Splits_clauses_on_blank_lines_and_trims_them()
    {
        var clauses = ContractDiffer.SplitClauses($"  {ArticleOne}  \n\n\n   \n{ArticleTwo}\n");

        Assert.Equal([ArticleOne, ArticleTwo], clauses);
    }

    [Theory]
    [InlineData("Article 5 — Responsabilité. Le prestataire accepte…")]
    [InlineData("ARTICLE 5 RESPONSABILITE")]
    [InlineData("5. Responsabilité du prestataire")]
    [InlineData("5.2 Plafond de responsabilité")]
    [InlineData("6) Confidentialité")]
    [InlineData("§ 7 Loi applicable")]
    [InlineData("Annexe A — Niveaux de service")]
    [InlineData("Section 8. Governing law")]
    public void Recognises_the_openings_that_start_a_new_clause(string heading)
    {
        var clauses = ContractDiffer.SplitClauses($"…suite de la clause précédente.\n{heading}");

        Assert.Equal(2, clauses.Count);
        Assert.Equal(heading, clauses[1]);
    }

    [Fact]
    public void Does_not_split_a_wrapped_line_that_merely_begins_with_a_figure()
    {
        // "45 000" opening a continuation line must not read as an outline number, or paragraphs
        // get cut mid-sentence and both sides of the redline fill with phantom changes.
        var clauses = ContractDiffer.SplitClauses("Le montant total s'élève à\n45 000 EUR hors taxes.");

        Assert.Single(clauses);
    }

    private const string Confidentiality =
        "Article 4 - Confidentialité. Chaque partie garde confidentielles les informations reçues de l'autre partie pendant toute la durée du contrat et trois ans après.";
    private const string Payment =
        "Article 5 - Paiement. Les factures sont payées à trente jours à compter de leur réception par virement bancaire.";
    private const string Termination =
        "Article 6 - Résiliation. Chaque partie peut résilier le contrat avec un préavis de trois mois notifié par lettre recommandée.";

    [Fact]
    public void A_clause_moved_elsewhere_and_renumbered_is_reported_as_moved_not_removed_and_added()
    {
        var before = string.Join("\n\n", Confidentiality, Payment, Termination);
        var after = string.Join("\n\n",
            Payment.Replace("Article 5", "Article 4"),
            Termination.Replace("Article 6", "Article 5"),
            Confidentiality.Replace("Article 4", "Article 6"));

        var diff = ContractDiffer.Compare(before, after);

        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        var moved = Assert.Single(diff.Clauses, c => c.Change == ClauseChangeType.Moved);
        Assert.StartsWith("Article 6 - Confidentialité", moved.CurrentText);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void A_moved_clause_that_was_also_edited_shows_its_changed_words()
    {
        var before = string.Join("\n\n", Confidentiality, Payment, Termination);
        var after = string.Join("\n\n", Payment, Termination, Confidentiality.Replace("trois ans", "cinq ans"));

        var diff = ContractDiffer.Compare(before, after);

        var moved = Assert.Single(diff.Clauses, c => c.Change == ClauseChangeType.Moved);
        Assert.Equal(["cinq"], moved.CurrentWords.Where(w => w.IsChanged).Select(w => w.Text));
    }

    [Fact]
    public void Two_unrelated_clauses_never_pair_as_a_move()
    {
        var before = string.Join("\n\n", Confidentiality, Payment);
        var after = string.Join("\n\n", Payment, Termination);

        var diff = ContractDiffer.Compare(before, after);

        Assert.Equal(0, diff.MovedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(1, diff.AddedCount);
    }
}
