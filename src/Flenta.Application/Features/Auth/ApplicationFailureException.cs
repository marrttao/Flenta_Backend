namespace Flenta.Application.Features.Auth;

public enum FailureKind { Unauthorized, Forbidden, Conflict, RateLimited, Unavailable, InvalidRequest, NotFound }

public sealed class ApplicationFailureException(FailureKind kind, string code, string message) : Exception(message)
{
    public FailureKind Kind { get; } = kind;
    public string Code { get; } = code;
}
