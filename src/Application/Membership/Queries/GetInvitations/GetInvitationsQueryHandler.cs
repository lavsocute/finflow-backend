using FinFlow.Application.Common;
using FinFlow.Application.Membership.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Invitations;

namespace FinFlow.Application.Membership.Queries.GetInvitations;

public sealed class GetInvitationsQueryHandler : IQueryHandler<GetInvitationsQuery, Result<IReadOnlyList<InvitationDto>>>
{
    private readonly IInvitationRepository _invitationRepository;

    public GetInvitationsQueryHandler(IInvitationRepository invitationRepository)
    {
        _invitationRepository = invitationRepository;
    }

    public async Task<Result<IReadOnlyList<InvitationDto>>> Handle(GetInvitationsQuery request, CancellationToken cancellationToken)
    {
        var invitations = await _invitationRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);

        var dtos = invitations
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(i => new InvitationDto(
                i.Id,
                i.Email,
                i.IdTenant,
                i.Role,
                i.ExpiresAt,
                i.CreatedAt,
                i.AcceptedAt,
                i.RevokedAt,
                i.RevokedByMembershipId,
                i.IsActive && !i.AcceptedAt.HasValue && !i.RevokedAt.HasValue && i.ExpiresAt > DateTime.UtcNow,
                i.ExpiresAt > DateTime.UtcNow))
            .ToList();

        return Result.Success((IReadOnlyList<InvitationDto>)dtos);
    }
}
