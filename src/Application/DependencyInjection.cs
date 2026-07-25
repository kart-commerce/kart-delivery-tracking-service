using FluentValidation;
using KartDeliveryTrackingService.Application.Common.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace KartDeliveryTrackingService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);

            // Registration order is pipeline order (outermost first) - Logging wraps Validation
            // so every request's completion/duration is observed uniformly.
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
