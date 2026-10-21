using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Tests.Domain;

public sealed class UserProfileTests
{
    [Fact]
    public void Rename_preserves_identity_and_email()
    {
        var id = Guid.NewGuid();
        var profile = new UserProfile(id, EmailAddress.Create("test@example.com"), "Test", true);
        profile.Rename("  New Name  ");
        Assert.Equal("New Name", profile.DisplayName);
        Assert.Equal(id, profile.Id);
        Assert.Equal("test@example.com", profile.Email.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Name\nOther")]
    public void Invalid_names_do_not_mutate_the_profile(string name)
    {
        var profile = new UserProfile(Guid.NewGuid(), EmailAddress.Create("test@example.com"), "Original", true);
        Assert.Throws<DomainValidationException>(() => profile.Rename(name));
        Assert.Equal("Original", profile.DisplayName);
    }
}
