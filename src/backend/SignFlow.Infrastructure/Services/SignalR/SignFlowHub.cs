using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SignFlow.Infrastructure.Services.SignalR;

[Authorize]
public class SignFlowHub : Hub
{
    /// <summary>One entry per connection currently viewing a document (a user with two tabs open is two entries, deduped on broadcast).</summary>
    private sealed record PresenceEntry(string UserId, string Name);

    /// <summary>Shape sent to clients in the "presence-changed" event (SignalR serializes camelCase).</summary>
    public sealed record PresenceViewer(string UserId, string Name);

    // documentId → connectionId → viewer. Static because a new hub instance is created per
    // invocation; presence must outlive them. External token signers never appear here — the
    // hub is [Authorize], so presence only covers authenticated users (owner, admins, recipients
    // with accounts).
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PresenceEntry>> DocumentViewers = new();

    public async Task JoinDocumentGroup(string documentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"document-{documentId}");

        var userId = Context.UserIdentifier ?? string.Empty;
        var name = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "Someone";
        var viewers = DocumentViewers.GetOrAdd(documentId, _ => new ConcurrentDictionary<string, PresenceEntry>());
        viewers[Context.ConnectionId] = new PresenceEntry(userId, name);

        await BroadcastPresence(documentId);
    }

    public async Task LeaveDocumentGroup(string documentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"document-{documentId}");
        RemoveConnection(documentId, Context.ConnectionId);
        await BroadcastPresence(documentId);
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // A closed tab never calls LeaveDocumentGroup — sweep this connection out of every
        // document it was viewing so presence doesn't show ghosts.
        foreach (var (documentId, viewers) in DocumentViewers)
        {
            if (viewers.TryRemove(Context.ConnectionId, out _))
            {
                if (viewers.IsEmpty) DocumentViewers.TryRemove(documentId, out _);
                await BroadcastPresence(documentId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private static void RemoveConnection(string documentId, string connectionId)
    {
        if (DocumentViewers.TryGetValue(documentId, out var viewers))
        {
            viewers.TryRemove(connectionId, out _);
            if (viewers.IsEmpty) DocumentViewers.TryRemove(documentId, out _);
        }
    }

    private Task BroadcastPresence(string documentId)
    {
        var list = DocumentViewers.TryGetValue(documentId, out var viewers)
            ? viewers.Values
                .GroupBy(v => v.UserId)
                .Select(g => new PresenceViewer(g.Key, g.First().Name))
                .ToList()
            : [];

        return Clients.Group($"document-{documentId}")
            .SendAsync("presence-changed", new { DocumentId = documentId, Viewers = list });
    }
}
