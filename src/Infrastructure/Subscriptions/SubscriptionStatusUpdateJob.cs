using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinFlow.Infrastructure.Subscriptions;

/// <summary>
/// Background job that updates DB-stored subscription status to match the lazy-computed
/// effective status. This keeps queries/dashboards fast (no need to recompute every read)
/// and emits domain events for status transitions (e.g., notifications).
///
/// The lazy compute (ComputeEffectiveStatus) remains the source of truth for quota/feature
/// gates — this job is a write-through cache.
/// </summary>
public sealed class SubscriptionStatusUpdateJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionStatusUpdateJob> _logger;

    // Run every hour. Adjust based on traffic — for low-volume SaaS, daily is enough.
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public SubscriptionStatusUpdateJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionStatusUpdateJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SubscriptionStatusUpdateJob started.");

        // Wait briefly on startup so the app finishes booting before first scan.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateSubscriptionStatusesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubscriptionStatusUpdateJob failed during iteration. Will retry next interval.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        _logger.LogInformation("SubscriptionStatusUpdateJob stopped.");
    }

    private async Task UpdateSubscriptionStatusesAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        // Job runs as super-admin to bypass tenant query filters.
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var tenantScope = currentTenant.BeginScope(tenantId: null, membershipId: null, isSuperAdmin: true);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        // Fetch only subscriptions that MIGHT have transitioned status.
        // We don't filter by Status here because Active subscriptions can become PastDue,
        // and PastDue can become Expired.
        var candidates = await dbContext.TenantSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
            .Where(s => s.PeriodEnd <= now)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogDebug("No subscriptions need status update at {Now}.", now);
            return;
        }

        var transitions = 0;
        foreach (var subscription in candidates)
        {
            var effectiveStatus = subscription.ComputeEffectiveStatus(now);
            if (effectiveStatus == subscription.Status)
                continue;

            // Only persist transitions that the entity allows. Both PastDue and Expired
            // are valid downward transitions from Active.
            switch (effectiveStatus)
            {
                case SubscriptionStatus.PastDue:
                    subscription.MarkPastDue();
                    transitions++;
                    break;

                case SubscriptionStatus.Expired:
                    subscription.MarkExpired();
                    transitions++;
                    break;
            }
        }

        if (transitions == 0)
        {
            _logger.LogDebug("No subscription transitions needed at {Now}.", now);
            return;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "SubscriptionStatusUpdateJob updated {Count} subscriptions at {Now}.",
                transitions,
                now);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Some subscriptions were modified concurrently (e.g., admin reactivated mid-run).
            // Skip this iteration — next run will pick them up.
            _logger.LogWarning(ex, "Concurrency conflict updating subscription statuses. Will retry next interval.");
        }
    }
}
