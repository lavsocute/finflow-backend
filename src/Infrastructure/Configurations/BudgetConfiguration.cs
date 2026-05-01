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

        builder.Property(b => b.Id)
            .HasColumnName("id");

        builder.Property(b => b.IdTenant)
            .HasColumnName("id_tenant")
            .IsRequired();

        builder.Property(b => b.IdDepartment)
            .HasColumnName("id_department")
            .IsRequired();

        builder.Property(b => b.Month)
            .HasColumnName("month")
            .IsRequired();

        builder.Property(b => b.Year)
            .HasColumnName("year")
            .IsRequired();

        builder.Property(b => b.AllocatedAmount)
            .HasColumnName("allocated_amount")
            .HasPrecision(15, 2)
            .IsRequired();

        builder.Property(b => b.SpentAmount)
            .HasColumnName("spent_amount")
            .HasPrecision(15, 2)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(b => new { b.IdDepartment, b.Month, b.Year })
            .IsUnique()
            .HasDatabaseName("ix_budgets_dept_month_year");

        builder.HasIndex(b => b.IdTenant)
            .HasDatabaseName("ix_budgets_tenant_id");
    }
}