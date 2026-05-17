namespace FinFlow.Application.Common.Abstractions;

/// <summary>
/// Abstraction for secret retrieval. Default impl reads from environment variables;
/// production may swap to Azure Key Vault / AWS Secrets Manager / HashiCorp Vault.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Returns the secret value for <paramref name="key"/> or null if not present.
    /// Implementations should cache locally to avoid hitting the backend on every call.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous read for hot paths (DI registration, options binding).
    /// </summary>
    string? Get(string key);
}
