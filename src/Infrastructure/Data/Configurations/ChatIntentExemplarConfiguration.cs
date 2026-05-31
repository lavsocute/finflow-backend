using FinFlow.Application.Chat.Cascade;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace FinFlow.Infrastructure.Data.Configurations;

public class ChatIntentExemplarConfiguration : IEntityTypeConfiguration<ChatIntentExemplar>
{
    internal const int IntentExemplarEmbeddingDimensions = 2048;

    public void Configure(EntityTypeBuilder<ChatIntentExemplar> builder)
    {
        builder.ToTable("chat_intent_exemplars");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ExemplarText).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Language).IsRequired().HasMaxLength(16);
        builder.Property(e => e.IntentMode).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.IntentFamily).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.IntentTask).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Weight).IsRequired();
        builder.Property(e => e.EmbeddingModel).IsRequired().HasMaxLength(64);
        builder.Property(e => e.IsActive).IsRequired();
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).IsRequired();

        builder.Property(e => e.Embedding)
            .Metadata.SetValueComparer(new ValueComparer<float[]>(
                (left, right) =>
                    ReferenceEquals(left, right) ||
                    (left != null && right != null && left.SequenceEqual(right)),
                value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                value => value.ToArray()));

        builder.Property(e => e.Embedding).IsRequired();

        builder.HasIndex(e => new { e.IsActive, e.EmbeddingModel });
        builder.HasIndex(e => new { e.IdTenant, e.IsActive });
    }
}
