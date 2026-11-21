namespace Flenta.Application.Abstractions.Kyc;

public sealed record KycIdentityData(string FullName, DateOnly? DateOfBirth, string Country, string? TaxNumber, string? DocumentNumber, string DocumentType);
public sealed record KycDocumentEvidence(string? FrontImage, string? BackImage);
public sealed record ProtectedIdentityData(string Ciphertext, string Digest, string? TaxFingerprint = null);
public interface IIdentityVault
{
    string TaxFingerprint(string taxNumber);
    ProtectedIdentityData Seal(Guid verification, KycIdentityData identity);
    KycIdentityData Open(Guid verification, string ciphertext);
}
