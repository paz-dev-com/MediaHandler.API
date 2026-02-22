using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediaHandler.Domain.Enums;
using MediaHandler.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Admin.Queries;

public record GetUsersQuery(int Page = 1, int PageSize = 20, string? Search = null)
    : IRequest<Result<PagedResult<UserDto>>>;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, Result<PagedResult<UserDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetUsersQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<PagedResult<UserDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(u => u.Email.Contains(request.Search) ||
                                     (u.DisplayName != null && u.DisplayName.Contains(request.Search)));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(u => u.Email)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new UserDto(u.Id, u.Email, u.DisplayName, u.PreferredLanguage, u.Role.ToString(), u.IsActive))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<UserDto>(items, total, request.Page, request.PageSize));
    }
}

public record SetUserRoleCommand(Guid UserId, UserRole Role) : IRequest<Result>;

public class SetUserRoleCommandHandler : IRequestHandler<SetUserRoleCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetUserRoleCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(SetUserRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FindAsync([request.UserId], cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.User), request.UserId);

        user.Role = request.Role;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public record SetUserActiveCommand(Guid UserId, bool IsActive) : IRequest<Result>;

public class SetUserActiveCommandHandler : IRequestHandler<SetUserActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetUserActiveCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FindAsync([request.UserId], cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.User), request.UserId);

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
