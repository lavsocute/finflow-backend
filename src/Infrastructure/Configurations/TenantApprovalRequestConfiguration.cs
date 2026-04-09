using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class TenantApprovalRequestConfiguration : IEntityTypeConfiguration<TenantApprovalRequest>
{
    public void Configure(EntityTypeBuilder<TenantApprovalRequest> builder)
    {
        builder.ToTable("tenant_approval_request");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantCode).HasColumnName("tenant_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.CompanyName).HasColumnName("company_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.TaxCode).HasColumnName("tax_code").HasMaxLength(14).IsRequired();
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(500);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(15);
        builder.Property(x => x.ContactPerson).HasColumnName("contact_person").HasMaxLength(100);
        builder.Property(x => x.BusinessType).HasColumnName("business_type").HasMaxLength(50);
        builder.Property(x => x.EmployeeCount).HasColumnName("employee_count");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("VND").IsRequired();
        builder.Property(x => x.TenancyModel).HasColumnName("tenancy_model").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.RequestedById).HasColumnName("requested_by_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.RejectedAt).HasColumnName("rejected_at");
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.TenantCode);
        builder.HasIndex(x => new { x.RequestedById, x.Status });

        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
    }
}
