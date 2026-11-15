using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Domain.Entities;
using Flenta.Infrastructure.Configuration;
using Flenta.Tests.Kyc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flenta.Tests.Banking;

public sealed class CardRequisitesTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, "card.confirmation_failed")]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "auth.provider_unavailable")]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "auth.rate_limited")]
    public async Task Failed_confirmation_keeps_the_current_session_and_never_reveals_secrets(
        HttpStatusCode providerStatus, HttpStatusCode expectedStatus, string expectedCode)
    {
        var repository = new CardRepository();
        using var provider = new RejectedConfirmation(providerStatus);
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IKycAccessPolicy, ApprovedKycPolicy>();
            services.RemoveAll<IUserProfileRepository>();
            services.AddSingleton<IUserProfileRepository, TestProfiles>();
            services.RemoveAll<IBankingRepository>();
            services.AddSingleton<IBankingRepository>(repository);
            services.Configure<SupabaseOptions>(options =>
            {
                options.Enabled = true;
                options.Url = "http://127.0.0.1:9999";
                options.PublishableKey = "sb_publishable_test_fixture";
            });
            services.AddHttpClient("SupabaseAuthClient").ConfigurePrimaryHttpMessageHandler(() => provider);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "alice");

        using var denied = await client.PostAsJsonAsync($"/api/cards/{repository.Card.Id}/requisites", new { password = "incorrect-password" });
        Assert.Equal(expectedStatus, denied.StatusCode);
        var body = await denied.Content.ReadAsStringAsync();
        Assert.Contains(expectedCode, body);
        Assert.DoesNotContain("cvv", body);
        Assert.Null(repository.Card.LastSecretsViewedAt);
        Assert.Equal(1, provider.Requests);

        using var profile = await client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var foreign = await client.PostAsJsonAsync($"/api/cards/{Guid.NewGuid()}/requisites", new { password = "incorrect-password" });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(1, provider.Requests);
    }

    private sealed class RejectedConfirmation(HttpStatusCode status) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.EndsWith("/auth/v1/token?grant_type=password", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = JsonContent.Create(new { error_code = status == HttpStatusCode.Unauthorized ? "invalid_credentials" : "provider_error" })
            });
        }
    }

    private sealed class CardRepository : IBankingRepository
    {
        public VirtualCard Card { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "1234", "unread-ciphertext", "fingerprint", 1, 2030, DateTimeOffset.UtcNow);
        public Task<VirtualCard?> OwnedCardAsync(Guid owner, Guid cardId, bool forUpdate, CancellationToken cancellationToken)
        {
            Assert.False(forUpdate);
            return Task.FromResult(owner == TestProfiles.Alice && cardId == Card.Id ? Card : null);
        }

        public Task<IReadOnlyList<BankAccount>> AccountsAsync(Guid owner, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BankAccount?> OwnedAccountAsync(Guid owner, Guid accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BankAccount?> AccountByIbanAsync(string iban, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BankAccount>> LockAccountsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<VirtualCard>> CardsAsync(Guid owner, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BankTransaction?> ByKeyAsync(Guid owner, Guid key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BankTransaction>> TransactionsAsync(Guid owner, int offset, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BankTransaction?> OwnedTransactionAsync(Guid owner, Guid transactionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Add(BankAccount account) => throw new NotSupportedException();
        public void Add(VirtualCard card) => throw new NotSupportedException();
        public void Add(BankTransaction transaction) => throw new NotSupportedException();
        public void AddEntries(IEnumerable<LedgerEntry> entries) => throw new NotSupportedException();
    }
}
