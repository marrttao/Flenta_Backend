using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Tests.Domain;

public sealed class EmailAddressTests
{
    [Fact]
    public void Create_trims_surrounding_whitespace_and_preserves_the_address()
    {
        var email = EmailAddress.Create("  Alice@example.com  ");

        Assert.Equal("Alice@example.com", email.Value);
    }

    [Fact]
    public void Equal_addresses_have_value_equality()
    {
        Assert.Equal(EmailAddress.Create("alice@example.com"), EmailAddress.Create("alice@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("Alice <alice@example.com>")]
    [InlineData("alice@example.com,bob@example.com")]
    [InlineData("alice@example.com\r\nBcc: bob@example.com")]
    public void Invalid_addresses_raise_a_domain_validation_error(string? value)
    {
        var error = Assert.Throws<DomainValidationException>(() => EmailAddress.Create(value));

        Assert.Equal("email.invalid", error.Code);
    }

    [Fact]
    public void Addresses_over_the_length_limit_are_rejected()
    {
        var value = new string('a', 243) + "@example.com";

        Assert.Throws<DomainValidationException>(() => EmailAddress.Create(value));
    }
}
