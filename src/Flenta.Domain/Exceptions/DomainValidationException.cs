namespace Flenta.Domain.Exceptions;

public sealed class DomainValidationException(string code, string message)
    : DomainException(code, message);
