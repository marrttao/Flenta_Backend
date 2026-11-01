using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Flenta.Application.Abstractions;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Cards;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;
using Flenta.Infrastructure.Persistence.Banking;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Flenta.Tests.Banking;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKING_TEST_CONNECTION")))
            Skip = "Requires a disposable PostgreSQL server configured in BANKING_TEST_CONNECTION.";
    }
}

[CollectionDefinition("BankingPostgres", DisableParallelization = true)]
public sealed class BankingPostgresCollection;

[Collection("BankingPostgres")]
public sealed partial class PostgresApiTests : IAsyncLifetime
{
    private string connection = "";
    private string database = "";
    private readonly string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static string RootConnection => Environment.GetEnvironmentVariable("BANKING_TEST_CONNECTION")
        ?? throw new InvalidOperationException("BANKING_TEST_CONNECTION must refer to a disposable PostgreSQL server.");

    public async Task InitializeAsync()
    {
        database = "flenta_test_" + Guid.NewGuid().ToString("N");
        await using var root = new NpgsqlConnection(RootConnection);
        await root.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", root);
        await create.ExecuteNonQueryAsync();
        connection = new NpgsqlConnectionStringBuilder(RootConnection) { Database = database }.ConnectionString;
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (string.IsNullOrEmpty(database)) return;
        await using var root = new NpgsqlConnection(RootConnection);
        await root.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", root);
        await drop.ExecuteNonQueryAsync();
    }

