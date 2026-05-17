using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantMemberships;
using FinFlow.Domain.TenantUsageSnapshots;

namespace FinFlow.Infrastructure.Subscriptions;

public sealed class MemberUsageService : IMemberUsageService
{
    private const string MembershipRequiredMessage = "Membership is required for this tenant.";
    private const string MembershipTenantMismatchMessage = "Membership does not belong to the tenant.";
    private const string MembershipInactiveMessage = "Membership must be active for usage tracking.";

    private readonly IMemberUsageSnapshotRepository _memberUsageSnapshotRepository;
    private readonly ITenantMembershipRepository _tenantMembershipRepository;

    public MemberUsageService(
        IMemberUsageSnapshotRepository memberUsageSnapshotRepository,
        ITenantMembershipRepository tenantMembershipRepository)
    {
        _memberUsageSnapshotRepository = memberUsageSnapshotRepository;
        _tenantMembershipRepository = tenantMembershipRepository;
    }

    public async Task<MemberUsageSnapshot> GetCurrentUsageAsync(
        Guid tenantId,
        Guid membershipId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(
            tenantId,
            membershipId,
            periodStart,
            periodEnd,
            cancellationToken);

        return snapshot;
    }

    public async Task RecordOcrUsageAsync(
        Guid tenantId,
        Guid membershipId,
        int pageCount,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(
            tenantId,
            membershipId,
            periodStart,
            periodEnd,
            cancellationToken);

        var result = snapshot.RecordOcrUsage(pageCount);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Description);
    }

    public async Task RecordChatbotUsageAsync(
        Guid tenantId,
        Guid membershipId,
        int messageCount,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(
            tenantId,
            membershipId,
            periodStart,
            periodEnd,
            cancellationToken);

        var result = snapshot.RecordChatbotUsage(messageCount);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Description);
    }

    public async Task RecordChatbotTokensAsync(
        Guid tenantId,
        Guid membershipId,
        long tokensUsed,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        if (tokensUsed <= 0)
            return;

        var snapshot = await GetOrCreateSnapshotAsync(
            tenantId,
            membershipId,
            periodStart,
            periodEnd,
            cancellationToken);

        var result = snapshot.RecordChatbotTokens(tokensUsed);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Description);
    }

    private async Task<MemberUsageSnapshot> GetOrCreateSnapshotAsync(
        Guid tenantId,
        Guid membershipId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken)
    {
        await EnsureMembershipBelongsToTenantAsync(tenantId, membershipId, cancellationToken);

        var existingSnapshot = await _memberUsageSnapshotRepository.GetByMembershipAndPeriodAsync(
            tenantId,
            membershipId,
            periodStart,
            periodEnd,
            cancellationToken);

        if (existingSnapshot is not null)
            return existingSnapshot;

        var createdSnapshotResult = MemberUsageSnapshot.Create(tenantId, membershipId, periodStart, periodEnd);
        if (createdSnapshotResult.IsFailure)
            throw new InvalidOperationException(createdSnapshotResult.Error.Description);

        return await _memberUsageSnapshotRepository.GetOrCreateAsync(createdSnapshotResult.Value, cancellationToken);
    }

    private async Task EnsureMembershipBelongsToTenantAsync(
        Guid tenantId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var membership = await _tenantMembershipRepository.GetByIdAsync(membershipId, cancellationToken);

        if (membership is null)
            throw new InvalidOperationException(MembershipRequiredMessage);

        if (membership.IdTenant != tenantId)
            throw new InvalidOperationException(MembershipTenantMismatchMessage);

        if (!membership.IsActive)
            throw new InvalidOperationException(MembershipInactiveMessage);
    }
}
