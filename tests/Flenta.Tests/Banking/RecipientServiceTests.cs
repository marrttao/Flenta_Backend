using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Abstractions.Desktop;
using Flenta.Application.Features.Recipients;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;

namespace Flenta.Tests.Banking;

public sealed class RecipientServiceTests
{
    [Fact]
    public async Task Retrying_an_existing_recipient_at_the_limit_returns_it_without_creating_a_duplicate()
    {
        var user = new CurrentUser();
        var repository = Filled(user.Profile.Id, 100);
        var existing = repository.Items[0];
        var service = new RecipientService(repository, new UnitOfWork(), user, new Clock());

        var result = await service.AddAsync("Changed name", existing.Iban.ToLowerInvariant().Insert(4, " "), default);

        Assert.Equal(existing.Id, result.Id);
        Assert.Equal(existing.Name, result.Name);
        Assert.Equal(100, repository.Items.Count);
    }

    [Fact]
    public async Task A_new_recipient_is_still_rejected_at_the_limit()
    {
        var user = new CurrentUser();
        var repository = Filled(user.Profile.Id, 100);
        var service = new RecipientService(repository, new UnitOfWork(), user, new Clock());
        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.AddAsync("New", Iban.CreateSandbox(Guid.NewGuid()).Value, default));
        Assert.Equal("recipient.limit", error.Code);
        Assert.Equal(100, repository.Items.Count);
    }

    [Fact]
    public async Task A_new_recipient_can_fill_the_last_available_slot()
    {
        var user = new CurrentUser();
        var repository = Filled(user.Profile.Id, 99);
        var service = new RecipientService(repository, new UnitOfWork(), user, new Clock());
        var iban = Iban.CreateSandbox(Guid.NewGuid()).Value;
        var result = await service.AddAsync("New", iban, default);
        Assert.Equal(iban, result.Iban);
        Assert.Equal(100, repository.Items.Count);
    }

    private static Repository Filled(Guid owner, int count)
    {
        var repository = new Repository();
        for (var index = 0; index < count; index++)
            repository.Items.Add(new SavedRecipient(owner, $"Recipient {index}", Iban.CreateSandbox(Guid.NewGuid()).Value, DateTimeOffset.UtcNow));
        return repository;
    }

    private sealed class Repository : IRecipientRepository
    {
        public List<SavedRecipient> Items { get; } = [];
        public Task<IReadOnlyList<SavedRecipient>> RecipientsAsync(Guid owner, CancellationToken ct) => Task.FromResult<IReadOnlyList<SavedRecipient>>(Items);
        public Task<SavedRecipient?> RecipientAsync(Guid owner, Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(r => r.Id == id && r.OwnerId == owner));
        public void Add(SavedRecipient recipient) => Items.Add(recipient);
        public void Remove(SavedRecipient recipient) => Items.Remove(recipient);
    }

    private sealed class UnitOfWork : IBankingUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(Guid owner, Guid key, Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);
    }
    private sealed class CurrentUser : ICurrentUser
    {
        public UserProfile Profile { get; } = new(Guid.NewGuid(), EmailAddress.Create("test@example.com"), "Test", true);
        public string AccessToken => "fixture";
    }
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    }
}
