using FinFlow.Domain.Interfaces;

namespace FinFlow.Infrastructure.Security;

/// <summary>
/// Implementation of <see cref="ICurrentTenant"/> backed by AsyncLocal so context
/// flows correctly across async/await without leaking across requests.
///
/// Two layers of state:
///   1. Base context — set once per request by TenantMiddleware via <see cref="ICurrentTenantWriter"/>.
///   2. Override stack — pushed/popped by Application code via <see cref="BeginScope"/>.
///
/// The override stack is LIFO and IDisposable-scoped. Nested scopes are supported.
/// On scope dispose, the previous context is restored automatically (exception-safe).
/// </summary>
public sealed class CurrentTenant : ICurrentTenant, ICurrentTenantWriter
{
    private static readonly AsyncLocal<TenantContext> _baseContext = new();
    private static readonly AsyncLocal<Stack<TenantContext>?> _overrideStack = new();

    public Guid? Id => Current.TenantId;
    public Guid? MembershipId => Current.MembershipId;
    public bool IsSuperAdmin => Current.IsSuperAdmin;
    public bool IsAvailable => Current.TenantId.HasValue;

    public void SetFromRequest(Guid? tenantId, Guid? membershipId, bool isSuperAdmin)
    {
        _baseContext.Value = new TenantContext(tenantId, membershipId, isSuperAdmin);
    }

    public IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false)
    {
        var stack = _overrideStack.Value ??= new Stack<TenantContext>();
        var newContext = new TenantContext(tenantId, membershipId, isSuperAdmin);
        stack.Push(newContext);
        return new Scope(this);
    }

    private TenantContext Current
    {
        get
        {
            var stack = _overrideStack.Value;
            if (stack != null && stack.Count > 0)
                return stack.Peek();
            return _baseContext.Value;
        }
    }

    private void PopOverride()
    {
        var stack = _overrideStack.Value;
        if (stack != null && stack.Count > 0)
        {
            stack.Pop();
            if (stack.Count == 0)
                _overrideStack.Value = null;
        }
    }

    private readonly struct TenantContext
    {
        public TenantContext(Guid? tenantId, Guid? membershipId, bool isSuperAdmin)
        {
            TenantId = tenantId;
            MembershipId = membershipId;
            IsSuperAdmin = isSuperAdmin;
        }

        public Guid? TenantId { get; }
        public Guid? MembershipId { get; }
        public bool IsSuperAdmin { get; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly CurrentTenant _owner;
        private bool _disposed;

        public Scope(CurrentTenant owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.PopOverride();
        }
    }
}
