using Flenta.Application.Features.Auth;
using Flenta.Domain.Exceptions;

namespace Flenta.Tests.Domain;

public sealed class PkceValidationTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(" ")]
    public async Task Challenge_must_match_the_entire_input(string suffix)
    {
        // Invalid input must be rejected before an identity provider is accessed.
        var service = new AuthenticationService(null!);
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.RegisterAsync("test@example.com", "a-valid-long-password", "Test",
                new string('a', 43) + suffix, CancellationToken.None));
        Assert.Equal("auth.invalid_challenge", exception.Code);
    }
}
