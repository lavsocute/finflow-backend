using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class ReviewedDocumentConfiguration : IEntityTypeConfiguration<ReviewedDocument>
{
    public void Configure(EntityTypeBuilder<ReviewedDocument> builder)
    {
        builder.ToTable("reviewed_document");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.MembershipId).HasColumnName("membership_id").IsRequired();
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.VendorName).HasColumnName("vendor_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(100).IsRequired();
        builder.Property(x => x.DocumentDate).HasColumnName("document_date").IsRequired();
        builder.Property(x => x.DueDate).HasColumnName("due_date").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
        builder.Property(x => x.VendorTaxId).HasColumnName("vendor_tax_id").HasMaxLength(50);
        builder.Property(x => x.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Vat).HasColumnName("vat").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReviewedByStaff).HasColumnName("reviewed_by_staff").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ConfidenceLabel).HasColumnName("confidence_label").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => new { x.IdTenant, x.Status, x.SubmittedAt });

        builder.OwnsMany(x => x.LineItems, ownedBuilder =>
        {
            ownedBuilder.ToTable("reviewed_document_line_item");
            ownedBuilder.WithOwner().HasForeignKey("reviewed_document_id");

            ownedBuilder.HasKey(x => x.Id);
            ownedBuilder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            ownedBuilder.Property(x => x.ItemName).HasColumnName("item_name").HasMaxLength(250).IsRequired();
            ownedBuilder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,2)").IsRequired();
            ownedBuilder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,2)").IsRequired();
            ownedBuilder.Property(x => x.Total).HasColumnName("total").HasColumnType("numeric(18,2)").IsRequired();
        });

        builder.Navigation(x => x.LineItems).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
