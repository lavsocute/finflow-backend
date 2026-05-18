using FinFlow.Application.Bank;
using HotChocolate.Types;

namespace FinFlow.Api.GraphQL.Bank;

[ExtendObjectType(typeof(global::Query))]
public sealed class BankExportQueries
{
    /// <summary>
    /// Catalog of bank CSV formats supported by the platform. Returned without
    /// authorization (the codes themselves are not sensitive). Frontend renders
    /// the dropdown the accountant uses when triggering an export.
    /// </summary>
    public IReadOnlyList<BankCsvFormatPayload> BankCsvFormats(
        [Service] BankCsvFormatterRegistry registry) =>
        registry.All
            .OrderBy(f => f.FormatCode, StringComparer.OrdinalIgnoreCase)
            .Select(BankCsvFormatPayload.From)
            .ToList();
}