    private BankingDbContext Context() => new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(connection).Options);

    private WebApplicationFactory<Program> Host() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.Configure<BankingOptions>(options =>
        {
            options.Enabled = true;
            options.ConnectionString = connection;
            options.CardEncryptionKey = key;
        });
        services.RemoveAll<IUserProfileRepository>();
        services.RemoveAll<IIdentityProvider>();
        services.AddSingleton<IUserProfileRepository, TestProfiles>();
        services.AddSingleton<IIdentityProvider, TestIdentity>();
    }));

    private static HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user);
        return client;
    }

    private static async Task<AccountResponse> Open(HttpClient client)
    {
        using var response = await client.PostAsync("/api/accounts", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, Guid? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return await client.SendAsync(request);
    }

    private static async Task<TransactionResponse> Operation(HttpClient client, string path, object body, Guid? key = null)
    {
        using var response = await Send(client, path, body, key);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>())!;
    }

    private static async Task<decimal> Balance(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{id}"))!.Balance;

    [PostgresFact]
    public async Task Iban_transfer_persists_fee_balanced_postings_and_private_history()
    {
        using var host = Host();
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        using var mallory = Client(host, "mallory");
        var source = await Open(alice);
        var destination = await Open(bob);
        await Operation(alice, $"/api/accounts/{source.Id}/top-ups", new { amount = 1000m });
        using var quoteResponse = await alice.PostAsJsonAsync("/api/transactions/quote", new { sourceAccountId = source.Id, amount = 500m, byIban = true });
        quoteResponse.EnsureSuccessStatusCode();
        var quote = await quoteResponse.Content.ReadFromJsonAsync<TransferQuote>();
        Assert.Equal(2.50m, quote!.Fee);
        Assert.Equal(502.50m, quote.Total);
        Assert.True(quote.SufficientFunds);
        var transfer = await Operation(alice, "/api/transactions/iban", new { sourceAccountId = source.Id, iban = destination.Iban, amount = 500m });
        Assert.Equal("Completed", transfer.Status);
        Assert.Equal(2.50m, transfer.Fee);
        Assert.Equal(497.50m, await Balance(alice, source.Id));
        Assert.Equal(500m, await Balance(bob, destination.Id));
        var incoming = await bob.GetFromJsonAsync<TransactionResponse[]>("/api/transactions");
        Assert.Equal(transfer.Id, Assert.Single(incoming!).Id);
        Assert.Empty((await mallory.GetFromJsonAsync<TransactionResponse[]>("/api/transactions"))!);
        using var privateLookup = await mallory.GetAsync($"/api/transactions/{transfer.Id}");
        Assert.Equal(HttpStatusCode.NotFound, privateLookup.StatusCode);
        await using var db = Context();
        Assert.Equal(250, (await db.Accounts.SingleAsync(a => a.Id == TransactionService.FeeAccountId)).BalanceMinorUnits);
        Assert.Equal(0, await db.Accounts.SumAsync(a => a.BalanceMinorUnits));
        Assert.All(await db.Entries.GroupBy(e => e.TransactionId).Select(g => g.Sum(e => e.AmountMinorUnits)).ToListAsync(), sum => Assert.Equal(0, sum));
    }

    [PostgresFact]
    public async Task Concurrent_duplicate_topups_post_once_and_reusing_the_key_for_other_data_conflicts()
    {
        using var host = Host();
        using var alice = Client(host, "alice");
        var account = await Open(alice);
        var requestKey = Guid.NewGuid();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m }, requestKey)));
        Assert.Single(attempts.Select(a => a.Id).Distinct());
        Assert.Equal(100m, await Balance(alice, account.Id));
        using var conflict = await Send(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 101m }, requestKey);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var db = Context();
        Assert.Equal(1, await db.Transactions.CountAsync());
        Assert.Equal(2, await db.Entries.CountAsync());
    }

    [PostgresFact]
    public async Task Concurrent_debits_cannot_overdraw_an_account()
    {
        using var host = Host();
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var source = await Open(alice);
        var destination = await Open(bob);
        await Operation(alice, $"/api/accounts/{source.Id}/top-ups", new { amount = 100m });
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Operation(alice, "/api/transactions/internal",
            new { sourceAccountId = source.Id, destinationAccountId = destination.Id, amount = 75m })));
        Assert.Single(attempts, a => a.Status == "Completed");
        var rejected = Assert.Single(attempts, a => a.Status == "Rejected");
        Assert.Equal("account.insufficient_funds", rejected.FailureCode);
        Assert.Equal(25m, await Balance(alice, source.Id));
        Assert.Equal(75m, await Balance(bob, destination.Id));
        await using var db = Context();
        Assert.Equal(4, await db.Entries.CountAsync());
        Assert.Equal(0, await db.Accounts.SumAsync(a => a.BalanceMinorUnits));
    }

    [PostgresFact]
    public async Task Database_write_failure_rolls_back_all_balances_and_the_same_key_can_be_retried()
    {
        using var host = Host();
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var source = await Open(alice);
        var destination = await Open(bob);
        await Operation(alice, $"/api/accounts/{source.Id}/top-ups", new { amount = 100m });
        await using var db = Context();
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION banking.fail_test_entry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'simulated posting failure'; END $$; CREATE TRIGGER fail_test_entry BEFORE INSERT ON banking.entries FOR EACH ROW EXECUTE FUNCTION banking.fail_test_entry();");
        var requestKey = Guid.NewGuid();
        var payload = new { sourceAccountId = source.Id, destinationAccountId = destination.Id, amount = 10m };
        using var failed = await Send(alice, "/api/transactions/internal", payload, requestKey);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.DoesNotContain("simulated posting failure", await failed.Content.ReadAsStringAsync());
        Assert.Equal(100m, await Balance(alice, source.Id));
        Assert.Equal(0m, await Balance(bob, destination.Id));
        Assert.Equal(1, await db.Transactions.CountAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_test_entry ON banking.entries; DROP FUNCTION banking.fail_test_entry();");
        var retry = await Operation(alice, "/api/transactions/internal", payload, requestKey);
        Assert.Equal("Completed", retry.Status);
        Assert.Equal(90m, await Balance(alice, source.Id));
        Assert.Equal(10m, await Balance(bob, destination.Id));
    }

    [PostgresFact]
    public async Task Rejected_and_invalid_requests_do_not_move_funds_or_disclose_other_accounts()
    {
        using var host = Host();
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var source = await Open(alice);
        var destination = await Open(bob);
        await Operation(alice, $"/api/accounts/{source.Id}/top-ups", new { amount = 10m });
        using var foreign = await Send(bob, $"/api/accounts/{source.Id}/top-ups", new { amount = 100m });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        foreach (var recipient in new[] { source.Id, Guid.NewGuid() })
        {
            var rejected = await Operation(alice, "/api/transactions/internal", new { sourceAccountId = source.Id, destinationAccountId = recipient, amount = 1m });
            Assert.Equal("Rejected", rejected.Status);
        }
        var feeFailure = await Operation(alice, "/api/transactions/iban", new { sourceAccountId = source.Id, iban = destination.Iban, amount = 10m });
        Assert.Equal("account.insufficient_funds", feeFailure.FailureCode);
        var unknownIban = await Operation(alice, "/api/transactions/iban", new { sourceAccountId = source.Id, iban = Iban.CreateSandbox(Guid.NewGuid()).Value, amount = 1m });
        Assert.Equal("transfer.recipient_not_found", unknownIban.FailureCode);
        using var precise = await Send(alice, $"/api/accounts/{source.Id}/top-ups", new { amount = .001m });
        Assert.Equal(HttpStatusCode.BadRequest, precise.StatusCode);
        using var missingKey = await alice.PostAsJsonAsync($"/api/accounts/{source.Id}/top-ups", new { amount = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Equal(10m, await Balance(alice, source.Id));
        Assert.Equal(0m, await Balance(bob, destination.Id));
        Assert.Empty((await bob.GetFromJsonAsync<TransactionResponse[]>("/api/transactions"))!);
    }

    [PostgresFact]
    public async Task Requisites_are_encrypted_masked_owner_only_and_require_password_confirmation()
    {
        using var host = Host();
        using var alice = Client(host, "alice");
        using var bob = Client(host, "bob");
        var account = await Open(alice);
        using var issued = await alice.PostAsJsonAsync("/api/cards", new { accountId = account.Id });
        issued.EnsureSuccessStatusCode();
        var card = (await issued.Content.ReadFromJsonAsync<CardResponse>())!;
        Assert.StartsWith("**** **** **** ", card.MaskedNumber);
        var publicJson = await issued.Content.ReadAsStringAsync();
        Assert.DoesNotContain("protectedData", publicJson);
        Assert.DoesNotContain("cvv", publicJson);
        using var foreign = await bob.PostAsJsonAsync($"/api/cards/{card.Id}/requisites", new { password = "confirm-card-password" });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var denied = await alice.PostAsJsonAsync($"/api/cards/{card.Id}/requisites", new { password = "incorrect-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var revealed = await alice.PostAsJsonAsync($"/api/cards/{card.Id}/requisites", new { password = "confirm-card-password" });
        revealed.EnsureSuccessStatusCode();
        Assert.Equal("no-store", revealed.Headers.CacheControl?.ToString());
        var secrets = (await revealed.Content.ReadFromJsonAsync<CardSecretsResponse>())!;
        Assert.StartsWith("999999", secrets.Number);
        Assert.Equal(16, secrets.Number.Length);
        Assert.Matches("^[0-9]{3}$", secrets.Cvv);
        await using var db = Context();
        var saved = await db.Cards.AsNoTracking().SingleAsync();
        Assert.DoesNotContain(secrets.Number, saved.ProtectedData);
        Assert.NotNull(saved.LastSecretsViewedAt);
        using var blocked = await alice.PostAsync($"/api/cards/{card.Id}/block", null);
        blocked.EnsureSuccessStatusCode();
        using var blockedReveal = await alice.PostAsJsonAsync($"/api/cards/{card.Id}/requisites", new { password = "confirm-card-password" });
        Assert.Equal(HttpStatusCode.BadRequest, blockedReveal.StatusCode);
        using var unblocked = await alice.PostAsync($"/api/cards/{card.Id}/unblock", null);
        unblocked.EnsureSuccessStatusCode();
        using var closed = await alice.PostAsync($"/api/cards/{card.Id}/close", null);
        closed.EnsureSuccessStatusCode();
        using var reopen = await alice.PostAsync($"/api/cards/{card.Id}/unblock", null);
        Assert.Equal(HttpStatusCode.BadRequest, reopen.StatusCode);
    }

    [PostgresFact]
    public async Task Database_rejects_ledger_rewrites_and_balances_without_postings()
    {
        using var host = Host();
        using var alice = Client(host, "alice");
        var account = await Open(alice);
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 20m });
        await using var db = Context();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE banking.entries SET \"AmountMinorUnits\" = \"AmountMinorUnits\" + 1"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE banking.accounts SET \"BalanceMinorUnits\" = \"BalanceMinorUnits\" + 1 WHERE \"Id\" = {account.Id}"));
        Assert.Equal(20m, await Balance(alice, account.Id));
        Assert.Equal(0, await db.Entries.SumAsync(e => e.AmountMinorUnits));
    }
}

