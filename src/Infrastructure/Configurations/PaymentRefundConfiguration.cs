using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

public sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("payment_refund");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.PaymentId).HasColumnName("payment_id").IsRequired();
        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(15, 2).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        builder.Property(x => x.InitiatedByMembershipId).HasColumnName("initiated_by_membership_id").IsRequired();
        builder.Property(x => x.InitiatedAt).HasColumnName("initiated_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasIndex(x => x.PaymentId);
        builder.HasIndex(x => x.IdTenant);
        builder.HasIndex(x => x.Status);
    }
}
