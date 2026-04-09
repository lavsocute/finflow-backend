using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenant");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.TenantCode).HasColumnName("tenant_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.TenancyModel).HasColumnName("tenancy_model").HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.ConnectionString).HasColumnName("connection_string");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("VND");
        builder.Property(x => x.CompanyName).HasColumnName("company_name").HasMaxLength(150);
        builder.Property(x => x.TaxCode).HasColumnName("tax_code").HasMaxLength(14);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => x.TenantCode).IsUnique();
        builder.HasQueryFilter(x => x.IsActive);
    }
}
