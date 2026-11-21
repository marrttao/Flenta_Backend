using Flenta.Application.Features.Auth;
namespace Flenta.Application.Abstractions;

public sealed record MfaFactor(Guid Id, string Name, string Status);
public sealed record MfaEnrollment(Guid Id, string Secret);
public interface ISecondFactorProvider
{
    Task<IReadOnlyList<MfaFactor>> FactorsAsync(string token, CancellationToken ct);
    Task<MfaEnrollment> EnrollAsync(string token, CancellationToken ct);
    Task<AuthSession> VerifyAsync(string token, Guid factor, string code, CancellationToken ct);
    Task RemoveAsync(string token, Guid factor, CancellationToken ct);
    Task SignOutOthersAsync(string token, CancellationToken ct);
}
