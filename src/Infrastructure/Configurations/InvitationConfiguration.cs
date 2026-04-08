using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitation");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IdTenant).HasColumnName("id_tenant").IsRequired();
        builder.Property(x => x.InvitedByMembershipId).HasColumnName("invited_by_membership_id").IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.IdTenant);
        builder.HasIndex(x => new { x.IdTenant, x.Email });

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.IdTenant).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TenantMembership>().WithMany().HasForeignKey(x => x.InvitedByMembershipId).OnDelete(DeleteBehavior.Restrict);
    }
}
