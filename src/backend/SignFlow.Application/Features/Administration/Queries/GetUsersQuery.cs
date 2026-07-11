using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SignFlow.Application.Common.Exceptions;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Features.Administration.Queries;

public record GetUsersQuery(int PageNumber = 1, int PageSize = 20, string? Search = null) : IQuery<PagedResult<UserDto>>;

public record UserDto(Guid Id, string FullName, string Email, string? Company, string? Department, string? Title, bool IsActive, DateTime CreatedAt, DateTime? LastLoginAt, List<string> Roles);

public class GetUsersQueryHandler(
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser) : IQueryHandler<GetUsersQuery, PagedResult<UserDto>>
{
    public async Task<PagedResult<UserDto>> HandleAsync(GetUsersQuery query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsInRole("Administrator"))
            throw new ForbiddenAccessException();

        var q = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(u => u.Email!.ToLower().Contains(s) || u.FirstName.ToLower().Contains(s) || u.LastName.ToLower().Contains(s));
        }

        var totalCount = await q.CountAsync(cancellationToken);
        var users = await q.OrderBy(u => u.LastName)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            dtos.Add(new UserDto(user.Id, user.FullName, user.Email!, user.Company, user.Department, user.Title, user.IsActive, user.CreatedAt, user.LastLoginAt, roles.ToList()));
        }

        return PagedResult<UserDto>.Create(dtos, totalCount, query.PageNumber, query.PageSize);
    }
}
