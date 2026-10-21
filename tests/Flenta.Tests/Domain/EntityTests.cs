using Flenta.Domain.Entities;

namespace Flenta.Tests.Domain;

public sealed class EntityTests
{
    [Fact]
    public void An_entity_cannot_have_an_empty_identifier()
    {
        Assert.Throws<ArgumentException>(() => new ExampleEntity(Guid.Empty));
    }

    private sealed class ExampleEntity(Guid id) : Entity(id);
}
