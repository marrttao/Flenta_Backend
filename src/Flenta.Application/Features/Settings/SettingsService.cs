using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Abstractions.Desktop;
using Flenta.Domain.Entities;
namespace Flenta.Application.Features.Settings;

public sealed record SettingsResponse(string Phone, string DefaultCurrency, bool TransactionNotifications);
public sealed class SettingsService(ISettingsRepository repository, IBankingUnitOfWork work, ICurrentUser user, IClock clock)
{
    public async Task<SettingsResponse> GetAsync(CancellationToken ct) => Map(await repository.SettingsAsync(user.Profile.Id, ct) ?? new ProfileSettings(user.Profile.Id));
    public Task<SettingsResponse> UpdateAsync(string? phone, string currency, bool notifications, CancellationToken ct) =>
        work.ExecuteAsync(user.Profile.Id, user.Profile.Id, async token =>
        {
            var settings = await repository.SettingsAsync(user.Profile.Id, token);
            if (settings is null) { settings = new ProfileSettings(user.Profile.Id); repository.Add(settings); }
            settings.Update(phone, currency, notifications, clock.UtcNow);
            return Map(settings);
        }, ct);
    public async Task<string?> AvatarAsync(CancellationToken ct)
    {
        var data = (await repository.SettingsAsync(user.Profile.Id, ct))?.AvatarJpeg;
        return data is null ? null : "data:image/jpeg;base64," + Convert.ToBase64String(data);
    }
    public Task<bool> SetAvatarAsync(string? base64, CancellationToken ct)
    {
        byte[]? data = null;
        if (base64 is not null)
        {
            if (base64.Length > 350000) throw new Flenta.Domain.Exceptions.DomainValidationException("profile.invalid_avatar", "The image is too large.");
            try { data = Convert.FromBase64String(base64); } catch (FormatException) { throw new Flenta.Domain.Exceptions.DomainValidationException("profile.invalid_avatar", "Invalid image encoding."); }
        }
        return work.ExecuteAsync(user.Profile.Id, user.Profile.Id, async token =>
        {
            var settings = await repository.SettingsAsync(user.Profile.Id, token);
            if (settings is null) { settings = new ProfileSettings(user.Profile.Id); repository.Add(settings); }
            settings.SetAvatar(data, clock.UtcNow); return true;
        }, ct);
    }
    private static SettingsResponse Map(ProfileSettings s) => new(s.Phone, s.DefaultCurrency, s.TransactionNotifications);
}
