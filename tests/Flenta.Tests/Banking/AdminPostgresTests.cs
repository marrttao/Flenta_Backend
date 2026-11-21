using System.Net;
using System.Net.Http.Json;
using Flenta.Application.Abstractions;
using Flenta.Application.Features.Admin;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flenta.Tests.Banking;

public sealed partial class PostgresApiTests
{
    [PostgresFact]
    public async Task Admin_reports_use_latest_KYC_exclude_deleted_users_and_do_not_change_financial_data()
    {
        using var bankingHost = Host();
        using var alice = Client(bankingHost, "alice");
        var account = await Open(alice);
        await Operation(alice, $"/api/accounts/{account.Id}/top-ups", new { amount = 100m });
        await using var db = Context();
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var approved = new KycVerification(TestProfiles.Alice, "admin-fixture", true, now);
        approved.Start(now);
        approved.ApplyProviderResult("alice-approved", "review-1", KycStatus.Verified, true, now);
        var deleted = new KycVerification(Guid.NewGuid(), "admin-fixture", true, now);
        deleted.Start(now);
        deleted.ApplyProviderResult("deleted-approved", "review-2", KycStatus.Verified, true, now);
        db.KycVerifications.AddRange(approved, deleted);
        await db.SaveChangesAsync();

        using var administrativeHost = Host().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserProfileRepository>();
            services.AddSingleton<IUserProfileRepository, AdminReadProfiles>();
            services.Configure<SupabaseOptions>(options =>
            {
                options.Enabled = true;
                options.Url = "http://127.0.0.1:9999";
                options.PublishableKey = "sb_publishable_test_fixture";
            });
            services.Configure<AdminOptions>(options => options.SupabaseSecretKey = "sb_secret_test_fixture");
            services.AddHttpClient("SupabaseAdminDirectory").ConfigurePrimaryHttpMessageHandler(() => new AdminReadDirectory());
        }));
        using var admin = Client(administrativeHost, "alice");
        var overview = await admin.GetFromJsonAsync<AdminOverviewResponse>("/api/admin/overview");
        Assert.NotNull(overview);
        Assert.Equal(2, overview.TotalClients);
        Assert.Equal(1, overview.VerifiedKycClients);
        Assert.Equal(1, overview.TodayTransactions);
        Assert.Equal("Alice", Assert.Single(overview.RecentTransactions).ClientName);
        var clients = await admin.GetFromJsonAsync<AdminClientsResponse>("/api/admin/clients?email=alice&pageSize=8");
        Assert.NotNull(clients);
        Assert.Equal(2, clients.TotalClients);
        Assert.Equal("Verified", Assert.Single(clients.Items).KycStatus);

        var latest = new KycVerification(TestProfiles.Alice, "admin-fixture", true, now.AddMinutes(1), attemptNumber: 2);
        latest.Start(now.AddMinutes(1));
        latest.ApplyProviderResult("alice-rejected", "review-3", KycStatus.Rejected, false, now.AddMinutes(1));
        db.KycVerifications.Add(latest);
        await db.SaveChangesAsync();
        var updated = await admin.GetFromJsonAsync<AdminOverviewResponse>("/api/admin/overview");
        Assert.Equal(0, updated?.VerifiedKycClients);
        var updatedClients = await admin.GetFromJsonAsync<AdminClientsResponse>("/api/admin/clients?email=alice");
        Assert.Equal("Rejected", Assert.Single(updatedClients!.Items).KycStatus);
        Assert.Equal(100m, await Balance(alice, account.Id));
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    private sealed class AdminReadProfiles : IUserProfileRepository
    {
        public async Task<UserProfile> GetCurrentAsync(string accessToken, CancellationToken ct)
        {
            var profile = await new TestProfiles().GetCurrentAsync(accessToken, ct);
            return new UserProfile(profile.Id, profile.Email, profile.DisplayName, profile.EmailConfirmed, profile.Id == TestProfiles.Alice);
        }
        public Task<UserProfile> UpdateCurrentAsync(string accessToken, string displayName, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class AdminReadDirectory : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            var users = new[] { new { id = TestProfiles.Alice, email = "alice@example.com", created_at = "2026-11-01T00:00:00Z",
                email_confirmed_at = "2026-11-01T00:00:00Z", user_metadata = new { display_name = "Alice" } },
                new { id = TestProfiles.Bob, email = "bob@example.com", created_at = "2026-11-01T00:00:00Z",
                email_confirmed_at = "2026-11-01T00:00:00Z", user_metadata = new { display_name = "Bob" } } };
            var filtered = query.ContainsKey("filter") ? users.Where(u => u.email.Contains("alice", StringComparison.Ordinal)) : users.AsEnumerable();
            var all = filtered.ToArray();
            var size = int.Parse(query["per_page"].ToString());
            var page = int.Parse(query["page"].ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { users = all.Skip((page - 1) * size).Take(size).ToArray() }) };
            response.Headers.Add("x-total-count", all.Length.ToString());
            return Task.FromResult(response);
        }
    }
}
