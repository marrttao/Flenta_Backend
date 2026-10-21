namespace Flenta.Application.Abstractions;

/// <summary>Lets use cases obtain time without depending on a system clock.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
