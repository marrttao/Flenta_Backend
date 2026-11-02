using System.Net;
using System.Net.Http.Json;
using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Kyc;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Kyc;
using Flenta.Tests.Kyc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Flenta.Tests.Banking;

public sealed partial class PostgresApiTests
{
    private WebApplicationFactory<Program> KycDatabaseHost(KycTestProvider provider) => Host().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.RemoveAll<IKycAccessPolicy>(); services.AddScoped<IKycAccessPolicy, KycAccessPolicy>();
        services.RemoveAll<IKycProvider>(); services.AddSingleton<IKycProvider>(provider);
        services.RemoveAll<IUserProfileRepository>(); services.AddSingleton<IUserProfileRepository, KycProfiles>();
        services.Configure<DiditOptions>(o => { o.Enabled = true; o.ApiKey = "api-fixture"; o.WebhookSecret = KycApiTests.WebhookSecret; o.WorkflowId = KycTestProvider.WorkflowId; });
    }));

    [PostgresFact]
    public async Task Kyc_results_and_concurrent_duplicate_webhooks_are_persisted_once_and_gate_real_banking()
    {
        var provider = new KycTestProvider { Status = KycStatus.Verified };
        using var host = KycDatabaseHost(provider); using var alice = Client(host, "alice"); using var hooks = KycApiTests.Client(host, null);
        using var blocked = await alice.PostAsync("/api/accounts", null); Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        using var start = await alice.PostAsync("/api/kyc/session", null); start.EnsureSuccessStatusCode();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => KycApiTests.SendWebhook(hooks, KycApiTests.Webhook())));
        foreach (var result in results) { result.EnsureSuccessStatusCode(); result.Dispose(); }
        var account = await Open(alice);
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        Assert.Equal(100m, await Balance(alice, account.Id));
        await using var db = Context();
        Assert.Equal(KycStatus.Verified, (await db.KycVerifications.SingleAsync()).Status);
        Assert.Equal(1, await db.KycWebhookReceipts.CountAsync()); Assert.Equal(2, await db.KycReviewEvents.CountAsync());
        using var bob = Client(host, "bob"); using var foreign = await bob.GetAsync("/api/accounts"); Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        provider.Status = KycStatus.PendingReview;
        using var reset = await KycApiTests.SendWebhook(hooks, KycApiTests.Webhook("reset")); reset.EnsureSuccessStatusCode();
        using var revoked = await alice.GetAsync("/api/accounts"); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [PostgresFact]
    public async Task Kyc_database_failure_rolls_back_status_audit_and_receipt_and_allows_same_event_retry()
    {
        var provider = new KycTestProvider { Status = KycStatus.Verified };
        using var host = KycDatabaseHost(provider); using var alice = Client(host, "alice"); using var hooks = KycApiTests.Client(host, null);
        using var start = await alice.PostAsync("/api/kyc/session", null); start.EnsureSuccessStatusCode();
        await using var db = Context();
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION kyc.fail_test_review() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'private-write-failure'; END $$; CREATE TRIGGER fail_test_review BEFORE INSERT ON kyc.review_events FOR EACH ROW EXECUTE FUNCTION kyc.fail_test_review();");
        using var failed = await KycApiTests.SendWebhook(hooks, KycApiTests.Webhook());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode); Assert.DoesNotContain("private-write-failure", await failed.Content.ReadAsStringAsync());
        Assert.Equal(KycStatus.InProgress, (await db.KycVerifications.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, await db.KycReviewEvents.CountAsync()); Assert.Equal(0, await db.KycWebhookReceipts.CountAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_test_review ON kyc.review_events; DROP FUNCTION kyc.fail_test_review();");
        using var retry = await KycApiTests.SendWebhook(hooks, KycApiTests.Webhook()); retry.EnsureSuccessStatusCode();
        Assert.True((await alice.GetFromJsonAsync<KycStatusResponse>("/api/kyc"))!.CanAccess);
        Assert.Equal(1, await db.KycWebhookReceipts.CountAsync()); Assert.Equal(2, await db.KycReviewEvents.CountAsync());
    }

    [PostgresFact]
    public async Task Kyc_audit_and_identity_are_immutable_and_private_tables_have_rls()
    {
        using var host = KycDatabaseHost(new KycTestProvider { Status = KycStatus.Verified });
        using var alice = Client(host, "alice"); using var hooks = KycApiTests.Client(host, null);
        using var start = await alice.PostAsync("/api/kyc/session", null); start.EnsureSuccessStatusCode();
        using var approved = await KycApiTests.SendWebhook(hooks, KycApiTests.Webhook()); approved.EnsureSuccessStatusCode();
        await using var db = Context();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM kyc.review_events"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE kyc.webhook_receipts SET \"ReceivedAt\"=now()"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE kyc.verifications SET \"Sandbox\"=false"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM kyc.verifications"));
        var protectedTables = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_tables WHERE schemaname='kyc' AND rowsecurity").SingleAsync();
        Assert.Equal(3, protectedTables);
    }

    [PostgresFact]
    public async Task Didit_migration_preserves_legacy_references_and_old_approval_does_not_unlock_Didit()
    {
        await using var db = Context(); var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261101125930_KycVerification");
        var verification = Guid.NewGuid(); var owner = KycProfiles.Alice; var review = Guid.NewGuid();
        var external = KycVerification.ExternalId(owner);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO kyc.verifications (\"Id\",\"OwnerId\",\"ExternalUserId\",\"LevelName\",\"Sandbox\",\"ApplicantId\",\"ReviewId\",\"Status\",\"CreatedAt\",\"UpdatedAt\",\"VerifiedAt\") VALUES ({verification},{owner},{external},'old-level',true,'old-applicant','old-review','Verified',now(),now(),now())");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO kyc.review_events (\"Id\",\"VerificationId\",\"OwnerId\",\"PreviousStatus\",\"Status\",\"CreatedAt\") VALUES ({review},{verification},{owner},'InProgress','Verified',now())");
        await migrator.MigrateAsync();
        var legacy = await db.KycVerifications.AsNoTracking().SingleAsync();
        Assert.Equal("Sumsub", legacy.ProviderName); Assert.Equal("old-applicant", legacy.ProviderSessionId);
        Assert.Equal("old-review", legacy.DecisionReference); Assert.Equal("old-level", legacy.ConfigurationId);
        Assert.Equal(1, await db.KycReviewEvents.CountAsync());
        using var host = KycDatabaseHost(new KycTestProvider()); using var alice = Client(host, "alice");
        Assert.Equal("NotStarted", (await alice.GetFromJsonAsync<KycStatusResponse>("/api/kyc"))!.Status);
        using var blocked = await alice.GetAsync("/api/profile"); Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        using var started = await alice.PostAsync("/api/kyc/session", null); started.EnsureSuccessStatusCode();
        Assert.Equal(2, await db.KycVerifications.CountAsync());
    }
}
