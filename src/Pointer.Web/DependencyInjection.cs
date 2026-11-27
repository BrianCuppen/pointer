using Pointer.Core.Availability;
using Pointer.Core.Admin;
using Pointer.Core.Bookings;
using Pointer.Core.Data;
using Pointer.Core.Payments;
using Pointer.Core.Notifications;
using Pointer.Web.Notifications;
using Pointer.Web.Payments;

namespace Pointer.Web;

public static class DependencyInjection
{
    public static IServiceCollection AddPointerCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. Set the ConnectionStrings__Default environment variable.");

        services.AddDbContext<PointerDbContext>(o => o.UsePointerDatabase(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.Configure<BookingOptions>(configuration.GetSection(BookingOptions.SectionName));
        services.Configure<AvailabilityOptions>(configuration.GetSection(AvailabilityOptions.SectionName));
        services.AddScoped<AvailabilityService>();
        services.AddScoped<BookingService>();
        services.AddScoped<ShopAdminService>();
        services.AddScoped<ShopProvisioning>();

        services.Configure<PlatformFeeOptions>(configuration.GetSection(PlatformFeeOptions.SectionName));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        services.AddScoped<DepositService>();
        services.AddScoped<PaymentWebhookProcessor>();

        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddScoped<NotificationOutbox>();
        services.AddScoped<NotificationRenderer>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<NotificationScheduler>();
        if (string.IsNullOrEmpty(configuration[$"{EmailOptions.SectionName}:{nameof(EmailOptions.BrevoApiKey)}"]))
            services.AddSingleton<IEmailSender, LogOnlyEmailSender>();
        else
            services.AddHttpClient<IEmailSender, BrevoEmailSender>(c => c.BaseAddress = new Uri(BrevoEmailSender.BaseAddress));
        if (configuration.GetValue("Notifications:WorkerEnabled", true))
            services.AddHostedService<NotificationWorker>();
        return services;
    }
}
