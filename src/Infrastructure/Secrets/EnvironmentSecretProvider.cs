using FinFlow.Application.Common.Abstractions;

namespace FinFlow.Infrastructure.Secrets;

/// <summary>
/// Default secret provider that reads from process environment variables.
/// Suitable for local development. Production should override with a managed
/// secret store implementation (Azure Key Vault, etc.).
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    public string? Get(string key) =>
        string.IsNullOrWhiteSpace(key) ? null : Environment.GetEnvironmentVariable(key);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Get(key));
}
