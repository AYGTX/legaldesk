using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SignFlow.Infrastructure.Services.SignalR;

[Authorize]
public class SignFlowHub : Hub
{
    public async Task JoinDocumentGroup(string documentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"document-{documentId}");
    }

    public async Task LeaveDocumentGroup(string documentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"document-{documentId}");
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

        await base.OnConnectedAsync();
    }
}
