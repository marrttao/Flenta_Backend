namespace Flenta.Application.Abstractions.Banking;

public sealed record ProtectedCardData(string Last4, string Ciphertext, string Fingerprint);
public sealed record CardSecrets(string Number, string Cvv);

public interface ICardVault
{
    ProtectedCardData Create(Guid owner, Guid cardId);
    CardSecrets Reveal(Guid owner, Guid cardId, string ciphertext);
}
