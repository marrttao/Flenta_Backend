using Flenta.Domain.Entities;

namespace Flenta.Application.Abstractions.Kyc;

public sealed record KycProviderConfiguration(string Name, string ConfigurationId, bool Sandbox);
public sealed record KycSessionSnapshot(string SessionId, string ExternalUserId, string ConfigurationId, bool Sandbox,
    KycStatus Status, bool RequirementsMet, string? VerificationUrl);

public interface IKycProvider
{
    KycProviderConfiguration Configuration { get; }
    Task<KycSessionSnapshot> CreateSessionAsync(string externalUserId, CancellationToken cancellationToken);
    Task<KycSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken);
}

public interface IKycWebhookAuthenticator
{
    bool Verify(ReadOnlySpan<byte> body, string signature, string canonicalSignature, string timestamp);
    KycNotification? ReadNotification(byte[] body);
}

public sealed record KycNotification(string Provider, string SessionId, string? ExternalUserId, string? ConfigurationId, bool? Sandbox, string Digest);
