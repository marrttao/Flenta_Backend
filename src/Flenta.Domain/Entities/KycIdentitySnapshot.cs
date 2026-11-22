namespace Flenta.Domain.Entities;

public sealed class KycIdentitySnapshot : Entity
{
    private KycIdentitySnapshot() : base(Guid.NewGuid()) { }
    public KycIdentitySnapshot(Guid verification, string ciphertext, string digest, DateTimeOffset now, string? taxFingerprint = null) : base(Guid.NewGuid())
    {
        if (verification == Guid.Empty || ciphertext.Length is < 32 or > 8192 || digest.Length != 64) throw new ArgumentException("Invalid protected identity snapshot.");
        VerificationId = verification; Ciphertext = ciphertext; Digest = digest; CreatedAt = now; TaxFingerprint = taxFingerprint;
    }
    public Guid VerificationId { get; private set; }
    public string Ciphertext { get; private set; } = "";
    public string? TaxFingerprint { get; private set; }
    public string Digest { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
}
