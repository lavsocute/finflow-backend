using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("DEPARTMENT");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.IdTenant)
            .HasColumnName("ID_TENANT")
            .IsRequired();

        builder.Property(x => x.ParentId)
            .HasColumnName("PARENT_ID");

        builder.Property(x => x.Name)
            .HasColumnName("NAME")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasOne(x => x.Tenant)
            .WithMany(x => x.Departments)
            .HasForeignKey(x => x.IdTenant)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Accounts)
            .WithOne(x => x.Department)
            .HasForeignKey(x => x.IdDepartment)
            .OnDelete(DeleteBehavior.Restrict);
    }
}