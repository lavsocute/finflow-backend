namespace FinFlow.Domain.Abstractions;

/// <summary>
/// Helpers for financial invariant validation shared across document/draft entities.
/// All comparisons use a tolerance of 0.01 (1 xu / 1 cent) for VND-precision math.
/// </summary>
public static class FinancialInvariants
{
    /// <summary>Tolerance for decimal comparisons in money calculations (1 xu).</summary>
    public const decimal Tolerance = 0.01m;

    /// <summary>Round a decimal value to 2 places using banker-safe AwayFromZero rule.</summary>
    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Two money values are equal within the tolerance.</summary>
    public static bool EqualsWithinTolerance(decimal a, decimal b) =>
        Math.Abs(a - b) <= Tolerance;

    /// <summary>
    /// Validates the financial breakdown for a document with optional discounts.
    /// Returns the first failing invariant via <paramref name="errorFactory"/> so callers
    /// can produce errors scoped to their entity (Draft vs ReviewedDocument).
    ///
    /// Invariants enforced (preserves legacy behaviour for backward compatibility):
    ///   - DocumentDiscountAmount &gt;= 0
    ///   - DocumentDiscountPercent ∈ [0, 100] when supplied
    ///   - DocumentDiscountAmount &lt;= Subtotal
    ///   - When both DocPercent and DocAmount supplied: round(Subtotal * Percent / 100) ≈ DocAmount
    ///   - TotalAmount &gt; 0
    ///   - Subtotal − DocumentDiscountAmount + Vat ≈ TotalAmount (UBL formula; reduces to legacy when DocAmount=0)
    ///
    /// NOTE: Each entity is responsible for its own ΣLineItem.Total invariant since the
    /// ReviewedDocument legacy convention enforces ΣLine = Total (tax-inclusive lines)
    /// while UploadedDocumentDraft historically did not.
    /// </summary>
    /// <param name="subtotal">Pre-tax subtotal as labelled by the source document.</param>
    /// <param name="documentDiscountPercent">Optional document-level discount percent [0,100].</param>
    /// <param name="documentDiscountAmount">Document-level discount amount (>= 0).</param>
    /// <param name="vat">VAT amount.</param>
    /// <param name="totalAmount">Final total. Must equal subtotal − documentDiscountAmount + vat (±tolerance).</param>
    /// <param name="errorFactory">Factory mapping a logical invariant key to an entity-specific Error.</param>
    public static Result ValidateBreakdown(
        decimal subtotal,
        decimal? documentDiscountPercent,
        decimal documentDiscountAmount,
        decimal vat,
        decimal totalAmount,
        FinancialErrorFactory errorFactory)
    {
        if (documentDiscountAmount < 0)
            return Result.Failure(errorFactory.DiscountAmountInvalid);

        if (documentDiscountPercent.HasValue &&
            (documentDiscountPercent.Value < 0 || documentDiscountPercent.Value > 100))
            return Result.Failure(errorFactory.DiscountPercentOutOfRange);

        var roundedTotal = RoundMoney(totalAmount);

        // Discount-specific invariants: only enforced when discount actually present.
        var hasDiscount = documentDiscountAmount > 0 || documentDiscountPercent.HasValue;
        if (hasDiscount)
        {
            var roundedSubtotal = RoundMoney(subtotal);

            if (documentDiscountAmount > roundedSubtotal + Tolerance)
                return Result.Failure(errorFactory.DocumentDiscountExceedsSubtotal);

            if (documentDiscountPercent.HasValue)
            {
                var expected = RoundMoney(roundedSubtotal * documentDiscountPercent.Value / 100m);
                if (!EqualsWithinTolerance(expected, documentDiscountAmount))
                    return Result.Failure(errorFactory.DocumentDiscountMismatch);
            }

            // UBL formula. Only enforce when discount present, so legacy tolerant
            // behaviour (Subtotal+Vat ≠ Total) is preserved for callers that opt out.
            var expectedTotal = RoundMoney(roundedSubtotal - documentDiscountAmount + vat);
            if (!EqualsWithinTolerance(expectedTotal, roundedTotal))
                return Result.Failure(errorFactory.FinancialBreakdownMismatch);
        }

        if (roundedTotal <= 0)
            return Result.Failure(errorFactory.TotalAmountInvalid);

        return Result.Success();
    }

    /// <summary>
    /// Strict variant: always enforces the UBL formula `Subtotal − DocumentDiscountAmount + Vat ≈ TotalAmount`.
    /// Used by entities (e.g. <c>ReviewedDocument</c>) where the breakdown must hold even without discount.
    /// </summary>
    public static Result ValidateBreakdownStrict(
        decimal subtotal,
        decimal? documentDiscountPercent,
        decimal documentDiscountAmount,
        decimal vat,
        decimal totalAmount,
        FinancialErrorFactory errorFactory)
    {
        var lenient = ValidateBreakdown(subtotal, documentDiscountPercent, documentDiscountAmount, vat, totalAmount, errorFactory);
        if (lenient.IsFailure)
            return lenient;

        if (documentDiscountAmount > 0 || documentDiscountPercent.HasValue)
            return Result.Success(); // UBL formula already enforced by lenient path.

        var roundedSubtotal = RoundMoney(subtotal);
        var roundedTotal = RoundMoney(totalAmount);
        var expectedTotal = RoundMoney(roundedSubtotal - documentDiscountAmount + vat);
        if (!EqualsWithinTolerance(expectedTotal, roundedTotal))
            return Result.Failure(errorFactory.FinancialBreakdownMismatch);

        return Result.Success();
    }
}

/// <summary>
/// Bundles the entity-specific error variants needed by <see cref="FinancialInvariants.ValidateBreakdown"/>.
/// </summary>
public sealed record FinancialErrorFactory(
    Error DiscountAmountInvalid,
    Error DiscountPercentOutOfRange,
    Error LineItemTotalsMismatch,
    Error DocumentDiscountExceedsSubtotal,
    Error DocumentDiscountMismatch,
    Error TotalAmountInvalid,
    Error FinancialBreakdownMismatch);