internal sealed class TestProfiles : IUserProfileRepository
{
    internal static readonly Guid Alice = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid Bob = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public Task<UserProfile> GetCurrentAsync(string accessToken, CancellationToken cancellationToken)
    {
        var id = accessToken switch { "alice" => Alice, "bob" => Bob, "mallory" => Guid.Parse("10000000-0000-0000-0000-000000000003"), _ => throw new ApplicationFailureException(FailureKind.Unauthorized, "auth.invalid", "Invalid session.") };
        return Task.FromResult(new UserProfile(id, EmailAddress.Create(accessToken + "@example.com"), accessToken, true));
    }
    public Task<UserProfile> UpdateCurrentAsync(string accessToken, string displayName, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class TestIdentity : IIdentityProvider
{
    public Task<AuthSession> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (password != "confirm-card-password") throw new ApplicationFailureException(FailureKind.Unauthorized, "auth.invalid_credentials", "Invalid credentials.");
        var id = email.StartsWith("alice", StringComparison.Ordinal) ? TestProfiles.Alice : TestProfiles.Bob;
        return Task.FromResult(new AuthSession("temporary-stepup", "temporary-refresh", 3600, new ProfileResponse(id, email, "Test", true)));
    }
    public Task LogoutAsync(string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<RegistrationResult> RegisterAsync(string email, string password, string displayName, string codeChallenge, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AuthSession> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SendRecoveryEmailAsync(string email, string codeChallenge, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ChangePasswordAsync(string accessToken, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
}
