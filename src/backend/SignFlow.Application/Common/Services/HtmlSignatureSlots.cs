using System.Text.RegularExpressions;

namespace SignFlow.Application.Common.Services;

/// <summary>
/// A signature placeholder (<c>{{legalSignature}}</c>) written in an HTML template marks where that signer's signature
/// belongs in the contract. When the sealed PDF is built the renderer first leaves a marker at each placeholder; the
/// PDF generator, which owns the signature images, then swaps each marker for the signature block. A placeholder that
/// ends up with no signature, or a signature with no placeholder, falls back to the signature section at the end.
/// </summary>
public static class HtmlSignatureSlots
{
    private static readonly Regex AnyMarker = new(@"<sf-signature slot=""\w+""></sf-signature>", RegexOptions.Compiled);

    public static string Marker(string key) => $"<sf-signature slot=\"{key}\"></sf-signature>";

    /// <summary>
    /// Replaces each marker that has a block in <paramref name="blocks"/> and removes the rest, so no marker ever
    /// reaches the PDF. Returns the keys that were placed in the text.
    /// </summary>
    public static (string Html, HashSet<string> Placed) Apply(string html, IReadOnlyDictionary<string, string> blocks)
    {
        var placed = new HashSet<string>();
        var result = AnyMarker.Replace(html, m =>
        {
            var key = Regex.Match(m.Value, @"slot=""(\w+)""").Groups[1].Value;
            if (!blocks.TryGetValue(key, out var block)) return string.Empty;
            placed.Add(key);
            return block;
        });
        return (result, placed);
    }
}
