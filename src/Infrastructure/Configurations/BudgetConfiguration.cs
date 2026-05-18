using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

public sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(b => b.IdDepartment).HasColumnName("id_department").IsRequired();
        builder.Property(b => b.Month).HasColumnName("month").IsRequired();
        builder.Property(b => b.Year).HasColumnName("year").IsRequired();

        builder.Property(b => b.AllocatedAmount)
            .HasColumnName("allocated_amount")
            .HasPrecision(15, 2)
            .IsRequired();

        builder.Property(b => b.CommittedAmount)
            .HasColumnName("committed_amount")
            .HasPrecision(15, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.SpentAmount)
            .HasColumnName("spent_amount")
            .HasPrecision(15, 2)
            .IsRequired();

        builder.Property(b => b.CarryOverFromPreviousMonth)
            .HasColumnName("carry_over_from_prev")
            .HasPrecision(15, 2);

        builder.Property(b => b.BaseCurrencyCode)
            .HasColumnName("base_currency_code")
            .HasMaxLength(3)
            .HasDefaultValue("VND")
            .IsRequired();

        builder.Property(b => b.EnforcementMode)
            .HasColumnName("enforcement_mode")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(FinFlow.Domain.Enums.BudgetEnforcementMode.SoftBlock)
            .IsRequired();

        builder.Property(b => b.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(b => b.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Same-tenant FK enforcement at DB level — defends against cross-tenant
        // attacks even when application checks are bypassed.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(b => b.IdTenant)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(b => b.IdDepartment)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => new { b.IdDepartment, b.Month, b.Year })
            .IsUnique()
            .HasDatabaseName("ix_budgets_dept_month_year");

        builder.HasIndex(b => b.IdTenant)
            .HasDatabaseName("ix_budgets_tenant_id");

        // Hot-path index for "all active budgets in tenant for period".
        builder.HasIndex(b => new { b.IdTenant, b.Year, b.Month, b.IsActive })
            .HasDatabaseName("ix_budgets_tenant_period_active");
    }
}
