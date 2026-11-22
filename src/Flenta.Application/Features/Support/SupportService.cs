using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Abstractions.Desktop;
using Flenta.Application.Features.Accounts;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
namespace Flenta.Application.Features.Support;

public sealed record SupportSummary(Guid Id, string Subject, string Kind, string Status, DateTimeOffset UpdatedAt, Guid OwnerId);
public sealed record MessageResponse(Guid Id, bool IsStaff, string Body, DateTimeOffset CreatedAt);
public sealed record SupportConversation(SupportSummary Request, IReadOnlyList<MessageResponse> Messages);
public sealed class SupportService(ISupportRepository repository, IBankingUnitOfWork work, ICurrentUser user, IClock clock)
{
    private bool Staff(bool staff) { if (staff && (!user.Profile.IsAdmin || !user.Profile.EmailConfirmed)) throw new ApplicationFailureException(FailureKind.Forbidden, "admin.forbidden", "Administrator access is required."); return staff; }
    public async Task<IReadOnlyList<SupportSummary>> ListAsync(bool staff, CancellationToken ct) => (await repository.RequestsAsync(user.Profile.Id, Staff(staff), ct)).Select(Map).ToArray();
    public async Task<SupportConversation> GetAsync(Guid id, bool staff, CancellationToken ct)
    {
        var request = await repository.RequestAsync(user.Profile.Id, id, Staff(staff), ct) ?? throw AccountService.Missing();
        return new(Map(request), (await repository.MessagesAsync(id, ct)).Select(m => new MessageResponse(m.Id, m.IsStaff, m.Body, m.CreatedAt)).ToArray());
    }
    public Task<SupportSummary> CreateAsync(string subject, string kind, string body, CancellationToken ct) =>
        work.ExecuteAsync(user.Profile.Id, user.Profile.Id, async token =>
        {
            if ((await repository.RequestsAsync(user.Profile.Id, false, token)).Count(r => r.Status == "Open") >= 20)
                throw new DomainValidationException("support.limit", "Close an existing request before creating another.");
            var request = new SupportRequest(user.Profile.Id, subject, kind, clock.UtcNow);
            repository.Add(request); repository.Add(new SupportMessage(request.Id, user.Profile.Id, false, body, clock.UtcNow)); return Map(request);
        }, ct);
    public Task<bool> ReplyAsync(Guid id, string body, bool staff, CancellationToken ct) =>
        work.ExecuteAsync(user.Profile.Id, id, async token =>
        {
            var request = await repository.RequestAsync(user.Profile.Id, id, Staff(staff), token) ?? throw AccountService.Missing();
            if ((await repository.MessagesAsync(id, token)).Count >= 1000) throw new DomainValidationException("support.message_limit", "Open a new request to continue.");
            request.Touch(clock.UtcNow); repository.Add(new SupportMessage(id, user.Profile.Id, staff, body, clock.UtcNow)); return true;
        }, ct);
    public Task<bool> CloseAsync(Guid id, bool staff, CancellationToken ct) =>
        work.ExecuteAsync(user.Profile.Id, id, async token =>
        {
            var request = await repository.RequestAsync(user.Profile.Id, id, Staff(staff), token) ?? throw AccountService.Missing();
            request.Close(clock.UtcNow); return true;
        }, ct);
    private static SupportSummary Map(SupportRequest r) => new(r.Id, r.Subject, r.Kind, r.Status, r.UpdatedAt, r.OwnerId);
}
