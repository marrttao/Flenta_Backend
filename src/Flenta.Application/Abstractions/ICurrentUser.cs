using Flenta.Domain.Entities;

namespace Flenta.Application.Abstractions;

public interface ICurrentUser
{
    UserProfile Profile { get; }
    string AccessToken { get; }
}
