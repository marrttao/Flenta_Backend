using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Tests.Domain;

public sealed class IbanTests
{
    private const string Valid = "UA213223130000026007233566001";

    [Fact]
    public void Parse_accepts_a_valid_ukrainian_iban()
    {
        Assert.Equal(Valid, Iban.Parse(Valid).Value);
    }

    [Fact]
    public void Parse_ignores_spaces_and_letter_case()
    {
        Assert.Equal(Valid, Iban.Parse("ua21 3223 1300 0002 6007 2335 6600 1").Value);
    }

    [Fact]
    public void Equal_ibans_have_value_equality()
    {
        Assert.Equal(Iban.Parse(Valid), Iban.Parse(Valid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UA21322313000002600723356600")]
    [InlineData("UA2132231300000260072335660012")]
    [InlineData("UB213223130000026007233566001")]
    [InlineData("UA21322313000002600723356600X")]
    [InlineData("UA213223130000026007233566002")]
    public void Invalid_ibans_raise_a_domain_validation_error(string? value)
    {
        var error = Assert.Throws<DomainValidationException>(() => Iban.Parse(value));

        Assert.Equal("iban.invalid", error.Code);
    }

    [Fact]
    public void Sandbox_ibans_are_deterministic_and_use_the_reserved_routing_code()
    {
        var accountId = Guid.Parse("10000000-0000-0000-0000-000000000001");

        var iban = Iban.CreateSandbox(accountId);

        Assert.Equal(iban, Iban.CreateSandbox(accountId));
        Assert.Equal(29, iban.Value.Length);
        Assert.StartsWith("UA", iban.Value);
        Assert.Equal("999999", iban.Value.Substring(4, 6));
        Assert.Equal(iban, Iban.Parse(iban.Value));
    }

    [Fact]
    public void Different_accounts_get_different_sandbox_ibans()
    {
        var first = Iban.CreateSandbox(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var second = Iban.CreateSandbox(Guid.Parse("10000000-0000-0000-0000-000000000002"));

        Assert.NotEqual(first, second);
    }
}
