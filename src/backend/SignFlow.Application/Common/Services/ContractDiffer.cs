using System.Text.RegularExpressions;

namespace SignFlow.Application.Common.Services;

/// <summary>How a clause changed between two versions of the same contract.</summary>
public enum ClauseChangeType
{
    Unchanged = 0,
    Added = 1,
    Removed = 2,
    Modified = 3,
    /// <summary>The clause is somewhere else now (R-M7-5); its words may have changed a little, and are marked.</summary>
    Moved = 4
}

/// <summary>
/// One run of words inside a clause. <paramref name="IsChanged"/> marks the words that differ from
/// the other version, so the UI can underline an insertion or strike a deletion without re-deriving
/// the comparison client-side.
/// </summary>
public record WordMark(string Text, bool IsChanged);

/// <summary>
/// A single clause of the redline. For <see cref="ClauseChangeType.Modified"/> both sides are
/// populated and both word lists are marked; for Added only the current side is, for Removed only
/// the previous side.
/// </summary>
public record ClauseDiff(
    ClauseChangeType Change,
    string? PreviousText,
    string? CurrentText,
    IReadOnlyList<WordMark> PreviousWords,
    IReadOnlyList<WordMark> CurrentWords);

/// <summary>The full redline plus the headline counts a reviewer reads first.</summary>
public record ContractDiff(
    IReadOnlyList<ClauseDiff> Clauses,
    int AddedCount,
    int RemovedCount,
    int ModifiedCount,
    int UnchangedCount,
    int MovedCount = 0)
{
    public bool HasChanges => AddedCount > 0 || RemovedCount > 0 || ModifiedCount > 0 || MovedCount > 0;
}

/// <summary>
/// Produces a clause-level redline between two versions of a contract's text.
///
/// This is deliberately a <b>deterministic text comparison, not an AI call</b>: "what changed
/// between the version we sent and the version they returned" is the one question in the review
/// loop that must be exhaustive and reproducible. A model that summarises the changes can omit one,
/// and the clause it omits is the clause that gets signed unnoticed.
///
/// Lives in Application (rather than beside the PDF plumbing in Infrastructure) so it is reachable
/// from <c>tests/SignFlow.UnitTests</c>, which references only this project.
/// </summary>
public static class ContractDiffer
{
    /// <summary>
    /// How much of two clauses must survive for a removal+insertion pair to read as one clause that
    /// was <i>edited</i> rather than two unrelated clauses. Tuned low: an amended liability cap can
    /// keep only its opening words, and showing that as "removed, and separately added" hides the
    /// very thing the reviewer opened the screen to see.
    /// </summary>
    private const double ModifiedSimilarityThreshold = 0.4;

    /// <summary>
    /// Guard against pathological input (a scanned page extracted as one enormous run). The word
    /// LCS is O(n*m); beyond this the pair is reported as a plain add/remove instead of hanging.
    /// </summary>
    private const int MaxWordsForWordDiff = 4000;

    public static ContractDiff Compare(string? previousText, string? currentText)
    {
        var previous = SplitClauses(previousText);
        var current = SplitClauses(currentText);

        var ops = DiffClauses(previous, current);
        var clauses = DetectMoves(PairModifications(ops, previous, current));

        return new ContractDiff(
            clauses,
            clauses.Count(c => c.Change == ClauseChangeType.Added),
            clauses.Count(c => c.Change == ClauseChangeType.Removed),
            clauses.Count(c => c.Change == ClauseChangeType.Modified),
            clauses.Count(c => c.Change == ClauseChangeType.Unchanged),
            clauses.Count(c => c.Change == ClauseChangeType.Moved));
    }

    /// <summary>How much of a clause must survive, apart from its number, for a removal here and an insertion there to
    /// read as the same clause moved. Much stricter than an edit in place: two unrelated clauses must never pair.</summary>
    private const double MovedSimilarityThreshold = 0.85;

