namespace FinFlow.Application.Bank;

/// <summary>
/// Registry that holds all <see cref="IBankCsvFormatter"/> implementations indexed
/// by <see cref="IBankCsvFormatter.FormatCode"/>. Registered as Singleton — formatters
/// are stateless.
/// </summary>
public sealed class BankCsvFormatterRegistry
{
    private readonly IReadOnlyDictionary<string, IBankCsvFormatter> _formatters;

    public BankCsvFormatterRegistry(IEnumerable<IBankCsvFormatter> formatters)
    {
        _formatters = formatters.ToDictionary(f => f.FormatCode, StringComparer.OrdinalIgnoreCase);
    }

    public IBankCsvFormatter? Find(string code) =>
        _formatters.TryGetValue(code, out var formatter) ? formatter : null;

    public IReadOnlyList<IBankCsvFormatter> All => _formatters.Values.ToList();
}
