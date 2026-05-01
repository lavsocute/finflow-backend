using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payment");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(x => x.IdDepartment).HasColumnName("id_department").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(15, 2).IsRequired();
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasConversion<string>().HasMaxLength(3).IsRequired();
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").HasPrecision(18, 6).IsRequired();
        builder.Property(x => x.AmountInVnd).HasColumnName("amount_in_vnd").HasPrecision(15, 2).IsRequired();
        builder.Property(x => x.RecordedByMembershipId).HasColumnName("recorded_by_membership_id").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.Method).HasColumnName("payment_method").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConfirmedByMembershipId).HasColumnName("confirmed_by_membership_id");
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.IdTenant);
        builder.HasIndex(x => x.DocumentId).IsUnique();
        builder.HasIndex(x => x.IdDepartment);
        builder.HasIndex(x => x.Status);
    }
}