    private static readonly Regex LeadingNumber = new(
        @"^(?:(?:article|art\.|section|clause)\s+)?(?:\d+(?:\.\d+)*|[ivxlc]+(?=[\s.):\-–]))\s*[.):\-–]?\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A clause taken out in one place and put back in another is a move, not a deletion and an unrelated addition
    /// (R-M7-5). It is shown where it now stands; renumbering ("Article 4" becoming "Article 6") alone is not a change.
    /// </summary>
    private static List<ClauseDiff> DetectMoves(List<ClauseDiff> clauses)
    {
        var removed = clauses.Select((c, i) => (c, i)).Where(x => x.c.Change == ClauseChangeType.Removed).ToList();
        var added = clauses.Select((c, i) => (c, i)).Where(x => x.c.Change == ClauseChangeType.Added).ToList();
        if (removed.Count == 0 || added.Count == 0) return clauses;

        var candidates = new List<(int Removed, int Added, double Score)>();
        foreach (var (r, ri) in removed)
        {
            var before = Tokenize(LeadingNumber.Replace(r.PreviousText!, string.Empty));
            foreach (var (a, ai) in added)
            {
                var after = Tokenize(LeadingNumber.Replace(a.CurrentText!, string.Empty));
                if (before.Count < 3 || after.Count < 3) continue; // a heading alone moves nothing worth reporting
                var score = Similarity(before, after);
                if (score >= MovedSimilarityThreshold) candidates.Add((ri, ai, score));
            }
        }
        if (candidates.Count == 0) return clauses;

        var result = clauses.ToArray();
        var dropped = new HashSet<int>();
        var usedAdds = new HashSet<int>();
        foreach (var (ri, ai, _) in candidates.OrderByDescending(c => c.Score))
        {
            if (dropped.Contains(ri) || usedAdds.Contains(ai)) continue;
            dropped.Add(ri);
            usedAdds.Add(ai);

            var previousText = clauses[ri].PreviousText!;
            var currentText = clauses[ai].CurrentText!;
            var (prevWords, curWords) = MarkWords(Tokenize(previousText), Tokenize(currentText));
            result[ai] = new ClauseDiff(ClauseChangeType.Moved, previousText, currentText, prevWords, curWords);
        }

        return [.. result.Where((_, i) => !dropped.Contains(i))];
    }

    /// <summary>
    /// Splits extracted PDF text into clauses. A clause ends at a blank line and at the next line
    /// that opens a new one (see <see cref="ClauseHeading"/>); every other line is a continuation
    /// and is joined onto the current clause.
    /// </summary>
    /// <remarks>
    /// Joining continuations rather than diffing line-by-line is what makes the redline survive
    /// <b>re-wrapping</b>: the same paragraph re-flowed at a different width produces entirely
    /// different lines, and a line-level diff would report a rewritten article where nothing was
    /// renegotiated. Blank lines alone are not enough to split on — PDF text extractors routinely
    /// return a contract with none — which is why headings are detected too. When neither signal is
    /// present the contract stays one clause and the change still shows, word by word, inside it.
    /// </remarks>
    public static List<string> SplitClauses(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var clauses = new List<string>();
        var buffer = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }

            var trimmed = line.Trim();
            if (ClauseHeading.IsMatch(trimmed)) Flush();
            buffer.Add(trimmed);
        }
        Flush();

        return clauses;

