using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.ToTable("tenant_subscription");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Ignore(x => x.TenantId);

        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.PlanTier).HasColumnName("plan_tier").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.PeriodStart).HasColumnName("period_start").HasColumnType("date").IsRequired();
        builder.Property(x => x.PeriodEnd).HasColumnName("period_end").HasColumnType("date").IsRequired();

        builder.HasIndex(x => x.IdTenant).IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.IdTenant)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
