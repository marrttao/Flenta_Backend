namespace Flenta.Domain.Exceptions;

/// <summary>A business-rule violation with a stable, client-safe error code.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
