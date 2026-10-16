using SignFlow.Application.Common.Services;
using SignFlow.Domain.Enums;

namespace SignFlow.UnitTests.Application;

/// <summary>
/// A signature placeholder in an HTML template marks where that signer's signature belongs. These cases pin down the
/// two halves of that: the renderer leaves a slot at the placeholder, and the sealed-PDF step swaps each slot for the
/// signature block — with nothing left over for a reader to see.
/// </summary>
public class HtmlSignatureSlotsTests
{
    private static readonly IReadOnlyDictionary<string, HtmlTemplateFieldType> Types = new Dictionary<string, HtmlTemplateFieldType>();

    [Fact]
    public void A_signature_placeholder_becomes_the_slot_the_caller_supplied()
    {
        var slots = new Dictionary<string, string> { ["legalSig"] = "<span class=\"sig-slot\">Rita</span>" };

        var html = HtmlContractRenderer.Render(
            "<p>Pour le Prestataire : {{legalSig}}</p>", new Dictionary<string, string>(), Types, slots);

        Assert.Equal("<p>Pour le Prestataire : <span class=\"sig-slot\">Rita</span></p>", html);
    }

    [Fact]
    public void Without_a_slot_the_placeholder_leaves_no_trace_as_before()
    {
        var html = HtmlContractRenderer.Render("<p>Signé : {{legalSig}}</p>", new Dictionary<string, string>(), Types);

        Assert.Equal("<p>Signé : </p>", html);
    }

    [Fact]
    public void An_answer_with_the_same_name_wins_over_a_slot()
    {
        var html = HtmlContractRenderer.Render(
            "{{legalSig}}", new Dictionary<string, string> { ["legalSig"] = "answer" }, Types,
            new Dictionary<string, string> { ["legalSig"] = "<b>slot</b>" });

        Assert.Equal("answer", html);
    }

    [Fact]
    public void Each_marker_is_replaced_by_its_signature_block_and_reported_as_placed()
    {
        var html = $"<td>{HtmlSignatureSlots.Marker("providerSig")}</td><td>{HtmlSignatureSlots.Marker("clientSig")}</td>";
        var blocks = new Dictionary<string, string> { ["providerSig"] = "[P]", ["clientSig"] = "[C]" };

        var (result, placed) = HtmlSignatureSlots.Apply(html, blocks);

        Assert.Equal("<td>[P]</td><td>[C]</td>", result);
        Assert.Equal(["clientSig", "providerSig"], placed.Order());
    }

    [Fact]
    public void A_marker_with_no_signature_is_removed_so_it_never_reaches_the_pdf()
    {
        var html = $"<p>A {HtmlSignatureSlots.Marker("signed")} B {HtmlSignatureSlots.Marker("pending")}</p>";

        var (result, placed) = HtmlSignatureSlots.Apply(html, new Dictionary<string, string> { ["signed"] = "[S]" });

        Assert.Equal("<p>A [S] B </p>", result);
        Assert.DoesNotContain("sf-signature", result);
        Assert.Equal(["signed"], placed);
    }

    [Fact]
    public void The_same_signer_placeholder_used_twice_is_filled_twice()
    {
        var html = HtmlSignatureSlots.Marker("sig") + " / " + HtmlSignatureSlots.Marker("sig");

        var (result, _) = HtmlSignatureSlots.Apply(html, new Dictionary<string, string> { ["sig"] = "X" });

        Assert.Equal("X / X", result);
    }
}
