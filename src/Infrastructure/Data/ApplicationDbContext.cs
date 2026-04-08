using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure;

public class ApplicationDbContext : DbContext, IUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenant currentTenant) : base(options)
    {
        _currentTenant = currentTenant;
    }

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
                entry.State = EntityState.Modified;
                if (entry.Metadata.FindProperty("IsActive") != null)
                {
                    entry.Property("IsActive").CurrentValue = false;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private static Guid? GetTenantIdValue(object? value) =>
        value is Guid guid ? guid : null;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        builder.Entity<Account>().HasQueryFilter(e => e.IsActive);

        builder.Entity<Department>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || (_currentTenant.Id.HasValue && e.IdTenant == _currentTenant.Id.Value)));

        builder.Entity<TenantMembership>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || (_currentTenant.Id.HasValue && e.IdTenant == _currentTenant.Id.Value)));

        builder.Entity<Invitation>().HasQueryFilter(e =>
            e.IsActive && (_currentTenant.IsSuperAdmin || (_currentTenant.Id.HasValue && e.IdTenant == _currentTenant.Id.Value)));

        builder.Entity<AuditLog>().HasQueryFilter(e =>
            _currentTenant.IsSuperAdmin || (_currentTenant.Id.HasValue && e.IdTenant == _currentTenant.Id.Value));
    }
}
