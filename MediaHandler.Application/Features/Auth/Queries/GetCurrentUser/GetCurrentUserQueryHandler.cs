using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediaHandler.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Auth.Queries.GetCurrentUser;

public record GetCurrentUserQuery : IRequest<Result<UserDto>>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetCurrentUserQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<UserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.OktaId == _currentUser.OktaId, cancellationToken);

        if (user is null)
            return Result.Fail<UserDto>("User not found.");

        return Result.Success(new UserDto(user.Id, user.Email, user.DisplayName,
            user.PreferredLanguage, user.Role.ToString(), user.IsActive));
    }
}
