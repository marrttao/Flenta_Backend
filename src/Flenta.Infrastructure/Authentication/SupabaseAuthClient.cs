using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;
using Flenta.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Flenta.Infrastructure.Authentication;

internal sealed class SupabaseAuthClient(HttpClient client, IOptions<SupabaseOptions> options)
{
    public string RedirectTo(string path) => options.Value.FrontendUrl.TrimEnd('/') + path;

    public async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, string? token, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            throw new ApplicationFailureException(FailureKind.Unavailable, "auth.not_configured", "Authentication service is unavailable. Try again later.");
        }

        using var request = new HttpRequestMessage(method, settings.Url.TrimEnd('/') + "/auth/v1/" + path);
        request.Headers.Add("apikey", settings.PublishableKey);
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = string.Empty;
                try
                {
                    using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                    if (error.RootElement.TryGetProperty("error_code", out var value)) code = value.GetString() ?? string.Empty;
                }
                catch (JsonException) { }
                throw MapError(response.StatusCode, code);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content)) return default;
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }
        catch (HttpRequestException)
        {
            throw Unavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable();
        }
        catch (JsonException)
        {
            throw Unavailable();
        }
    }

    public static UserProfile ReadUser(JsonElement user)
    {
        if (!user.TryGetProperty("id", out var idElement) || !Guid.TryParse(idElement.GetString(), out var id) || id == Guid.Empty
            || !user.TryGetProperty("email", out var emailElement) || string.IsNullOrEmpty(emailElement.GetString())) throw Unavailable();
        var email = emailElement.GetString()!;
        var displayName = email.Split('@')[0];
        if (user.TryGetProperty("user_metadata", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("display_name", out var name) && name.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(name.GetString())) displayName = name.GetString()!;
        var confirmed = user.TryGetProperty("email_confirmed_at", out var confirmedAt) && confirmedAt.ValueKind == JsonValueKind.String;
        return new UserProfile(id, EmailAddress.Create(email), displayName, confirmed);
    }

    public static AuthSession ReadSession(JsonElement data)
    {
        if (!data.TryGetProperty("access_token", out var access) || string.IsNullOrWhiteSpace(access.GetString())
            || !data.TryGetProperty("refresh_token", out var refresh) || string.IsNullOrWhiteSpace(refresh.GetString())
            || !data.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetInt64(out var expiresIn) || expiresIn <= 0
            || !data.TryGetProperty("user", out var user)) throw Unavailable();
        var profile = ReadUser(user);
        return new AuthSession(access.GetString()!, refresh.GetString()!, expiresIn,
            new ProfileResponse(profile.Id, profile.Email.Value, profile.DisplayName, profile.EmailConfirmed));
    }

    private static ApplicationFailureException MapError(HttpStatusCode status, string code) => code switch
    {
        "email_not_confirmed" => new(FailureKind.Forbidden, "auth.email_not_confirmed", "Confirm your email before signing in."),
        "invalid_credentials" or "refresh_token_not_found" or "refresh_token_already_used" or "session_not_found" or "bad_jwt" =>
            new(FailureKind.Unauthorized, "auth.invalid_credentials", "Credentials or session are invalid. Sign in again."),
        "user_already_exists" or "email_exists" => new(FailureKind.Conflict, "auth.registration_failed", "Registration could not be completed."),
        "reauthentication_needed" or "reauthentication_not_valid" => new(FailureKind.Forbidden, "auth.reauthentication_required", "Sign in again before changing your password."),
        _ when (int)status >= 500 => Unavailable(),
        _ when status == HttpStatusCode.TooManyRequests => new(FailureKind.RateLimited, "auth.rate_limited", "Too many attempts. Try again later."),
        _ when status == HttpStatusCode.Unauthorized => new(FailureKind.Unauthorized, "auth.invalid_session", "Session is invalid. Sign in again."),
        _ when status == HttpStatusCode.Forbidden => new(FailureKind.Forbidden, "auth.forbidden", "This action is not allowed."),
        _ => new(FailureKind.InvalidRequest, "auth.request_rejected", "The authentication request was rejected. Check the input or request a new email link.")
    };

    private static ApplicationFailureException Unavailable() => new(FailureKind.Unavailable, "auth.provider_unavailable", "Authentication service is unavailable. Try again later.");
}
