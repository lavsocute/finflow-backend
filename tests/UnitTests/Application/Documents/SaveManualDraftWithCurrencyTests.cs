using FinFlow.Application.Documents.Commands.SaveManualDraft;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using FinFlow.UnitTests.TestStubs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinFlow.UnitTests.Application.Documents;

public class SaveManualDraftWithCurrencyTests
{
    [Fact]
    public async Task Handle_OmitsCurrency_DefaultsToTenantBaseCurrencyAtUnitRate()
    {
        var (tenantId, membershipId) = (Guid.NewGuid(), Guid.NewGuid());
        var repo = new InMemoryDraftRepository();
        var handler = BuildHandler(repo, tenantCurrency: "VND");

        var result = await handler.Handle(BuildCommand(tenantId, membershipId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var draft = repo.Added.Single();
        Assert.Equal("VND", draft.CurrencyCode);
        Assert.Equal("VND", draft.BaseCurrencyCode);
        Assert.Equal(1m, draft.ExchangeRate);
    }

    [Fact]
    public async Task Handle_WithUsdCurrency_AutoFetchesRate()
    {
        var (tenantId, membershipId) = (Guid.NewGuid(), Guid.NewGuid());
        var repo = new InMemoryDraftRepository();
        var handler = BuildHandler(
            repo,
            tenantCurrency: "VND",
            rates: new Dictionary<string, decimal> { ["USD->VND"] = 25_450m });

        var cmd = BuildCommand(tenantId, membershipId, currencyCode: "USD");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var draft = repo.Added.Single();
        Assert.Equal("USD", draft.CurrencyCode);
        Assert.Equal("VND", draft.BaseCurrencyCode);
        Assert.Equal(25_450m, draft.ExchangeRate);
    }

    [Fact]
    public async Task Handle_WithExplicitRate_OverridesAutoFetch()
    {
        var (tenantId, membershipId) = (Guid.NewGuid(), Guid.NewGuid());
        var repo = new InMemoryDraftRepository();
        // Auto-fetch would return 25,450 — but caller overrides with company-internal rate.
        var handler = BuildHandler(
            repo,
            tenantCurrency: "VND",
            rates: new Dictionary<string, decimal> { ["USD->VND"] = 25_450m });

        var cmd = BuildCommand(tenantId, membershipId, currencyCode: "USD", exchangeRate: 25_000m);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var draft = repo.Added.Single();
        Assert.Equal(25_000m, draft.ExchangeRate);
    }

    [Fact]
    public async Task Handle_RateProviderUnavailable_FallsBackToBaseCurrency()
    {
        var (tenantId, membershipId) = (Guid.NewGuid(), Guid.NewGuid());
        var repo = new InMemoryDraftRepository();
        // Provider fails — handler must store as base currency at rate 1.0.
        var handler = BuildHandler(repo, tenantCurrency: "VND", rateServiceShouldFail: true);

        var cmd = BuildCommand(tenantId, membershipId, currencyCode: "USD");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var draft = repo.Added.Single();
        Assert.Equal("VND", draft.CurrencyCode);
        Assert.Equal(1m, draft.ExchangeRate);
    }

    [Fact]
    public async Task Handle_SameCurrencyAsTenantBase_StoresUnitRate()
    {
        var (tenantId, membershipId) = (Guid.NewGuid(), Guid.NewGuid());
        var repo = new InMemoryDraftRepository();
        var handler = BuildHandler(repo, tenantCurrency: "USD");

        var cmd = BuildCommand(tenantId, membershipId, currencyCode: "USD");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var draft = repo.Added.Single();
        Assert.Equal("USD", draft.CurrencyCode);
        Assert.Equal("USD", draft.BaseCurrencyCode);
        Assert.Equal(1m, draft.ExchangeRate);
    }

    [Fact]
    public async Task Handle_LowercaseCurrencyCode_NormalizesToUpper()
    {
        var (tenantId, membershipId) = (Guid.NewGuid(), Guid.NewGuid());
        var repo = new InMemoryDraftRepository();
        var handler = BuildHandler(
            repo,
            tenantCurrency: "VND",
            rates: new Dictionary<string, decimal> { ["EUR->VND"] = 27_500m });

        var cmd = BuildCommand(tenantId, membershipId, currencyCode: "eur");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var draft = repo.Added.Single();
        Assert.Equal("EUR", draft.CurrencyCode);
    }

    private static SaveManualDraftCommandHandler BuildHandler(
        InMemoryDraftRepository repo,
        string tenantCurrency,
        IDictionary<string, decimal>? rates = null,
        bool rateServiceShouldFail = false) =>
        new(
            repo,
            new NoOpUnitOfWork(),
            new StubTenantRepository(tenantCurrency),
            new StubExchangeRateService(rates as IReadOnlyDictionary<string, decimal>, rateServiceShouldFail),
            NullLogger<SaveManualDraftCommandHandler>.Instance);

    private static SaveManualDraftCommand BuildCommand(
        Guid tenantId,
        Guid membershipId,
        string? currencyCode = null,
        decimal? exchangeRate = null) =>
        new(
            tenantId,
            membershipId,
            "manual.pdf",
            "Acme Vendor",
            "INV-100",
            new DateOnly(2026, 5, 17),
            "Travel",
            null,
            100m,
            0m,
            100m,
            "staff@finflow.test",
            new[] { new SaveManualDraftLineItem("Taxi", 1m, 100m, 100m) },
            currencyCode,
            exchangeRate);

    private sealed class InMemoryDraftRepository : IUploadedDocumentDraftRepository
    {
        public List<UploadedDocumentDraft> Added { get; } = new();
        public void Add(UploadedDocumentDraft draft) => Added.Add(draft);
        public void Update(UploadedDocumentDraft draft) { }
        public Task<UploadedDocumentDraft?> GetByIdAsync(Guid id, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => Task.FromResult<UploadedDocumentDraft?>(null);
        public Task<UploadedDocumentDraft?> GetByIdAsync(Guid id, Guid tenantId, Guid membershipId, bool includeInactive, CancellationToken cancellationToken = default) => Task.FromResult<UploadedDocumentDraft?>(null);
        public Task<UploadedDocumentDraft?> GetByTenantIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<UploadedDocumentDraft?>(null);
        public Task<IReadOnlyList<UploadedDocumentDraft>> GetMyDocumentDraftsAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UploadedDocumentDraft>>(Array.Empty<UploadedDocumentDraft>());
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<UploadedDocumentDraft>> GetOwnedActiveAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<UploadedDocumentDraft>>(Array.Empty<UploadedDocumentDraft>());
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
