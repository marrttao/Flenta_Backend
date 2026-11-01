using Microsoft.Extensions.DependencyInjection;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Profile;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Cards;
using Flenta.Application.Features.Transactions;
using Flenta.Application.Features.Finance;

namespace Flenta.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthenticationService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<AccountService>();
        services.AddScoped<CardService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<SavingsService>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<ExchangeService>();
        services.AddScoped<AnalyticsService>();
        return services;
    }
}
