using FinFlow.Application.Bank;
using FinFlow.Application.Bank.Formatters;
using FinFlow.Application.Common.Audit;
using FinFlow.Application.Tenant.Support;
using FinFlow.Application.Vendors.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
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

        // Bank CSV export — formatters are stateless, share single instance.
        services.AddSingleton<IBankCsvFormatter, VietcombankCsvFormatter>();
        services.AddSingleton<IBankCsvFormatter, BidvBulkTransferCsvFormatter>();
        services.AddSingleton<IBankCsvFormatter, TechcombankCsvFormatter>();
        services.AddSingleton<IBankCsvFormatter, GenericCsvFormatter>();
        services.AddSingleton<BankCsvFormatterRegistry>();

        // Vendor auto-link resolver — Scoped because it shares the unit-of-work
        // with the document save path.
        services.AddScoped<IVendorLinkResolver, VendorLinkResolver>();

        return services;
    }
}
