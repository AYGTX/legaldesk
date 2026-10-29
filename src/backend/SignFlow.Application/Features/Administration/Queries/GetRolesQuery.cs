using SignFlow.Application.Common.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.Application.Features.Administration.Queries;

public record GetRolesQuery : IQuery<List<string>>;

public class GetRolesQueryHandler(
    RoleManager<IdentityRole<Guid>> roleManager,
    ICurrentUserService currentUser) : IQueryHandler<GetRolesQuery, List<string>>
{
    public async Task<List<string>> HandleAsync(GetRolesQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.Can(Permissions.ManageUsers))
            throw new ForbiddenAccessException();

        return await roleManager.Roles
            .Where(r => r.Name != null)
            .Select(r => r.Name!)
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);
    }
}
