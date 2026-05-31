using FinFlow.Application.Chat.Cascade;
using FinFlow.Application.Common.Audit;
using FinFlow.Application.Common.Notifications;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Chat;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace FinFlow.Infrastructure;

public class ApplicationDbContext : DbContext, IUnitOfWork
{
    internal const int DocumentChunkEmbeddingDimensions = 2048;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDomainEventAuditMapper? _auditMapper;
    private readonly IDomainEventNotificationMapper? _notificationMapper;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<ApplicationDbContext>? _logger;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenant currentTenant) : base(options)
    {
        _currentTenant = currentTenant;
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenant currentTenant,
        IDomainEventAuditMapper auditMapper,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ApplicationDbContext> logger) : base(options)
    {
        _currentTenant = currentTenant;
        _auditMapper = auditMapper;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenant currentTenant,
        IDomainEventAuditMapper auditMapper,
        IDomainEventNotificationMapper notificationMapper,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ApplicationDbContext> logger) : base(options)
    {
        _currentTenant = currentTenant;
        _auditMapper = auditMapper;
        _notificationMapper = notificationMapper;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public DbSet<EmailChallenge> EmailChallenges => Set<EmailChallenge>();
    public DbSet<ReviewedDocument> ReviewedDocuments => Set<ReviewedDocument>();
    public DbSet<UploadedDocumentDraft> UploadedDocumentDrafts => Set<UploadedDocumentDraft>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantUsageSnapshot> TenantUsageSnapshots => Set<TenantUsageSnapshot>();
    public DbSet<MemberUsageSnapshot> MemberUsageSnapshots => Set<MemberUsageSnapshot>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatIntentExemplar> ChatIntentExemplars => Set<ChatIntentExemplar>();
    public DbSet<FinFlow.Domain.ExchangeRates.ExchangeRateHistory> ExchangeRateHistory => Set<FinFlow.Domain.ExchangeRates.ExchangeRateHistory>();
    public DbSet<FinFlow.Domain.Employees.EmployeeReimbursementProfile> EmployeeReimbursementProfiles => Set<FinFlow.Domain.Employees.EmployeeReimbursementProfile>();
    public DbSet<FinFlow.Domain.Notifications.Notification> Notifications => Set<FinFlow.Domain.Notifications.Notification>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var provisionedTenantIds = ChangeTracker.Entries<Tenant>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IMultiTenant)
            {
                var tenantProp = entry.Property("IdTenant");
                var currentVal = GetTenantIdValue(tenantProp.CurrentValue);

                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    var originalVal = GetTenantIdValue(tenantProp.OriginalValue);

                    if (currentVal != originalVal && !_currentTenant.IsSuperAdmin)
                    {
                        throw new InvalidOperationException("Unauthorized: Cannot change entity ownership.");
                    }

                    if (!_currentTenant.IsSuperAdmin && !_currentTenant.IsAvailable)
                    {
                        throw new InvalidOperationException("Tenant context is missing. Cannot modify or delete tenant-scoped data.");
                    }

                    if (!_currentTenant.IsSuperAdmin && currentVal != _currentTenant.Id)
                    {
                        throw new InvalidOperationException(
                            $"Data isolation violation: Entity {entry.Entity.GetType().Name} belongs to tenant {currentVal} but current tenant is {_currentTenant.Id}.");
                    }
                }

                if (entry.State == EntityState.Added)
                {
                    if (_currentTenant.IsAvailable)
                    {
                        // Normal tenant-scoped request: auto-assign when missing and block cross-tenant writes.
                        if (currentVal == null || currentVal == Guid.Empty)
                        {
                            tenantProp.CurrentValue = _currentTenant.Id;
                        }
                        else if (currentVal != _currentTenant.Id && !_currentTenant.IsSuperAdmin)
                        {
                            throw new InvalidOperationException(
                                $"Data isolation violation: Entity {entry.Entity.GetType().Name} belongs to tenant {currentVal} but current tenant is {_currentTenant.Id}.");
                        }
                    }
                    else
                    {
                        // Pre-auth onboarding is allowed only while provisioning a brand-new tenant
                        // in the same transaction (for example Register => Tenant + Department + Account).
                        if (currentVal == null || currentVal == Guid.Empty)
                        {
                            throw new InvalidOperationException(
                                "Tenant context is missing. Cannot create tenant-scoped data without explicit assignment.");
                        }

                        if (!provisionedTenantIds.Contains(currentVal.Value))
                        {
                            throw new InvalidOperationException(
                                "Tenant context is missing. Pre-auth tenant-scoped creation is allowed only for a newly created tenant.");
                        }
                    }
                }
            }

            if (entry.State == EntityState.Added && entry.Entity is Entity entity && entity.Id == Guid.Empty)
            {
                entry.Property("Id").CurrentValue = Guid.NewGuid();
            }

            if (entry.State == EntityState.Deleted && entry.Entity is Entity)
            {
                // Only soft-delete entities marked with ISoftDeletable.
                // Other entities (e.g., DocumentChunk, RefreshToken, EmailChallenge,
                // ChatMessage, AuditLog, Payment, Expense, Budget) are hard-deleted.
                if (entry.Entity is ISoftDeletable && entry.Metadata.FindProperty("IsActive") != null)
                {
                    entry.State = EntityState.Modified;
                    entry.Property("IsActive").CurrentValue = false;
                }
            }
        }

        var affected = await base.SaveChangesAsync(cancellationToken);
        await DispatchDomainEventsAsync(cancellationToken);
        return affected;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        if (_auditMapper is null && _notificationMapper is null)
            return;

        var entitiesWithEvents = ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(entity => entity.GetDomainEvents().Count > 0)
            .ToList();

        if (entitiesWithEvents.Count == 0)
            return;

        var accountId = ResolveAccountId();
        var tenantId = _currentTenant.Id;

        var auditLogs = new List<AuditLog>();
        var notifications = new List<FinFlow.Domain.Notifications.Notification>();

        foreach (var entity in entitiesWithEvents)
        {
            foreach (var domainEvent in entity.GetDomainEvents())
            {
                if (_auditMapper is not null)
                {
                    try
                    {
                        var log = _auditMapper.Map(domainEvent, tenantId, accountId);
                        if (log is not null)
                            auditLogs.Add(log);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(
                            ex,
                            "Failed to map domain event {EventType} to audit log for entity {EntityType}#{EntityId}.",
                            domainEvent.GetType().Name, entity.GetType().Name, entity.Id);
                    }
                }

                if (_notificationMapper is not null)
                {
                    try
                    {
                        var built = await _notificationMapper.MapAsync(domainEvent, tenantId, cancellationToken);
                        if (built.Count > 0)
                            notifications.AddRange(built);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(
                            ex,
                            "Failed to map domain event {EventType} to notifications for entity {EntityType}#{EntityId}.",
                            domainEvent.GetType().Name, entity.GetType().Name, entity.Id);
                    }
                }
            }
            entity.ClearDomainEvents();
        }

        if (auditLogs.Count == 0 && notifications.Count == 0)
            return;

        try
        {
            if (auditLogs.Count > 0)
                Set<AuditLog>().AddRange(auditLogs);
            if (notifications.Count > 0)
                Set<FinFlow.Domain.Notifications.Notification>().AddRange(notifications);
            await base.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to persist {AuditCount} audit log entries and {NotificationCount} notifications for domain events.",
                auditLogs.Count, notifications.Count);
        }
    }

    private Guid? ResolveAccountId()
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        if (user is null)
            return null;

        var rawAccountId = user.FindFirst("sub")?.Value
            ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        return Guid.TryParse(rawAccountId, out var id) ? id : null;
    }

    private static Guid? GetTenantIdValue(object? value) =>
        value is Guid guid ? guid : null;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ConfigureDocumentChunkModel(builder);

        builder.Entity<Account>().HasQueryFilter(e => e.IsActive);

        builder.Entity<Department>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<TenantMembership>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<Invitation>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<ReviewedDocument>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<UploadedDocumentDraft>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<TenantSubscription>().HasQueryFilter(e =>
            e.Status == SubscriptionStatus.Active &&
            (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<TenantUsageSnapshot>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<MemberUsageSnapshot>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<Budget>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<AuditLog>().HasQueryFilter(e =>
            _currentTenant.IsSuperAdmin || (e.IdTenant == _currentTenant.Id));

        builder.Entity<Category>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<Payment>().HasQueryFilter(e =>
            _currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id));

        builder.Entity<PaymentRefund>().HasQueryFilter(e =>
            _currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id));

        builder.Entity<Expense>().HasQueryFilter(e =>
            _currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id));

        builder.Entity<ChatSession>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || e.IdTenant == _currentTenant.Id));

        builder.Entity<Vendor>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id)));

        builder.Entity<DocumentChunk>().HasQueryFilter(e =>
            _currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id));

        builder.Entity<FinFlow.Domain.Employees.EmployeeReimbursementProfile>().HasQueryFilter(e =>
            _currentTenant.IsSuperAdmin || ((Guid?)e.IdTenant == _currentTenant.Id));

        // Note: ChatMessage does not have IdTenant directly.
        // Tenant isolation is enforced via ChatSession's query filter.
        // Always query messages through their session relationship.
    }

    private void ConfigureDocumentChunkModel(ModelBuilder builder)
    {
        var documentChunk = builder.Entity<DocumentChunk>();

        documentChunk.Property(x => x.Embedding)
            .Metadata.SetValueComparer(new ValueComparer<float[]>(
                (left, right) =>
                    ReferenceEquals(left, right) ||
                    (left != null && right != null && left.SequenceEqual(right)),
                value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                value => value.ToArray()));

        documentChunk.Property(x => x.Embedding)
            .IsRequired();

        documentChunk.HasIndex(x => x.IdTenant);
        documentChunk.HasIndex(x => new { x.IdTenant, x.Type });
        documentChunk.HasIndex(x => new { x.IdTenant, x.OwnerMembershipId });
        documentChunk.HasIndex(x => new { x.IdTenant, x.DepartmentId });

        if (!Database.IsNpgsql())
            return;

        builder.HasPostgresExtension("vector");

        documentChunk.Property(x => x.Embedding)
            .HasConversion(new ValueConverter<float[], Vector>(
                value => new Vector(value),
                value => value.ToArray()))
            .HasColumnType($"vector({DocumentChunkEmbeddingDimensions})");

        var intentExemplar = builder.Entity<ChatIntentExemplar>();
        intentExemplar.Property(x => x.Embedding)
            .HasConversion(new ValueConverter<float[], Vector>(
                value => new Vector(value),
                value => value.ToArray()))
            .HasColumnType($"vector({DocumentChunkEmbeddingDimensions})");
    }
}