        void Flush()
        {
            if (buffer.Count == 0) return;
            clauses.Add(string.Join(' ', buffer));
            buffer.Clear();
        }
    }

    /// <summary>
    /// Openings that start a new clause: a named heading in either working language, or an outline
    /// number such as <c>3.</c>, <c>3.1</c> or <c>4)</c>.
    /// </summary>
    /// <remarks>
    /// A single-level number deliberately requires a trailing dot or bracket. Without it, a wrapped
    /// line continuing "…45 000 EUR hors taxes" would read as a new clause and split a paragraph
    /// mid-sentence, which is the failure mode this whole method exists to avoid. A multi-level
    /// number (<c>5.2</c>) needs no such punctuation — prose does not wrap onto one.
    /// </remarks>
    private static readonly Regex ClauseHeading = new(
        @"^(?:(?:article|art\.|section|clause|chapitre|chapter|annexe|annex|appendix|exhibit|schedule|préambule|preamble|titre|title)\b|§|(?:\d+(?:\.\d+)+|\d+\s*[.)])\s)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whitespace-insensitive key used for matching; the original text is kept for display.</summary>
    private static string NormalizeForMatch(string clause) =>
        string.Join(' ', clause.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string MatchKey(string clause) => NormalizeForMatch(LeadingNumber.Replace(clause, string.Empty));

    private static List<string> Tokenize(string clause) =>
        [.. clause.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    private record ClauseOp(ClauseChangeType Change, int PreviousIndex, int CurrentIndex);

    /// <summary>
    /// Longest-common-subsequence diff over clauses, emitted in reading order. Unmatched clauses
    /// come out as separate Removed/Added ops; <see cref="PairModifications"/> is what decides
    /// whether an adjacent pair of those is really one edited clause.
    /// </summary>
    private static List<ClauseOp> DiffClauses(List<string> previous, List<string> current)
    {
        // Matched without their number, so renumbering alone ("Article 5" now "Article 4") does not hide a moved clause.
        var a = previous.Select(MatchKey).ToList();
        var b = current.Select(MatchKey).ToList();

        var lcs = BuildLcsTable(a, b, StringComparer.Ordinal);

        var ops = new List<ClauseOp>();
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (string.Equals(a[i], b[j], StringComparison.Ordinal))
            {
                ops.Add(new ClauseOp(ClauseChangeType.Unchanged, i, j));
                i++; j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                ops.Add(new ClauseOp(ClauseChangeType.Removed, i, -1));
                i++;
            }
            else
            {
                ops.Add(new ClauseOp(ClauseChangeType.Added, -1, j));
                j++;
            }
        }
        while (i < a.Count) ops.Add(new ClauseOp(ClauseChangeType.Removed, i++, -1));
        while (j < b.Count) ops.Add(new ClauseOp(ClauseChangeType.Added, -1, j++));

        return ops;
    }

    /// <summary>
    /// Collapses each "block of removals immediately followed by insertions" into Modified clauses
    /// where the two sides are recognisably the same clause, leaving genuine additions and deletions
    /// alone. Pairing is greedy by best similarity, so an amended clause finds its counterpart even
    /// when the block also contains an unrelated new one.
    /// </summary>
    private static List<ClauseDiff> PairModifications(
        List<ClauseOp> ops, List<string> previous, List<string> current)
    {
        var result = new List<ClauseDiff>();

        for (var index = 0; index < ops.Count;)
        {
            if (ops[index].Change == ClauseChangeType.Unchanged)
            {
                var before = previous[ops[index].PreviousIndex];
                var text = current[ops[index].CurrentIndex];
                if (NormalizeForMatch(before) == NormalizeForMatch(text))
                {
                    result.Add(new ClauseDiff(ClauseChangeType.Unchanged, text, text, [], []));
                }
                else
                {
                    // Renumbered: the same clause under another number is still a change a reviewer sees.
                    var (prevWords, curWords) = MarkWords(Tokenize(before), Tokenize(text));
                    result.Add(new ClauseDiff(ClauseChangeType.Modified, before, text, prevWords, curWords));
                }
                index++;
                continue;
            }

            var removed = new List<int>();
            var added = new List<int>();
            while (index < ops.Count && ops[index].Change == ClauseChangeType.Removed)
                removed.Add(ops[index++].PreviousIndex);
            while (index < ops.Count && ops[index].Change == ClauseChangeType.Added)
                added.Add(ops[index++].CurrentIndex);

            result.AddRange(ResolveBlock(removed, added, previous, current));
        }

        return result;
    }

    private static List<ClauseDiff> ResolveBlock(
        List<int> removed, List<int> added, List<string> previous, List<string> current)
    {
        var block = new List<ClauseDiff>();
        var takenAdds = new HashSet<int>();

        foreach (var prevIndex in removed)
        {
            var prevTokens = Tokenize(previous[prevIndex]);

            var bestAdd = -1;
            var bestScore = ModifiedSimilarityThreshold;
            foreach (var curIndex in added)
            {
                if (takenAdds.Contains(curIndex)) continue;

                var score = Similarity(prevTokens, Tokenize(current[curIndex]));
                if (score >= bestScore)
                {
                    bestScore = score;
                    bestAdd = curIndex;
                }
            }

            if (bestAdd < 0)
            {
                block.Add(new ClauseDiff(
                    ClauseChangeType.Removed, previous[prevIndex], null,
                    MarkAll(prevTokens), []));
                continue;
            }

            takenAdds.Add(bestAdd);
            var (prevWords, curWords) = MarkWords(prevTokens, Tokenize(current[bestAdd]));
            block.Add(new ClauseDiff(
                ClauseChangeType.Modified, previous[prevIndex], current[bestAdd], prevWords, curWords));
        }

        foreach (var curIndex in added.Where(c => !takenAdds.Contains(c)))
        {
            block.Add(new ClauseDiff(
                ClauseChangeType.Added, null, current[curIndex], [], MarkAll(Tokenize(current[curIndex]))));
        }

        return block;
    }

    private static List<WordMark> MarkAll(List<string> tokens) =>
        [.. tokens.Select(t => new WordMark(t, true))];

    /// <summary>
    /// Word-level LCS: words on the common subsequence are unchanged, everything else is marked.
    /// Comparison is case-insensitive on purpose — a capitalisation shift caused by re-typesetting
    /// is noise, and a redline full of noise stops being read.
    /// </summary>
    private static (List<WordMark> Previous, List<WordMark> Current) MarkWords(
        List<string> previous, List<string> current)
    {
        if (previous.Count > MaxWordsForWordDiff || current.Count > MaxWordsForWordDiff)
            return (MarkAll(previous), MarkAll(current));

        var lcs = BuildLcsTable(previous, current, StringComparer.OrdinalIgnoreCase);

        var prevMarks = new List<WordMark>(previous.Count);
        var curMarks = new List<WordMark>(current.Count);

        int i = 0, j = 0;
        while (i < previous.Count && j < current.Count)
        {
            if (string.Equals(previous[i], current[j], StringComparison.OrdinalIgnoreCase))
            {
                prevMarks.Add(new WordMark(previous[i], false));
                curMarks.Add(new WordMark(current[j], false));
                i++; j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                prevMarks.Add(new WordMark(previous[i], true));
                i++;
            }
            else
            {
                curMarks.Add(new WordMark(current[j], true));
                j++;
            }
        }
        while (i < previous.Count) prevMarks.Add(new WordMark(previous[i++], true));
        while (j < current.Count) curMarks.Add(new WordMark(current[j++], true));

        return (prevMarks, curMarks);
    }

    private static double Similarity(List<string> previous, List<string> current)
    {
        if (previous.Count == 0 && current.Count == 0) return 1;
        if (previous.Count == 0 || current.Count == 0) return 0;
        if (previous.Count > MaxWordsForWordDiff || current.Count > MaxWordsForWordDiff) return 0;

        var lcs = BuildLcsTable(previous, current, StringComparer.OrdinalIgnoreCase);
        return 2.0 * lcs[0, 0] / (previous.Count + current.Count);
    }

    /// <summary>
    /// Suffix LCS table: <c>table[i, j]</c> is the LCS length of <c>a[i..]</c> and <c>b[j..]</c>,
    /// which lets the walk above run forward and emit ops in reading order.
    /// </summary>
    private static int[,] BuildLcsTable(List<string> a, List<string> b, StringComparer comparer)
    {
        var table = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                table[i, j] = comparer.Equals(a[i], b[j])
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }
        return table;
    }
}
