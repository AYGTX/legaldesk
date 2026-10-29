using SignFlow.Domain.Entities;
using SignFlow.Domain.Enums;
using SignFlow.Domain.Exceptions;
using SignFlow.Domain.Rules;

namespace SignFlow.UnitTests.Domain;

/// <summary>
/// R-M2-5: the transition table is the specification. Every (from, to) pair is walked, so a move added or removed
/// by accident fails here rather than in production.
/// </summary>
public class ContractStateMachineTests
{
    private static readonly HashSet<(DocumentStatus From, DocumentStatus To)> Expected =
    [
        (DocumentStatus.Draft, DocumentStatus.Draft),
        (DocumentStatus.Draft, DocumentStatus.InProgress),
        (DocumentStatus.InProgress, DocumentStatus.Completed),
        (DocumentStatus.InProgress, DocumentStatus.Rejected),
        (DocumentStatus.InProgress, DocumentStatus.ChangesRequested),
        (DocumentStatus.InProgress, DocumentStatus.Expired),
        (DocumentStatus.InProgress, DocumentStatus.Cancelled),
        (DocumentStatus.ChangesRequested, DocumentStatus.Draft),
        (DocumentStatus.ChangesRequested, DocumentStatus.Cancelled),
        (DocumentStatus.Rejected, DocumentStatus.Draft),
        (DocumentStatus.Expired, DocumentStatus.InProgress),
        (DocumentStatus.Expired, DocumentStatus.Cancelled),
    ];

    public static TheoryData<DocumentStatus, DocumentStatus> AllPairs()
    {
        var data = new TheoryData<DocumentStatus, DocumentStatus>();
        foreach (var from in Enum.GetValues<DocumentStatus>())
            foreach (var to in Enum.GetValues<DocumentStatus>())
                data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Allows_exactly_the_listed_moves(DocumentStatus from, DocumentStatus to)
    {
        Assert.Equal(Expected.Contains((from, to)), ContractStateMachine.CanMove(from, to));
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Document_refuses_every_move_the_table_forbids(DocumentStatus from, DocumentStatus to)
    {
        var document = NewDocument();
        document.SeedStatus(from);

        if (Expected.Contains((from, to)))
        {
            document.SetStatus(to);
            Assert.Equal(to, document.Status);
        }
        else
        {
            var error = Assert.Throws<InvalidStatusTransitionException>(() => document.SetStatus(to));
            Assert.Equal(from, error.From);
            Assert.Equal(from, document.Status);
        }
    }

    [Fact]
    public void A_completed_contract_cannot_be_revised()
    {
        var document = NewDocument();
        document.SeedStatus(DocumentStatus.Completed);

        Assert.Throws<InvalidStatusTransitionException>(() =>
            document.ApplyRevision("v2.pdf", "documents/v2.pdf", 10, 1, "hash"));
    }

    [Fact]
    public void Only_an_expired_contract_can_be_reopened()
    {
        var cancelled = NewDocument();
        cancelled.SeedStatus(DocumentStatus.Cancelled);
        Assert.Throws<InvalidStatusTransitionException>(() => cancelled.Reopen(24));

        var expired = NewDocument();
        expired.SeedStatus(DocumentStatus.Expired);
        expired.Reopen(24);
        Assert.Equal(DocumentStatus.InProgress, expired.Status);
    }

    private static Document NewDocument() =>
        Document.Create("Contract", "contract.pdf", "documents/contract.pdf", 10, 1, Guid.NewGuid());
}
