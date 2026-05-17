using FinFlow.Domain.ExchangeRates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Configurations;

public sealed class ExchangeRateHistoryConfiguration : IEntityTypeConfiguration<ExchangeRateHistory>
{
    public void Configure(EntityTypeBuilder<ExchangeRateHistory> builder)
    {
        builder.ToTable("exchange_rate_history");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FromCurrency).HasColumnName("from_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ToCurrency).HasColumnName("to_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.RateDate).HasColumnName("rate_date").IsRequired();
        builder.Property(x => x.Rate).HasColumnName("rate").HasColumnType("numeric(18,6)").IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedByMembershipId).HasColumnName("created_by_membership_id");

        builder.HasIndex(x => new { x.FromCurrency, x.ToCurrency, x.RateDate, x.Source })
            .IsUnique()
            .HasDatabaseName("uq_exchange_rate_key");

        builder.HasIndex(x => new { x.FromCurrency, x.ToCurrency, x.RateDate })
            .HasDatabaseName("ix_exchange_rate_lookup");
    }
}
