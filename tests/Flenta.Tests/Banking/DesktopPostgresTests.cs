using System.Net;
using System.Net.Http.Json;
using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Admin;
using Flenta.Application.Features.Finance;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Cards;
using Flenta.Application.Features.Kyc;
using Flenta.Application.Features.Notifications;
using Flenta.Application.Features.Recipients;
using Flenta.Application.Features.Settings;
using Flenta.Application.Features.Support;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Configuration;
using Flenta.Tests.Kyc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Flenta.Tests.Banking;

public sealed partial class PostgresApiTests
{
    private WebApplicationFactory<Program> WorkflowHost() => Host().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.AddSingleton<IKycProvider, AdminEvidenceProvider>();
        services.RemoveAll<IUserProfileRepository>(); services.AddSingleton<IUserProfileRepository, AdminReadProfiles>();
        services.RemoveAll<IIdentityProvider>(); services.AddSingleton<IIdentityProvider, AdminCommandIdentity>();
        services.Configure<SupabaseOptions>(o => { o.Enabled = true; o.Url = "http://127.0.0.1:9999"; o.PublishableKey = "sb_publishable_test_fixture"; });
        services.Configure<AdminOptions>(o => o.SupabaseSecretKey = "sb_secret_test_fixture");
        services.AddHttpClient("SupabaseAdminDirectory").ConfigurePrimaryHttpMessageHandler(() => new AdminReadDirectory());
    }));

    [PostgresFact]
    public async Task Preferences_recipients_and_notification_receipts_are_owner_scoped()
    {
        using var host = Host(); using var alice = Client(host, "alice"); using var bob = Client(host, "bob");
        var a = await Open(alice); var b = await Open(bob);
        using var settings = await alice.PutAsJsonAsync("/api/settings", new { phone = "+380991234567", defaultCurrency = "EUR", transactionNotifications = false });
        settings.EnsureSuccessStatusCode();
        var actual = await alice.GetFromJsonAsync<SettingsResponse>("/api/settings");
        Assert.Equal("EUR", actual!.DefaultCurrency);
        Assert.Equal("", (await bob.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Phone);
        using var bad = await alice.PutAsJsonAsync("/api/settings", new { phone = "abc", defaultCurrency = "UAH", transactionNotifications = true });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var first = await alice.PostAsJsonAsync("/api/recipients", new { name = "Bob", iban = b.Iban });
        first.EnsureSuccessStatusCode(); var recipient = await first.Content.ReadFromJsonAsync<RecipientResponse>();
        using var duplicate = await alice.PostAsJsonAsync("/api/recipients", new { name = "Bob again", iban = b.Iban });
        Assert.Equal(recipient!.Id, (await duplicate.Content.ReadFromJsonAsync<RecipientResponse>())!.Id);
        using var foreign = await bob.DeleteAsync("/api/recipients/" + recipient.Id);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<RecipientResponse[]>("/api/recipients"))!);
        await Operation(alice, $"/api/accounts/{a.Id}/top-ups", new { amount = 100m });
        await Operation(alice, "/api/transactions/iban", new { sourceAccountId = a.Id, iban = b.Iban, amount = 10m, memo = "Test payment" });
        Assert.Empty((await alice.GetFromJsonAsync<NotificationResponse[]>("/api/notifications"))!);
        var note = Assert.Single((await bob.GetFromJsonAsync<NotificationResponse[]>("/api/notifications"))!);
        using var denied = await alice.PostAsync($"/api/notifications/{note.Id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        (await bob.PostAsync($"/api/notifications/{note.Id}/read", null)).EnsureSuccessStatusCode();
        (await bob.PostAsync($"/api/notifications/{note.Id}/read", null)).EnsureSuccessStatusCode();
        Assert.True(Assert.Single((await bob.GetFromJsonAsync<NotificationResponse[]>("/api/notifications"))!).IsRead);
        Assert.Equal("no-store", settings.Headers.CacheControl?.ToString());
    }

    [PostgresFact]
    public async Task Support_conversations_keep_private_messages_immutable_and_reject_closed_replies()
    {
        using var host = Host(); using var alice = Client(host, "alice"); using var bob = Client(host, "bob");
        using var created = await bob.PostAsJsonAsync("/api/support/requests", new { subject = "Transfer question", kind = "Message", body = "Please help", isStaff = true });
        created.EnsureSuccessStatusCode(); var summary = (await created.Content.ReadFromJsonAsync<SupportSummary>())!;
        using var denied = await alice.GetAsync("/api/support/requests/" + summary.Id); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var own = (await bob.GetFromJsonAsync<SupportConversation>("/api/support/requests/" + summary.Id))!;
        Assert.False(Assert.Single(own.Messages).IsStaff);
        using var adminHost = WorkflowHost(); using var admin = Client(adminHost, "alice");
        (await admin.PostAsJsonAsync($"/api/admin/support/{summary.Id}/messages", new { body = "We are checking." })).EnsureSuccessStatusCode();
        own = (await bob.GetFromJsonAsync<SupportConversation>("/api/support/requests/" + summary.Id))!;
        Assert.True(own.Messages.Last().IsStaff);
        (await bob.PostAsync($"/api/support/requests/{summary.Id}/close", null)).EnsureSuccessStatusCode();
        using var closed = await bob.PostAsJsonAsync($"/api/support/requests/{summary.Id}/messages", new { body = "Another reply" });
        Assert.Equal(HttpStatusCode.BadRequest, closed.StatusCode);
        await using var db = Context();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE banking.support_messages SET \"Body\"='rewritten'"));
    }

    [PostgresFact]
    public async Task Concurrent_card_payments_enforce_monthly_limit_and_channel_controls_under_lock()
    {
        using var host = Host(); using var alice = Client(host, "alice");
        var account = await Open(alice); await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        using var response = await alice.PostAsJsonAsync("/api/cards", new { accountId = account.Id }); var card = (await response.Content.ReadFromJsonAsync<CardResponse>())!;
        (await alice.PutAsJsonAsync($"/api/cards/{card.Id}/settings", new { onlinePayments = true, contactlessPayments = false, cashWithdrawals = false, monthlyLimit = 30m })).EnsureSuccessStatusCode();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 20m })));
        Assert.Single(results, r => r.Status == "Completed"); Assert.Equal("card.monthly_limit", Assert.Single(results, r => r.Status == "Rejected").FailureCode);
        var channel = await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 1m, channel = "Contactless" });
        Assert.Equal("card.channel_disabled", channel.FailureCode);
        await using var db = Context(); Assert.Equal(2000, (await db.Cards.SingleAsync()).SpentMinorUnits);
        Assert.Equal(80.2m, await Balance(alice, account.Id));
    }

    [PostgresFact]
    public async Task Card_routing_uses_keyed_fingerprints_and_idempotency_without_persisting_PAN()
    {
        using var host = Host(); using var alice = Client(host, "alice"); using var bob = Client(host, "bob");
        var a = await Open(alice); var b = await Open(bob); await Operation(alice, $"/api/accounts/{a.Id}/top-ups", new { amount = 100m });
        using var created = await bob.PostAsJsonAsync("/api/cards", new { accountId = b.Id }); var card = (await created.Content.ReadFromJsonAsync<CardResponse>())!;
        using var reveal = await bob.PostAsJsonAsync($"/api/cards/{card.Id}/requisites", new { password = "confirm-card-password" }); var secrets = (await reveal.Content.ReadFromJsonAsync<CardSecretsResponse>())!;
        var key = Guid.NewGuid(); var body = new { sourceAccountId = a.Id, number = secrets.Number, amount = 12.34m, memo = "Lunch" };
        var first = await Operation(alice, "/api/payments/card-transfers", body, key);
        var retry = await Operation(alice, "/api/payments/card-transfers", body, key);
        Assert.Equal(first.Id, retry.Id); Assert.Equal("Completed", first.Status); Assert.Equal("Lunch", first.Memo);
        Assert.Equal(12.34m, await Balance(bob, b.Id)); Assert.Equal(87.66m, await Balance(alice, a.Id));
        await using var db = Context(); Assert.DoesNotContain(secrets.Number, (await db.Transactions.SingleAsync(t => t.Id == first.Id)).RequestFingerprint);
        Assert.Equal(0, await db.Entries.SumAsync(e => e.AmountMinorUnits));
    }

    [PostgresFact]
    public async Task Admin_adjustments_compensations_and_restrictions_require_password_reason_and_audit()
    {
        using var host = WorkflowHost(); using var admin = Client(host, "alice"); using var bob = Client(host, "bob");
        var account = await Open(bob); await Operation(bob, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        var path = $"/api/admin/clients/{TestProfiles.Bob}/adjustments"; var key = Guid.NewGuid();
        var body = new { accountId = account.Id, amount = 5m, credit = true, reason = "Fee correction", password = "admin-confirm-password" };
        var adjusted = await Operation(admin, path, body, key); await Operation(admin, path, body, key);
        Assert.Equal(105m, await Balance(bob, account.Id));
        var listed = await admin.GetFromJsonAsync<AdminTransactionPage>("/api/admin/transactions?query=bob");
        Assert.Equal(TestProfiles.Bob, Assert.Single(listed!.Items, t => t.Id == adjusted.Id).ClientId);
        using var wrong = await Send(admin, path, new { accountId = account.Id, amount = 5m, credit = true, reason = "Fee correction", password = "wrong" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        using var denied = await Send(bob, path, body); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        (await Send(admin, $"/api/admin/transactions/{adjusted.Id}/actions", new { compensate = true, reason = "Undo correction", password = "admin-confirm-password" })).EnsureSuccessStatusCode();
        Assert.Equal(100m, await Balance(bob, account.Id));
        using var twice = await Send(admin, $"/api/admin/transactions/{adjusted.Id}/actions", new { compensate = true, reason = "Undo again", password = "admin-confirm-password" });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        (await Send(admin, $"/api/admin/clients/{TestProfiles.Bob}/controls", new { blocked = true, cardsFrozen = true, reason = "Account security review", password = "admin-confirm-password" })).EnsureSuccessStatusCode();
        using var blocked = await bob.PostAsync("/api/accounts", null); Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        using var newCard = await bob.PostAsJsonAsync("/api/cards", new { accountId = account.Id }); Assert.Equal(HttpStatusCode.Forbidden, newCard.StatusCode);
        var withdrawal = await Operation(bob, "/api/payments", new { accountId = account.Id, amount = 1m, kind = "MobileTopUp", reference = "+380991234567" });
        Assert.Equal("account.blocked", withdrawal.FailureCode); Assert.Equal(100m, await Balance(bob, account.Id));
        await using var db = Context(); Assert.Equal(3, await db.AdminAudit.CountAsync());
        Assert.Equal(TransactionStatus.Completed, (await db.Transactions.SingleAsync(t => t.Id == adjusted.Id)).Status);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE banking.admin_audit SET \"Reason\"='rewrite'"));
        Assert.Equal(0, await db.Entries.SumAsync(e => e.AmountMinorUnits));
    }

    [PostgresFact]
    public async Task Manual_KYC_decision_keeps_Didit_history_and_never_approves_a_later_attempt()
    {
        using var host = WorkflowHost(); using var admin = Client(host, "alice");
        await using var db = Context(); var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var verification = new KycVerification(TestProfiles.Bob, KycTestProvider.WorkflowId, true, now); verification.Start(now);
        verification.ApplyProviderResult("submitted-session", "provider-review", KycStatus.PendingReview, true, now);
        db.KycVerifications.Add(verification); await db.SaveChangesAsync();
        var body = new { approved = true, reason = "Evidence reviewed in provider dashboard", password = "admin-confirm-password" };
        var key = Guid.NewGuid();
        (await Send(admin, $"/api/admin/kyc/{verification.Id}/reviews", body, key)).EnsureSuccessStatusCode();
        (await Send(admin, $"/api/admin/kyc/{verification.Id}/reviews", body, key)).EnsureSuccessStatusCode();
        Assert.Equal(KycStatus.PendingReview, (await db.KycVerifications.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, await db.ManualReviews.CountAsync());
        var identity = await db.IdentitySnapshots.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("Bob Test", identity.Ciphertext);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM kyc.identity_snapshots"));
        var detail = await admin.GetFromJsonAsync<AdminClientDetail>($"/api/admin/clients/{TestProfiles.Bob}");
        Assert.Equal("Verified", detail!.Client.KycStatus);
        var queue = await admin.GetFromJsonAsync<AdminKycPage>("/api/admin/kyc?query=3653912301");
        Assert.Equal(verification.Id, Assert.Single(queue!.Items).Id); Assert.Equal(1, queue.ApprovedToday);
        using (var scope = host.Services.CreateScope())
        {
            var policy = new KycAccessPolicy(scope.ServiceProvider.GetRequiredService<IKycRepository>(), new KycTestProvider(), new BobCurrentUser());
            await policy.RequireVerifiedAsync(default);
        }
        var next = new KycVerification(TestProfiles.Bob, KycTestProvider.WorkflowId, true, DateTimeOffset.UtcNow, attemptNumber: 2); db.KycVerifications.Add(next); await db.SaveChangesAsync();
        using (var scope = host.Services.CreateScope())
        {
            var policy = new KycAccessPolicy(scope.ServiceProvider.GetRequiredService<IKycRepository>(), new KycTestProvider(), new BobCurrentUser());
            var failure = await Assert.ThrowsAsync<ApplicationFailureException>(() => policy.RequireVerifiedAsync(default)); Assert.Equal("kyc.required", failure.Code);
        }
        using var stale = await Send(admin, $"/api/admin/kyc/{verification.Id}/reviews", body); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM kyc.manual_reviews"));
    }

    [PostgresFact]
    public async Task Statements_include_all_rows_in_range_and_sandbox_payments_do_not_claim_external_delivery()
    {
        using var host = Host(); using var alice = Client(host, "alice"); using var bob = Client(host, "bob");
        var account = await Open(alice); await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        var payment = await Operation(alice, "/api/payments", new { accountId = account.Id, amount = 10m, kind = "MobileTopUp", reference = "+380991234567" });
        Assert.Equal("Completed", payment.Status); Assert.DoesNotContain("+38099", payment.Memo);
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd"); var path = $"/api/statements?from={date}&through={date}";
        var rows = (await alice.GetFromJsonAsync<TransactionResponse[]>(path))!; Assert.Equal(2, rows.Length);
        Assert.Empty((await bob.GetFromJsonAsync<TransactionResponse[]>(path))!);
        using var invalid = await alice.GetAsync("/api/statements?from=2025-01-01&through=2026-11-22"); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [PostgresFact]
    public async Task Legacy_card_limit_migration_conservatively_counts_current_month_account_purchases()
    {
        using var host = Host(); using var alice = Client(host, "alice");
        var account = await Open(alice); await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        using var first = await alice.PostAsJsonAsync("/api/cards", new { accountId = account.Id });
        var card = (await first.Content.ReadFromJsonAsync<CardResponse>())!;
        (await alice.PostAsJsonAsync("/api/cards", new { accountId = account.Id })).EnsureSuccessStatusCode();
        await Operation(alice, "/api/finance/purchases", new { cardId = card.Id, amount = 9.90m });
        await using var db = Context();
        await db.Database.ExecuteSqlRawAsync("UPDATE banking.cards SET \"SpentMinorUnits\"=0, \"SpendingMonth\"=0");
        await db.Database.ExecuteSqlRawAsync(Flenta.Infrastructure.Persistence.Banking.Migrations.DesktopWorkflows.LegacyCardSpendingBackfill);
        var cards = await db.Cards.AsNoTracking().ToListAsync();
        Assert.Equal(2, cards.Count); Assert.All(cards, c => Assert.Equal(990, c.SpentMinorUnits));
        Assert.All(cards, c => Assert.Equal(DateTime.UtcNow.Year * 100 + DateTime.UtcNow.Month, c.SpendingMonth));
    }

    [PostgresFact]
    public async Task Exchange_compensation_reverses_each_currency_and_describes_the_source_currency_correctly()
    {
        using var host = WorkflowHost().WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<IExchangeRateProvider>(); s.AddSingleton<IExchangeRateProvider, TestRates>(); }));
        using var admin = Client(host, "alice"); using var bob = Client(host, "bob");
        var uah = await OpenCurrency(bob, "UAH"); var usd = await OpenCurrency(bob, "USD");
        await Operation(bob, $"/api/accounts/{uah.Id}/top-ups", new { amount = 1000m });
        using var quoted = await bob.PostAsJsonAsync("/api/finance/exchange-quotes", new { sourceAccountId = uah.Id, destinationAccountId = usd.Id, amount = 400m });
        quoted.EnsureSuccessStatusCode(); var quote = (await quoted.Content.ReadFromJsonAsync<ExchangeQuoteResponse>())!;
        var exchange = await Operation(bob, "/api/finance/exchanges", new { quoteId = quote.Id });
        (await Send(admin, $"/api/admin/transactions/{exchange.Id}/actions", new { compensate = true, reason = "Reverse sandbox exchange", password = "admin-confirm-password" })).EnsureSuccessStatusCode();
        Assert.Equal(1000m, await Balance(bob, uah.Id)); Assert.Equal(0m, await Balance(bob, usd.Id));
        await using var db = Context(); var reversal = await db.Transactions.SingleAsync(t => t.Kind == TransactionKind.Compensation);
        Assert.Equal("USD", reversal.CurrencyCode); Assert.Equal(1000, reversal.AmountMinorUnits); Assert.Equal("UAH", reversal.DestinationCurrency); Assert.Equal(40000, reversal.ReceivedMinorUnits);
        var entries = await db.Entries.Where(e => e.TransactionId == reversal.Id).ToListAsync();
        Assert.All(entries.GroupBy(e => e.CurrencyCode), g => Assert.Equal(0, g.Sum(e => e.AmountMinorUnits)));
    }

    private sealed class AdminEvidenceProvider : IKycProvider
    {
        public KycProviderConfiguration Configuration => new("Didit", KycTestProvider.WorkflowId, true);
        public Task<KycSessionSnapshot> GetSessionAsync(string id, CancellationToken ct) => Task.FromResult(new KycSessionSnapshot(id, KycVerification.ExternalId(TestProfiles.Bob), KycTestProvider.WorkflowId, true, KycStatus.PendingReview, true, null,
            new KycIdentityData("Bob Test", new DateOnly(1990, 1, 1), "UKR", "3653912301", "TEST0001", "ID_CARD"), new KycDocumentEvidence("https://media.didit.me/fixture-front.jpg", null), true));
        public Task<KycSessionSnapshot> CreateSessionAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class BobCurrentUser : ICurrentUser
    {
        public UserProfile Profile => new(TestProfiles.Bob, Flenta.Domain.ValueObjects.EmailAddress.Create("bob@example.com"), "Bob", true);
        public string AccessToken => "bob";
    }
    private sealed class AdminCommandIdentity : IIdentityProvider
    {
        public Task<AuthSession> LoginAsync(string email, string password, CancellationToken ct)
        {
            if (password != "admin-confirm-password") throw new ApplicationFailureException(FailureKind.Unauthorized, "auth.invalid_credentials", "Invalid credentials.");
            return Task.FromResult(new AuthSession("temporary", "temporary-refresh", 3600, new ProfileResponse(TestProfiles.Alice, email, "Admin", true, true)));
        }
        public Task LogoutAsync(string token, CancellationToken ct) => Task.CompletedTask;
        public Task<RegistrationResult> RegisterAsync(string e, string p, string d, string c, CancellationToken ct) => throw new NotSupportedException();
        public Task<AuthSession> RefreshAsync(string t, CancellationToken ct) => throw new NotSupportedException();
        public Task<AuthSession> ExchangeCodeAsync(string c, string v, CancellationToken ct) => throw new NotSupportedException();
        public Task SendRecoveryEmailAsync(string e, string c, CancellationToken ct) => throw new NotSupportedException();
        public Task ChangePasswordAsync(string t, string p, CancellationToken ct) => throw new NotSupportedException();
    }
}
