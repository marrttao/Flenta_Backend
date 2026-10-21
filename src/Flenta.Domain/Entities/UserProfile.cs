using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Domain.Entities;

public sealed class UserProfile : Entity
{
    public UserProfile(Guid id, EmailAddress email, string displayName, bool emailConfirmed) : base(id)
    {
        Email = email;
        EmailConfirmed = emailConfirmed;
        Rename(displayName);
    }

    public EmailAddress Email { get; }
    public bool EmailConfirmed { get; }
    public string DisplayName { get; private set; } = string.Empty;

    public void Rename(string? displayName)
    {
        DisplayName = NormalizeDisplayName(displayName);
    }

    public static string NormalizeDisplayName(string? displayName)
    {
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100 || name.Any(char.IsControl))
        {
            throw new DomainValidationException("profile.invalid_name", "Display name must contain 1–100 characters without control characters.");
        }

        return name;
    }
}
