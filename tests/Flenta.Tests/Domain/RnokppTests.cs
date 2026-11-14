using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Tests.Domain;

public sealed class RnokppTests
{
    [Theory]
    [InlineData("3653912301")]
    [InlineData("3592845608")]
    [InlineData("3135378905")]
    public void Numbers_with_a_correct_control_digit_are_accepted(string value)
    {
        Assert.Equal(value, Rnokpp.Parse(value).Value);
    }

    [Fact]
    public void A_remainder_of_ten_gives_a_control_digit_of_zero()
    {
        Assert.Equal("3600212300", Rnokpp.Parse("3600212300").Value);
    }

    [Fact]
    public void Parse_trims_surrounding_whitespace()
    {
        Assert.Equal("3653912301", Rnokpp.Parse("  3653912301 ").Value);
    }

    [Fact]
    public void Equal_numbers_have_value_equality()
    {
        Assert.Equal(Rnokpp.Parse("3653912301"), Rnokpp.Parse("3653912301"));
    }

    [Theory]
    [InlineData("3653912300")]
    [InlineData("3653912302")]
    [InlineData("3592845609")]
    public void A_wrong_control_digit_is_rejected(string value)
    {
        var error = Assert.Throws<DomainValidationException>(() => Rnokpp.Parse(value));

        Assert.Equal("rnokpp.invalid", error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("365391230")]
    [InlineData("36539123011")]
    [InlineData("36539123O1")]
    [InlineData("365 912301")]
    [InlineData("-653912301")]
    public void Malformed_numbers_raise_a_domain_validation_error(string? value)
    {
        var error = Assert.Throws<DomainValidationException>(() => Rnokpp.Parse(value));

        Assert.Equal("rnokpp.invalid", error.Code);
    }
}
