using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        builder.ToTable("vendor");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.TaxCode).HasColumnName("tax_code").HasMaxLength(14).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsVerified).HasColumnName("is_verified").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.VerifiedByMembershipId).HasColumnName("verified_by_membership_id");
        builder.Property(x => x.VerifiedAt).HasColumnName("verified_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => new { x.IdTenant, x.TaxCode }).IsUnique();
        builder.HasIndex(x => x.IsVerified);

        builder.HasQueryFilter(x => x.IsActive);
    }
}