using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Application.Options;
using SchoolAdmission.Application.Services;

namespace SchoolAdmission.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdmissionOptions>()
            .Configure(o => o.HoldDurationSeconds =
                configuration.GetValue(AdmissionOptions.HoldDurationKey, o.HoldDurationSeconds))
            .Validate(o => o.HoldDurationSeconds > 0, $"{AdmissionOptions.HoldDurationKey} must be a positive integer.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IClassService, ClassService>();
        services.AddScoped<IHoldService, HoldService>();
        return services;
    }
}
