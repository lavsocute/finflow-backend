using FinFlow.Application.Bank;
using FinFlow.Application.Bank.Formatters;
using FinFlow.Application.Common.Audit;
using FinFlow.Application.Tenant.Support;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;
using FinFlow.Application.Vendors.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace FinFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddBehavior(typeof(MediatR.IPipelineBehavior<,>), typeof(Behaviors.ValidationBehavior<,>));
        });
        services.AddScoped<TenantCreationActorAuthorizationService>();
        services.AddScoped<IDomainEventAuditMapper, DomainEventAuditMapper>();
        services.AddScoped<Common.Notifications.IDomainEventNotificationMapper, Common.Notifications.DomainEventNotificationMapper>();

        // Bank CSV export — formatters are stateless, share single instance.
        services.AddSingleton<IBankCsvFormatter, VietcombankCsvFormatter>();
        services.AddSingleton<IBankCsvFormatter, BidvBulkTransferCsvFormatter>();
        services.AddSingleton<IBankCsvFormatter, TechcombankCsvFormatter>();
        services.AddSingleton<IBankCsvFormatter, GenericCsvFormatter>();
        services.AddSingleton<BankCsvFormatterRegistry>();

        // Vendor auto-link resolver — Scoped because it shares the unit-of-work
        // with the document save path.
        services.AddScoped<IVendorLinkResolver, VendorLinkResolver>();
        services.AddScoped<IChatPolicyEngine, ChatPolicyEngine>();

        // LLM Entity Extractor — registered via factory because HttpClient wiring
        // (BaseAddress, auth headers) lives in Infrastructure/DependencyInjection.cs
        services.AddScoped<ILlmEntityExtractor>(sp =>
            new LlmEntityExtractor(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<IOptions<LlmEntityExtractorOptions>>(),
                sp.GetRequiredService<ILogger<LlmEntityExtractor>>(),
                sp.GetRequiredService<ITextNormalizer>()));

        // Text normalization — singleton as it's stateless
        services.AddSingleton<ITextNormalizer, TextNormalizer>();

        // Context management services — singleton for stateless operations
        services.AddSingleton<IConfidenceScorer, ConfidenceScorer>();
        services.AddSingleton<IContextResolver, ContextResolver>();
        services.AddSingleton<IHybridResolutionRouter, HybridResolutionRouter>();
        services.AddSingleton<IContextSummarizationService, ContextSummarizationService>();
        services.AddSingleton<IConversationStateManager, ConversationStateManager>();

        // Budget reservation pipeline. Both services are Scoped — they share
        // the active DbContext + unit-of-work with the lifecycle handlers.
        services.AddScoped<Budgets.Services.IBudgetGuard, Budgets.Services.BudgetGuard>();
        services.AddScoped<Budgets.Services.IBudgetReservationService, Budgets.Services.BudgetReservationService>();

        return services;
    }
}
