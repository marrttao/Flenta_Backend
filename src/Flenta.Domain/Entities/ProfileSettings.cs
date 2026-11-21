using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;
namespace Flenta.Domain.Entities;

public sealed class ProfileSettings : Entity
{
    private ProfileSettings() : base(Guid.NewGuid()) { }
    public ProfileSettings(Guid owner) : base(owner) { if (owner == Guid.Empty) throw new ArgumentException("Owner is required."); }
    public byte[]? AvatarJpeg { get; private set; }
    public void SetAvatar(byte[]? data, DateTimeOffset now)
    {
        if (data is not null)
        {
            if (data.Length is < 20 or > 262144 || data[0] != 255 || data[1] != 216 || data[^2] != 255 || data[^1] != 217)
                throw new DomainValidationException("profile.invalid_avatar", "Use a JPEG image up to 256 KiB.");
            var dimensions = false;
            for (var offset = 2; offset + 8 < data.Length;)
            {
                if (data[offset] != 255) break;
                var marker = data[offset + 1];
                if (marker == 218 || marker == 217) break;
                var length = (data[offset + 2] << 8) | data[offset + 3];
                if (length < 2 || offset + 2 + length > data.Length) break;
                if (marker is 192 or 193 or 194)
                {
                    var height = (data[offset + 5] << 8) | data[offset + 6]; var width = (data[offset + 7] << 8) | data[offset + 8];
                    dimensions = width is > 0 and <= 1024 && height is > 0 and <= 1024; break;
                }
                offset += length + 2;
            }
            if (!dimensions) throw new DomainValidationException("profile.invalid_avatar", "Use a JPEG image no larger than 1024 × 1024.");
        }
        AvatarJpeg = data?.ToArray(); UpdatedAt = now;
    }
    public string Phone { get; private set; } = "";
    public string DefaultCurrency { get; private set; } = "UAH";
    public bool TransactionNotifications { get; private set; } = true;
    public DateTimeOffset UpdatedAt { get; private set; }
    public void Update(string? phone, string currency, bool notifications, DateTimeOffset now)
    {
        var value = (phone ?? "").Trim();
        if (value.Length > 0 && (value.Length is < 8 or > 16 || value[0] != '+' || value.Skip(1).Any(c => !char.IsAsciiDigit(c))))
            throw new DomainValidationException("profile.invalid_phone", "Use an international phone number beginning with +.");
        Phone = value; DefaultCurrency = Currency.Parse(currency); TransactionNotifications = notifications; UpdatedAt = now;
    }
}
