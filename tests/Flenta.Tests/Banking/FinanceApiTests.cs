using System.Net;
using System.Net.Http.Json;
using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Cards;
using Flenta.Application.Features.Finance;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flenta.Tests.Banking;

public sealed partial class PostgresApiTests
{
    private WebApplicationFactory<Program> FinanceHost(TestRates? rates = null, TestClock? clock = null) =>
        Host().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IExchangeRateProvider>();
            services.AddSingleton<IExchangeRateProvider>(rates ?? new TestRates());
            if (clock is not null) { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); }
        }));

    private static async Task<AccountResponse> OpenCurrency(HttpClient client, string currency)
    {
        using var response = await client.PostAsync("/api/accounts?currency=" + currency, null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private static async Task<CardResponse> Issue(HttpClient client, Guid accountId)
    {
        using var response = await client.PostAsJsonAsync("/api/cards", new { accountId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardResponse>())!;
    }

    private static async Task<GoalResponse> Goal(HttpClient client, Guid accountId, decimal target = 500)
    {
        using var response = await client.PostAsJsonAsync("/api/goals", new { fundingAccountId = accountId, name = "Laptop", target });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GoalResponse>())!;
    }

    private static async Task SetRule(HttpClient client, GoalResponse goal, string mode, decimal percentage = 5, int step = 10)
    {
        using var response = await client.PutAsJsonAsync($"/api/goals/{goal.Id}", new GoalSettings(goal.Name, goal.Target, null, mode, percentage, step));
        response.EnsureSuccessStatusCode();
    }

    [PostgresFact]
    public async Task Percentage_purchase_cashback_and_savings_post_once_under_concurrent_retries()
    {
        using var host = FinanceHost(); using var alice = Client(host, "alice"); using var bob = Client(host, "bob");
        var account = await Open(alice); var card = await Issue(alice, account.Id); var goal = await Goal(alice, account.Id);
        await SetRule(alice, goal, "Percentage");
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 1000m });
        var key = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 200m }, key)));
        Assert.Single(results.Select(t => t.Id).Distinct());
        Assert.All(results, t => { Assert.Equal("Completed", t.Status); Assert.Equal(2m, t.Cashback); Assert.Equal(10m, t.Saved); Assert.Equal(goal.Id, t.GoalId); });
        Assert.Equal(792m, await Balance(alice, account.Id));
        Assert.Equal(10m, Assert.Single((await alice.GetFromJsonAsync<GoalResponse[]>("/api/goals"))!).Balance);
        Assert.Single((await alice.GetFromJsonAsync<CashbackResponse>("/api/finance/cashback"))!.Transactions);
        Assert.Empty((await bob.GetFromJsonAsync<CashbackResponse>("/api/finance/cashback"))!.Transactions);
        using var reused = await Send(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 201m }, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        using var foreign = await Send(bob, "/api/finance/purchases", new { cardId = card.Id, amount = 1m });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [PostgresFact]
    public async Task Roundup_uses_exact_remainder_caps_at_target_and_does_not_round_manual_deposits()
    {
        using var host = FinanceHost(); using var alice = Client(host, "alice");
        var account = await Open(alice); var card = await Issue(alice, account.Id); var goal = await Goal(alice, account.Id, 5);
        await SetRule(alice, goal, "RoundUp");
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 1000m });
        var first = await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 156.40m });
        Assert.Equal(3.60m, first.Saved); Assert.Equal(1.56m, first.Cashback);
        var round = await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 160m });
        Assert.Equal(0, round.Saved);
        var capped = await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 156.40m });
        Assert.Equal(1.40m, capped.Saved);
        var completed = await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 1m });
        Assert.Equal(0, completed.Saved);
        var manual = await Operation(alice, $"/api/goals/{goal.Id}/deposits", new { amount = 123.45m });
        Assert.Equal(123.45m, manual.Amount); Assert.Equal(0, manual.Cashback);
        Assert.Equal(128.45m, Assert.Single((await alice.GetFromJsonAsync<GoalResponse[]>("/api/goals"))!).Balance);
        await Operation(alice, $"/api/goals/{goal.Id}/withdrawals", new { amount = 128.45m });
        using var archived = await alice.PostAsync($"/api/goals/{goal.Id}/archive", null);
        archived.EnsureSuccessStatusCode();
        Assert.True((await archived.Content.ReadFromJsonAsync<GoalResponse>())!.Archived);
    }

    [PostgresFact]
    public async Task Insufficient_optional_savings_does_not_cancel_purchase_and_rejected_purchases_earn_nothing()
    {
        using var host = FinanceHost(); using var alice = Client(host, "alice");
        var account = await Open(alice); var card = await Issue(alice, account.Id); var goal = await Goal(alice, account.Id);
        await SetRule(alice, goal, "Percentage", 50);
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        var paid = await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 98m });
        Assert.Equal("Completed", paid.Status); Assert.Equal("insufficient_funds", paid.SavingsSkipReason);
        Assert.Equal(0, paid.Saved); Assert.Equal(.98m, paid.Cashback); Assert.Equal(2.98m, await Balance(alice, account.Id));
        var insufficient = await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 3m });
        Assert.Equal("Rejected", insufficient.Status); Assert.Equal(0, insufficient.Cashback); Assert.Equal(0, insufficient.Saved);
        using var block = await alice.PostAsync($"/api/cards/{card.Id}/block", null); block.EnsureSuccessStatusCode();
        var blocked = await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 1m });
        Assert.Equal("Rejected", blocked.Status); Assert.Equal("card.unavailable", blocked.FailureCode);
        Assert.Equal(2.98m, await Balance(alice, account.Id));
    }

    [PostgresFact]
    public async Task Concurrent_purchases_and_savings_cannot_overdraw_source_or_overfill_goal()
    {
        using var host = FinanceHost(); using var alice = Client(host, "alice");
        var account = await Open(alice); var card = await Issue(alice, account.Id); var goal = await Goal(alice, account.Id);
        await SetRule(alice, goal, "Percentage", 10);
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 75m })));
        Assert.Single(results, t => t.Status == "Completed"); Assert.Single(results, t => t.Status == "Rejected");
        Assert.Equal(18.25m, await Balance(alice, account.Id));
        Assert.Equal(7.50m, Assert.Single((await alice.GetFromJsonAsync<GoalResponse[]>("/api/goals"))!).Balance);
    }

    [PostgresFact]
    public async Task Purchase_write_failure_rolls_back_cashback_savings_and_balance_together()
    {
        using var host = FinanceHost(); using var alice = Client(host, "alice");
        var account = await Open(alice); var card = await Issue(alice, account.Id); var goal = await Goal(alice, account.Id);
        await SetRule(alice, goal, "Percentage");
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        await using var db = Context();
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION banking.fail_finance() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected failure'; END $$; CREATE TRIGGER fail_finance BEFORE INSERT ON banking.entries FOR EACH ROW EXECUTE FUNCTION banking.fail_finance();");
        var key = Guid.NewGuid(); var payload = new { cardId = card.Id, amount = 10m };
        using var failed = await Send(alice, "/api/finance/purchases", payload, key);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(100m, await Balance(alice, account.Id));
        Assert.Equal(0, Assert.Single((await alice.GetFromJsonAsync<GoalResponse[]>("/api/goals"))!).Balance);
        Assert.Empty((await alice.GetFromJsonAsync<CashbackResponse>("/api/finance/cashback"))!.Transactions);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_finance ON banking.entries; DROP FUNCTION banking.fail_finance();");
        var retry = await Operation(alice, "/api/finance/purchases", payload, key);
        Assert.Equal("Completed", retry.Status); Assert.Equal(.10m, retry.Cashback); Assert.Equal(.50m, retry.Saved);
        Assert.Equal(89.60m, await Balance(alice, account.Id));
    }

    [PostgresFact]
    public async Task Goal_ownership_validation_and_single_automatic_goal_are_enforced()
    {
        using var host = FinanceHost(); using var alice = Client(host, "alice"); using var bob = Client(host, "bob");
        var account = await Open(alice); var goal = await Goal(alice, account.Id); var other = await Goal(alice, account.Id);
        await SetRule(alice, goal, "Percentage", 12.34m);
        using var duplicate = await alice.PutAsJsonAsync($"/api/goals/{other.Id}", new GoalSettings("Other", 100, null, "RoundUp", 0, 10));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        foreach (var percentage in new[] { -1m, 0m, 100.01m, 1.001m })
        {
            using var invalid = await alice.PutAsJsonAsync($"/api/goals/{goal.Id}", new GoalSettings("Goal", 100, null, "Percentage", percentage, 10));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var foreign = await Send(bob, $"/api/goals/{goal.Id}/withdrawals", new { amount = 1m });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<GoalResponse[]>("/api/goals"))!);
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 5m });
        var key = Guid.NewGuid();
        var deposit = await Operation(alice, $"/api/goals/{goal.Id}/deposits", new { amount = 1.23m }, key);
        var again = await Operation(alice, $"/api/goals/{goal.Id}/deposits", new { amount = 1.23m }, key);
        Assert.Equal(deposit.Id, again.Id); Assert.Equal(3.77m, await Balance(alice, account.Id));
        using var nonempty = await alice.PostAsync($"/api/goals/{goal.Id}/archive", null);
        Assert.Equal(HttpStatusCode.BadRequest, nonempty.StatusCode);
    }

    [PostgresFact]
    public async Task Exchange_uses_confirmed_rate_and_balances_every_currency_separately()
    {
        var rates = new TestRates(); using var host = FinanceHost(rates); using var alice = Client(host, "alice");
        var source = await Open(alice); var destination = await OpenCurrency(alice, "USD");
        await Operation(alice, $"/api/accounts/{source.Id}/top-ups", new { amount = 1000m });
        using var quoted = await alice.PostAsJsonAsync("/api/finance/exchange-quotes", new { sourceAccountId = source.Id, destinationAccountId = destination.Id, amount = 400m });
        quoted.EnsureSuccessStatusCode(); var quote = (await quoted.Content.ReadFromJsonAsync<ExchangeQuoteResponse>())!;
        Assert.Equal(10m, quote.ReceivedAmount); Assert.Equal(.025m, quote.Rate); Assert.Equal(0, quote.Fee);
        rates.Usd = 80;
        var key = Guid.NewGuid();
        var exchanged = await Operation(alice, "/api/finance/exchanges", new { quoteId = quote.Id }, key);
        Assert.Equal("Completed", exchanged.Status); Assert.Equal(10m, exchanged.ReceivedAmount); Assert.Equal("USD", exchanged.DestinationCurrency);
        var retry = await Operation(alice, "/api/finance/exchanges", new { quoteId = quote.Id }, key);
        Assert.Equal(exchanged.Id, retry.Id);
        using var consumed = await Send(alice, "/api/finance/exchanges", new { quoteId = quote.Id });
        Assert.Equal(HttpStatusCode.BadRequest, consumed.StatusCode);
        Assert.Equal(600m, await Balance(alice, source.Id)); Assert.Equal(10m, await Balance(alice, destination.Id));
        await using var db = Context();
        Assert.All(await db.Entries.GroupBy(e => new { e.TransactionId, e.CurrencyCode }).Select(g => g.Sum(e => e.AmountMinorUnits)).ToListAsync(), value => Assert.Equal(0, value));
    }

    [PostgresFact]
    public async Task Exchange_quotes_expire_and_cannot_be_used_by_another_owner_or_for_direct_cross_currency_transfer()
    {
        var clock = new TestClock(); using var host = FinanceHost(clock: clock); using var alice = Client(host, "alice"); using var bob = Client(host, "bob");
        var source = await Open(alice); var destination = await OpenCurrency(alice, "EUR");
        await Operation(alice, $"/api/accounts/{source.Id}/top-ups", new { amount = 100m });
        using var quoted = await alice.PostAsJsonAsync("/api/finance/exchange-quotes", new { sourceAccountId = source.Id, destinationAccountId = destination.Id, amount = 45m });
        quoted.EnsureSuccessStatusCode(); var quote = (await quoted.Content.ReadFromJsonAsync<ExchangeQuoteResponse>())!;
        using var foreign = await Send(bob, "/api/finance/exchanges", new { quoteId = quote.Id });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var transfer = await Operation(alice, "/api/transactions/internal", new { sourceAccountId = source.Id, destinationAccountId = destination.Id, amount = 1m });
        Assert.Equal("Rejected", transfer.Status); Assert.Equal("transfer.currency_mismatch", transfer.FailureCode);
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        using var expired = await Send(alice, "/api/finance/exchanges", new { quoteId = quote.Id });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(100m, await Balance(alice, source.Id)); Assert.Equal(0, await Balance(alice, destination.Id));
    }

    [PostgresFact]
    public async Task Analytics_excludes_own_transfers_and_savings_from_income_and_expenses()
    {
        using var host = FinanceHost(); using var alice = Client(host, "alice"); using var bob = Client(host, "bob");
        var account = await Open(alice); var own = await Open(alice); var foreign = await Open(bob);
        var goal = await Goal(alice, account.Id); var card = await Issue(alice, account.Id);
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 1000m });
        await Operation(alice, "/api/transactions/internal", new { sourceAccountId = account.Id, destinationAccountId = own.Id, amount = 100m });
        await Operation(alice, "/api/transactions/iban", new { sourceAccountId = account.Id, iban = foreign.Iban, amount = 100m });
        await Operation(alice, $"/api/goals/{goal.Id}/deposits", new { amount = 50m });
        await Operation(alice, $"/api/goals/{goal.Id}/withdrawals", new { amount = 10m });
        await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 200m });
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var summary = (await alice.GetFromJsonAsync<AnalyticsResponse>($"/api/finance/analytics?from={today}&through={today}"))!;
        var uah = Assert.Single(summary.Currencies, c => c.Currency == "UAH");
        Assert.Equal(1000m, uah.Income); Assert.Equal(300m, uah.Expenses); Assert.Equal(1m, uah.Fees);
        Assert.Equal(2m, uah.Cashback); Assert.Equal(50m, uah.SavingsDeposited); Assert.Equal(10m, uah.SavingsWithdrawn);
        Assert.Equal(40m, uah.SavingsBalance); Assert.Equal(661m, uah.AvailableBalance);
        var received = (await bob.GetFromJsonAsync<AnalyticsResponse>($"/api/finance/analytics?from={today}&through={today}"))!.Currencies.Single(c => c.Currency == "UAH");
        Assert.Equal(100m, received.Income); Assert.Equal(0, received.Expenses); Assert.Equal(0, received.Cashback);
        using var goalsResponse = await alice.GetAsync("/api/goals");
        Assert.Equal("no-store", goalsResponse.Headers.CacheControl!.ToString());
    }

    [PostgresFact]
    public async Task Finance_migration_preserves_existing_accounts_balances_and_immutable_history()
    {
        await using var db = Context(); var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261025110524_InitialBanking");
        var id = Guid.NewGuid(); var operation = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid(); var owner = TestProfiles.Alice;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO banking.accounts (\"Id\",\"OwnerId\",\"Iban\",\"Kind\",\"BalanceMinorUnits\",\"CreatedAt\") VALUES ({id},{owner},'UA019999990000000000000000001','Customer',10000,now())");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO banking.transactions (\"Id\",\"InitiatorId\",\"IdempotencyKey\",\"RequestFingerprint\",\"Kind\",\"Status\",\"SourceAccountId\",\"DestinationAccountId\",\"AmountMinorUnits\",\"FeeMinorUnits\",\"CreatedAt\",\"FinishedAt\") VALUES ({operation},{owner},{Guid.NewGuid()},'original','TopUp','Completed','00000000-0000-0000-0000-000000000001',{id},10000,0,now(),now())");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO banking.entries (\"Id\",\"TransactionId\",\"AccountId\",\"AmountMinorUnits\",\"CreatedAt\") VALUES ({first},{operation},{id},10000,now()),({second},{operation},'00000000-0000-0000-0000-000000000001',-10000,now())");
            await db.Database.ExecuteSqlRawAsync("UPDATE banking.accounts SET \"BalanceMinorUnits\" = -10000 WHERE \"Id\" = '00000000-0000-0000-0000-000000000001'");
            await tx.CommitAsync();
        }
        await migrator.MigrateAsync();
        Assert.Equal(10000, (await db.Accounts.SingleAsync(a => a.Id == id)).BalanceMinorUnits);
        Assert.Equal(-10000, (await db.Accounts.SingleAsync(a => a.Id == TransactionService.ClearingAccountId)).BalanceMinorUnits);
        Assert.Equal("UAH", (await db.Transactions.SingleAsync(t => t.Id == operation)).CurrencyCode);
        Assert.Equal(2, await db.Entries.CountAsync());
    }

    [PostgresFact]
    public async Task Database_rejects_postings_that_only_balance_after_mixing_currencies()
    {
        await using var db = Context();
        var op = new BankTransaction(TestProfiles.Alice, Guid.NewGuid(), "corrupt-test", TransactionKind.Exchange,
            SystemAccounts.Clearing("UAH"), SystemAccounts.Merchant("USD"), null, Money.FromDecimal(1), Money.FromDecimal(0), DateTimeOffset.UtcNow);
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Transactions.Add(op);
        db.Entries.AddRange(
            new LedgerEntry(op.Id, SystemAccounts.Clearing("UAH"), -100, DateTimeOffset.UtcNow, "UAH"),
            new LedgerEntry(op.Id, SystemAccounts.Merchant("UAH"), 50, DateTimeOffset.UtcNow, "UAH"),
            new LedgerEntry(op.Id, SystemAccounts.Clearing("USD"), -50, DateTimeOffset.UtcNow, "USD"),
            new LedgerEntry(op.Id, SystemAccounts.Merchant("USD"), 100, DateTimeOffset.UtcNow, "USD"));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE banking.transactions SET \"Status\"='Completed' WHERE \"Id\"={op.Id}");
        await db.Database.ExecuteSqlRawAsync("UPDATE banking.accounts SET \"BalanceMinorUnits\" = CASE \"Id\" WHEN '00000000-0000-0000-0000-000000000001' THEN -100 WHEN '00000000-0000-0000-0000-000000000003' THEN 50 WHEN '00000000-0000-0000-0000-000000000011' THEN -50 WHEN '00000000-0000-0000-0000-000000000013' THEN 100 ELSE 0 END");
        var failure = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => tx.CommitAsync());
        Assert.Contains("Unbalanced transaction currency", failure.MessageText);
    }
}

internal sealed class TestRates : IExchangeRateProvider
{
    public decimal Usd { get; set; } = 40m;
    public Task<CurrencyRates> GetAsync(CancellationToken ct) => Task.FromResult(new CurrencyRates(DateOnly.FromDateTime(DateTime.UtcNow), "NBU",
        new Dictionary<string, decimal> { ["UAH"] = 1m, ["USD"] = Usd, ["EUR"] = 45m }));
}

internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
