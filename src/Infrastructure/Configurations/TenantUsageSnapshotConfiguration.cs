using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class TenantUsageSnapshotConfiguration : IEntityTypeConfiguration<TenantUsageSnapshot>
{
    public void Configure(EntityTypeBuilder<TenantUsageSnapshot> builder)
    {
        builder.ToTable("tenant_usage_snapshot");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Ignore(x => x.TenantId);

        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.PeriodStart).HasColumnName("period_start").HasColumnType("date").IsRequired();
        builder.Property(x => x.PeriodEnd).HasColumnName("period_end").HasColumnType("date").IsRequired();
        builder.Property(x => x.OcrPagesUsed).HasColumnName("ocr_pages_used").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ChatbotMessagesUsed).HasColumnName("chatbot_messages_used").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.StorageUsedBytes).HasColumnName("storage_used_bytes").HasDefaultValue(0L).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => new { x.IdTenant, x.PeriodStart, x.PeriodEnd })
            .IsUnique()
            .HasDatabaseName("ux_tenant_usage_snapshot_period");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.IdTenant)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
