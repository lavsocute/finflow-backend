using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expense");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.IdDepartment).HasColumnName("id_department").IsRequired();
        builder.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(x => x.PaymentId).HasColumnName("payment_id").IsRequired();
        builder.Property(x => x.IdCategory).HasColumnName("category_id").IsRequired();
        builder.Property(x => x.VendorName).HasColumnName("vendor_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(15, 2).IsRequired();
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();
        builder.Property(x => x.AmountInBaseCurrency).HasColumnName("amount_in_base_currency").HasPrecision(15, 2).IsRequired();
        builder.Property(x => x.BaseCurrencyCode).HasColumnName("base_currency_code").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Month).HasColumnName("month").IsRequired();
        builder.Property(x => x.Year).HasColumnName("year").IsRequired();
        builder.Property(x => x.ExpenseDate).HasColumnName("expense_date").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedByMembershipId).HasColumnName("created_by_membership_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasColumnType("text");
        builder.Property(x => x.RejectedAt).HasColumnName("rejected_at");
        builder.Property(x => x.RejectedByMembershipId).HasColumnName("rejected_by_membership_id");
        builder.Property(x => x.ReopenedAt).HasColumnName("reopened_at");
        builder.Property(x => x.ReopenedReason).HasColumnName("reopened_reason").HasMaxLength(500);
        builder.Property(x => x.ReopenedByMembershipId).HasColumnName("reopened_by_membership_id");

        builder.Property(x => x.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasIndex(x => x.IdTenant);
        builder.HasIndex(x => x.IdDepartment);
        builder.HasIndex(x => x.DocumentId).IsUnique();
        builder.HasIndex(x => x.PaymentId).IsUnique();
        builder.HasIndex(x => x.IdCategory);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.Year, x.Month });
    }
}