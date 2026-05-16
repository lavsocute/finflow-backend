namespace FinFlow.Domain.Abstractions;

public static class SharedErrors
{
    public static readonly Error ConcurrencyConflict = new(
        "Shared.ConcurrencyConflict",
        "The resource was modified by another request. Please reload and try again.");
}
