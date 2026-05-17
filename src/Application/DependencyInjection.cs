using FinFlow.Application.Behaviors;
using FinFlow.Application.Common.Audit;
using FinFlow.Application.Tenant.Support;
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

        return services;
    }
}
