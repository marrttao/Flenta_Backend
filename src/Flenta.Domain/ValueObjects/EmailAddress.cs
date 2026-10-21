using System.Net.Mail;
using Flenta.Domain.Exceptions;

namespace Flenta.Domain.ValueObjects;

/// <summary>An immutable address; display names and address lists are not accepted.</summary>
public sealed record EmailAddress
{
    private EmailAddress(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static EmailAddress Create(string? value)
    {
        var candidate = value?.Trim();

        if (string.IsNullOrEmpty(candidate)
            || candidate.Length > 254
            || !MailAddress.TryCreate(candidate, out var address)
            || !string.Equals(address.Address, candidate, StringComparison.Ordinal))
        {
            throw new DomainValidationException("email.invalid", "A valid email address is required.");
        }

        return new EmailAddress(candidate);
    }

    public override string ToString() => Value;
}
