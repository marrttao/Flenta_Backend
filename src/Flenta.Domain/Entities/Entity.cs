namespace Flenta.Domain.Entities;

/// <summary>Base for entities whose identity is assigned by the domain.</summary>
public abstract class Entity
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", nameof(id));
        }

        Id = id;
    }

    public Guid Id { get; }
}
