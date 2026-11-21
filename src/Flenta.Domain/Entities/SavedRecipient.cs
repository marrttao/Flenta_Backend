using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;
namespace Flenta.Domain.Entities;

public sealed class SavedRecipient : Entity
{
    private SavedRecipient() : base(Guid.NewGuid()) { }
    public SavedRecipient(Guid owner, string name, string iban, DateTimeOffset now) : base(Guid.NewGuid())
    {
        if (owner == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new DomainValidationException("recipient.invalid_name", "A recipient name of up to 100 characters is required.");
        OwnerId = owner; Name = name.Trim(); Iban = ValueObjects.Iban.Parse(iban).Value; CreatedAt = now;
    }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = "";
    public string Iban { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
}
