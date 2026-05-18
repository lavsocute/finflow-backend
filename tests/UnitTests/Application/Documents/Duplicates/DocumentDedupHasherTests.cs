using FinFlow.Application.Documents.Duplicates;
using Xunit;

namespace FinFlow.UnitTests.Application.Documents.Duplicates;

public class DocumentDedupHasherTests
{
    [Fact]
    public void Compute_SameInputs_DeterministicHash()
    {
        var h1 = DocumentDedupHasher.Compute("0123456789", "Acme Co", "INV-001", new DateOnly(2026, 5, 1), 1_500_000m);
        var h2 = DocumentDedupHasher.Compute("0123456789", "Acme Co", "INV-001", new DateOnly(2026, 5, 1), 1_500_000m);

        Assert.NotNull(h1);
        Assert.Equal(h1, h2);
        Assert.Equal(32, h1!.Length);   // 16 bytes hex
    }

    [Fact]
    public void Compute_DifferentInvoice_DifferentHash()
    {
        var h1 = DocumentDedupHasher.Compute("0123456789", "Acme", "INV-001", new DateOnly(2026, 5, 1), 1m);
        var h2 = DocumentDedupHasher.Compute("0123456789", "Acme", "INV-002", new DateOnly(2026, 5, 1), 1m);
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Compute_NormalizesInvoiceWhitespaceAndCasing()
    {
        var h1 = DocumentDedupHasher.Compute("0123456789", "Acme", "INV 001", new DateOnly(2026, 5, 1), 1m);
        var h2 = DocumentDedupHasher.Compute("0123456789", "Acme", "inv-001", new DateOnly(2026, 5, 1), 1m);
        var h3 = DocumentDedupHasher.Compute("0123456789", "Acme", "INV001", new DateOnly(2026, 5, 1), 1m);

        Assert.Equal(h1, h2);
        Assert.Equal(h1, h3);   // whitespace + dashes stripped + uppercase
    }

    [Fact]
    public void Compute_TaxIdMissing_FallsBackToVendorName()
    {
        var hashWithName = DocumentDedupHasher.Compute(null, "Acme Co", "INV-001", new DateOnly(2026, 5, 1), 100m);
        Assert.NotNull(hashWithName);
    }

    [Fact]
    public void Compute_NormalizesVendorDiacriticsAndPunctuation()
    {
        // Vietnamese with diacritics + punctuation
        var h1 = DocumentDedupHasher.Compute(null, "Cty TNHH ABC", "INV-001", new DateOnly(2026, 5, 1), 100m);
        var h2 = DocumentDedupHasher.Compute(null, "CTY TNHH ABC,", "INV-001", new DateOnly(2026, 5, 1), 100m);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Compute_BothInvoiceAndVendorMissing_ReturnsNull()
    {
        var hash = DocumentDedupHasher.Compute(null, "", "", new DateOnly(2026, 5, 1), 100m);
        Assert.Null(hash);
    }

    [Fact]
    public void Compute_TaxIdPreferredOverName()
    {
        // Different name, same tax id → same hash (tax id is more stable)
        var h1 = DocumentDedupHasher.Compute("0123456789", "Acme Co", "INV-001", new DateOnly(2026, 5, 1), 1m);
        var h2 = DocumentDedupHasher.Compute("0123456789", "ACME CORP", "INV-001", new DateOnly(2026, 5, 1), 1m);
        Assert.Equal(h1, h2);
    }
}
