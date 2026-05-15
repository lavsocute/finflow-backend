namespace FinFlow.Application.Common.Abstractions;

public interface IOtpOperationLockService
{
    Task<IAsyncDisposable?> AcquireLockAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);
}

public sealed class OtpLock : IAsyncDisposable
{
    private readonly Func<Task> _releaseFunc;
    private bool _disposed;

    private OtpLock(Func<Task> releaseFunc) => _releaseFunc = releaseFunc;

    /// <summary>
    /// A no-op lock used when Redis is unavailable (fail-open).
    /// Callers treat this as a successful lock acquisition.
    /// </summary>
    public static OtpLock NoOp { get; } = new(() => Task.CompletedTask);

    public static OtpLock Create(Func<Task> releaseFunc) => new(releaseFunc);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _releaseFunc();
    }
}