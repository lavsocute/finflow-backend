using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Membership.DTOs.Requests;
using FinFlow.Application.Membership.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.Invitations;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.Tenants;

namespace FinFlow.Application.Membership.Commands.InviteMember;

public sealed class InviteMemberCommandHandler : MediatR.IRequestHandler<InviteMemberCommand, Result<InvitationResponse>>
{
    private readonly IAccountRepository _accountRepository;
    private readonly ITenantMembershipRepository _membershipRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IInvitationRepository _invitationRepository;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentTenant _currentTenant;

    public InviteMemberCommandHandler(
        IAccountRepository accountRepository,
        ITenantMembershipRepository membershipRepository,
        ITenantRepository tenantRepository,
        IInvitationRepository invitationRepository,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        ICurrentTenant currentTenant)
    {
        _accountRepository = accountRepository;
        _membershipRepository = membershipRepository;
        _tenantRepository = tenantRepository;
        _invitationRepository = invitationRepository;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _currentTenant = currentTenant;
    }

    public async Task<Result<InvitationResponse>> Handle(InviteMemberCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        
        var inviterAccount = await _accountRepository.GetLoginInfoByIdAsync(request.InviterAccountId, cancellationToken);
        if (inviterAccount == null || !inviterAccount.IsActive)
            return Result.Failure<InvitationResponse>(AccountErrors.Unauthorized);

        var inviterMembership = await _membershipRepository.GetByIdAsync(request.InviterMembershipId, cancellationToken);
        if (inviterMembership == null || !inviterMembership.IsActive)
            return Result.Failure<InvitationResponse>(TenantMembershipErrors.NotFound);

        if (inviterMembership.AccountId != request.InviterAccountId)
            return Result.Failure<InvitationResponse>(AccountErrors.Unauthorized);

        if (inviterMembership.Role != Domain.Enums.RoleType.TenantAdmin)
            return Result.Failure<InvitationResponse>(InvitationErrors.Forbidden);

        if (request.Role is Domain.Enums.RoleType.SuperAdmin)
            return Result.Failure<InvitationResponse>(InvitationErrors.InvalidRole);

        var tenant = await _tenantRepository.GetByIdAsync(inviterMembership.IdTenant, cancellationToken);
        if (tenant == null || !tenant.IsActive)
            return Result.Failure<InvitationResponse>(TenantErrors.NotFound);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingAccount = await _accountRepository.GetLoginInfoByEmailAsync(normalizedEmail, cancellationToken);
        if (existingAccount != null)
        {
            var alreadyMember = await _membershipRepository.ExistsAsync(existingAccount.Id, inviterMembership.IdTenant, cancellationToken);
            if (alreadyMember)
                return Result.Failure<InvitationResponse>(InvitationErrors.AlreadyMember);
        }

        var pendingInvitationExists = await _invitationRepository.HasPendingInvitationAsync(normalizedEmail, inviterMembership.IdTenant, cancellationToken);
        if (pendingInvitationExists)
            return Result.Failure<InvitationResponse>(InvitationErrors.PendingInvitationExists);

        var rawInviteToken = _tokenService.GenerateRefreshToken();
        var invitationResult = Invitation.Create(
            normalizedEmail,
            inviterMembership.IdTenant,
            inviterMembership.Id,
            request.Role,
            rawInviteToken,
            DateTime.UtcNow.AddDays(7));

        if (invitationResult.IsFailure)
            return Result.Failure<InvitationResponse>(invitationResult.Error);

        var invitation = invitationResult.Value;
        var originalTenantId = _currentTenant.Id;
        var originalMembershipId = _currentTenant.MembershipId;
        var originalIsSuperAdmin = _currentTenant.IsSuperAdmin;

        try
        {
            _currentTenant.Id = inviterMembership.IdTenant;
            _currentTenant.MembershipId = inviterMembership.Id;
            _currentTenant.IsSuperAdmin = false;

            _invitationRepository.Add(invitation);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _currentTenant.Id = originalTenantId;
            _currentTenant.MembershipId = originalMembershipId;
            _currentTenant.IsSuperAdmin = originalIsSuperAdmin;
        }

        return Result.Success(new InvitationResponse(
            invitation.Id,
            rawInviteToken,
            invitation.Email,
            invitation.Role,
            invitation.IdTenant,
            invitation.ExpiresAt));
    }
}
