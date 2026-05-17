using System.Text;
using System.Text.Json;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Interfaces;
using FinFlow.Infrastructure.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinFlow.UnitTests.Infrastructure.Audit;

public class IdempotencyMiddlewareTests
{
    private const string ValidKey = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public async Task NoIdempotencyKey_PassesThrough()
    {
        var nextCalled = 0;
        var middleware = new IdempotencyMiddleware(_ => { nextCalled++; return Task.CompletedTask; }, NullLogger<IdempotencyMiddleware>.Instance);
        var ctx = BuildContext("""{"query":"mutation { recordPayment(input:{}) { id } }"}""", header: null);
        var cache = new InMemoryCache();

        await middleware.InvokeAsync(ctx, cache, new StubCurrentTenant());

        Assert.Equal(1, nextCalled);
        Assert.Empty(cache.Store);
    }

    [Fact]
    public async Task InvalidKey_Returns400StyleGraphqlError()
    {
        var nextCalled = 0;
        var middleware = new IdempotencyMiddleware(_ => { nextCalled++; return Task.CompletedTask; }, NullLogger<IdempotencyMiddleware>.Instance);
        var ctx = BuildContext("""{"query":"mutation { recordPayment }"}""", header: "not-a-uuid");
        var cache = new InMemoryCache();

        await middleware.InvokeAsync(ctx, cache, new StubCurrentTenant());

        Assert.Equal(0, nextCalled);
        var body = ReadResponse(ctx);
        Assert.Contains("Idempotency.InvalidKey", body);
    }

    [Fact]
    public async Task NonCriticalMutation_BypassesCache()
    {
        var nextCalled = 0;
        var middleware = new IdempotencyMiddleware(_ => { nextCalled++; return Task.CompletedTask; }, NullLogger<IdempotencyMiddleware>.Instance);
        var ctx = BuildContext("""{"query":"mutation { someOtherMutation }"}""", header: ValidKey);
        var cache = new InMemoryCache();

        await middleware.InvokeAsync(ctx, cache, new StubCurrentTenant());

        Assert.Equal(1, nextCalled);
        Assert.Empty(cache.Store);
    }

    [Fact]
    public async Task FirstRequest_PopulatesCache()
    {
        var middleware = new IdempotencyMiddleware(async c =>
        {
            c.Response.StatusCode = 200;
            c.Response.ContentType = "application/json";
            await c.Response.WriteAsync("""{"data":{"recordPayment":{"id":"abc"}}}""");
        }, NullLogger<IdempotencyMiddleware>.Instance);

        var ctx = BuildContext("""{"query":"mutation { recordPayment(input:{x:1}) { id } }"}""", header: ValidKey);
        var cache = new InMemoryCache();

        await middleware.InvokeAsync(ctx, cache, new StubCurrentTenant());

        Assert.Single(cache.Store);
        var body = ReadResponse(ctx);
        Assert.Contains("recordPayment", body);
    }

    [Fact]
    public async Task ReplaySameKeyAndBody_ReturnsCachedResponse()
    {
        var middleware = new IdempotencyMiddleware(async c =>
        {
            c.Response.StatusCode = 200;
            c.Response.ContentType = "application/json";
            await c.Response.WriteAsync("""{"data":{"id":"xyz"}}""");
        }, NullLogger<IdempotencyMiddleware>.Instance);

        var cache = new InMemoryCache();
        const string body = """{"query":"mutation { recordPayment(input:{x:1}) { id } }"}""";

        // First call populates cache
        var ctx1 = BuildContext(body, header: ValidKey);
        await middleware.InvokeAsync(ctx1, cache, new StubCurrentTenant());

        // Second call should hit cache
        var nextCalled = 0;
        var middleware2 = new IdempotencyMiddleware(_ => { nextCalled++; return Task.CompletedTask; }, NullLogger<IdempotencyMiddleware>.Instance);
        var ctx2 = BuildContext(body, header: ValidKey);
        await middleware2.InvokeAsync(ctx2, cache, new StubCurrentTenant());

        Assert.Equal(0, nextCalled);
        var responseBody = ReadResponse(ctx2);
        Assert.Contains("\"id\":\"xyz\"", responseBody);
    }

    [Fact]
    public async Task SameKeyDifferentBody_ReturnsConflict()
    {
        var middleware = new IdempotencyMiddleware(async c =>
        {
            c.Response.StatusCode = 200;
            c.Response.ContentType = "application/json";
            await c.Response.WriteAsync("""{"data":{}}""");
        }, NullLogger<IdempotencyMiddleware>.Instance);

        var cache = new InMemoryCache();

        var ctx1 = BuildContext("""{"query":"mutation { recordPayment(x:1) { id } }"}""", header: ValidKey);
        await middleware.InvokeAsync(ctx1, cache, new StubCurrentTenant());

        var nextCalled = 0;
        var middleware2 = new IdempotencyMiddleware(_ => { nextCalled++; return Task.CompletedTask; }, NullLogger<IdempotencyMiddleware>.Instance);
        var ctx2 = BuildContext("""{"query":"mutation { recordPayment(x:2) { id } }"}""", header: ValidKey);
        await middleware2.InvokeAsync(ctx2, cache, new StubCurrentTenant());

        Assert.Equal(0, nextCalled);
        var body = ReadResponse(ctx2);
        Assert.Contains("Idempotency.KeyReuseConflict", body);
    }

    [Fact]
    public async Task CacheBackendDown_FailsOpen()
    {
        var nextCalled = 0;
        var middleware = new IdempotencyMiddleware(c => { nextCalled++; c.Response.StatusCode = 200; return Task.CompletedTask; }, NullLogger<IdempotencyMiddleware>.Instance);
        var ctx = BuildContext("""{"query":"mutation { recordPayment(x:1) { id } }"}""", header: ValidKey);
        var cache = new ThrowingCache();

        await middleware.InvokeAsync(ctx, cache, new StubCurrentTenant());

        Assert.Equal(1, nextCalled);
    }

    private static HttpContext BuildContext(string body, string? header)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = "/graphql";
        if (header != null)
            ctx.Request.Headers["Idempotency-Key"] = header;

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        ctx.Request.Body = new MemoryStream(bodyBytes);
        ctx.Request.ContentLength = bodyBytes.Length;

        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static string ReadResponse(HttpContext context)
    {
        if (context.Response.Body is not MemoryStream ms)
            return string.Empty;
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private sealed class InMemoryCache : ICacheService
    {
        public Dictionary<string, object> Store { get; } = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class =>
            Task.FromResult(Store.TryGetValue(key, out var v) ? (T?)v : null);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            Store[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Store.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
        {
            foreach (var k in Store.Keys.Where(k => k.StartsWith(keyPrefix)).ToList())
                Store.Remove(k);
            return Task.CompletedTask;
        }

        public Task<long> IncrementWithExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
        {
            if (Store.TryGetValue(key, out var v)) return (T)v;
            var value = await factory();
            Store[key] = value!;
            return value;
        }
    }

    private sealed class ThrowingCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class =>
            throw new InvalidOperationException("cache down");

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class =>
            throw new InvalidOperationException("cache down");

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> IncrementWithExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class =>
            throw new InvalidOperationException("cache down");
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public Guid? Id => Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        public Guid? MembershipId => null;
        public bool IsAvailable => true;
        public bool IsSuperAdmin => false;
        public IDisposable BeginScope(Guid? tenantId, Guid? membershipId = null, bool isSuperAdmin = false) =>
            new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
