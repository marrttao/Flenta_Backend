using Microsoft.Extensions.DependencyInjection;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Profile;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Cards;
using Flenta.Application.Features.Transactions;
using Flenta.Application.Features.Finance;
using Flenta.Application.Features.Kyc;
using Flenta.Application.Abstractions.Kyc;

namespace Flenta.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthenticationService>();
        services.AddScoped<Features.Payments.PaymentService>();
        services.AddScoped<StatementService>();
        services.AddScoped<Features.Admin.AdminWorkflowService>();
        services.AddScoped<Features.Settings.SettingsService>();
        services.AddScoped<Features.Support.SupportService>();
        services.AddScoped<Features.Recipients.RecipientService>();
        services.AddScoped<Features.Notifications.NotificationService>();
        services.AddScoped<Flenta.Application.Features.Admin.AdminService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<VerifiedIdentityService>();
        services.AddScoped<AccountService>();
        services.AddScoped<CardService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<SavingsService>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<ExchangeService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<KycService>();
        services.AddScoped<IKycAccessPolicy, KycAccessPolicy>();
        return services;
    }
}
