using System.Security.Cryptography;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Flenta.Infrastructure.Persistence.Banking;
using Flenta.Infrastructure.Security;
using Flenta.Infrastructure.Kyc;
using Microsoft.Extensions.Options;
namespace Flenta.Tests.Kyc;

public sealed class IdentityVaultTests
{
    [Fact]
    public void Identity_encryption_is_attempt_bound_nondeterministic_and_authenticated()
    {
        var vault = new IdentityVault(Options.Create(new BankingOptions { CardEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
        var id = Guid.NewGuid(); var identity = new KycIdentityData("Test Person", new DateOnly(1990, 1, 2), "UKR", "3653912301", "TEST0001", "ID_CARD");
        var first = vault.Seal(id, identity); var second = vault.Seal(id, identity);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext); Assert.Equal(first.Digest, second.Digest);
        Assert.DoesNotContain(identity.FullName, first.Ciphertext); Assert.Equal(identity, vault.Open(id, first.Ciphertext));
        Assert.Throws<ApplicationFailureException>(() => vault.Open(Guid.NewGuid(), first.Ciphertext));
        var bytes = Convert.FromBase64String(first.Ciphertext[3..]); bytes[^1] ^= 1;
        Assert.Throws<ApplicationFailureException>(() => vault.Open(id, "v1." + Convert.ToBase64String(bytes)));
    }
    [Theory]
    [InlineData("http://media.didit.me/front.jpg")]
    [InlineData("https://127.0.0.1/front.jpg")]
    [InlineData("https://localhost/front.jpg")]
    [InlineData("https://server.local/front.jpg")]
    [InlineData("https://user:secret@media.didit.me/front.jpg")]
    [InlineData("javascript:alert(1)")]
    public void Document_links_reject_local_or_unsafe_locations(string url) => Assert.Null(DiditClient.SafeDocumentUrl(url));
    [Fact]
    public void Signed_https_document_links_keep_their_capability_query()
    {
        const string url = "https://media.didit.me/front.jpg?signature=fixture";
        Assert.Equal(url, DiditClient.SafeDocumentUrl(url));
    }
}
