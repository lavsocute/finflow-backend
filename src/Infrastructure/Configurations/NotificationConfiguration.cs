using FinFlow.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.RecipientMembershipId).HasColumnName("recipient_membership_id").IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("text").IsRequired();
        builder.Property(x => x.Severity).HasColumnName("severity").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.IsRead).HasColumnName("is_read").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ReadAt).HasColumnName("read_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        // Hot index for "my recent notifications" — recipient + unread first +
        // newest first.
        builder.HasIndex(x => new { x.RecipientMembershipId, x.IsRead, x.CreatedAt })
            .HasDatabaseName("ix_notification_recipient_unread_created");
        builder.HasIndex(x => new { x.IdTenant, x.CreatedAt });
    }
}
