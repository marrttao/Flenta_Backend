using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Flenta.Application.Abstractions;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Flenta.Api.Configuration;

internal sealed class SupabaseAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IUserProfileRepository profiles) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization")) return AuthenticateResult.NoResult();
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter) || header.Parameter.Length > 16384)
            return AuthenticateResult.Fail("A bearer token is required.");

        try
        {
            // Supabase validates the token; no claims are trusted by decoding an unsigned JWT.
            var profile = await profiles.GetCurrentAsync(header.Parameter, Context.RequestAborted);
            Context.Items[typeof(UserProfile)] = profile;
            Context.Items[typeof(HttpCurrentUser)] = header.Parameter;
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, profile.Id.ToString()),
                new Claim(ClaimTypes.Email, profile.Email.Value),
                new Claim(ClaimTypes.Name, profile.DisplayName)
            ], Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (ApplicationFailureException exception) when (exception.Kind is FailureKind.Unauthorized or FailureKind.Forbidden)
        {
            return AuthenticateResult.Fail("Session is invalid.");
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication is required.",
            extensions: new Dictionary<string, object?> { ["code"] = "auth.unauthorized" }).ExecuteAsync(Context);
    }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public UserProfile Profile => accessor.HttpContext?.Items[typeof(UserProfile)] as UserProfile
        ?? throw new ApplicationFailureException(FailureKind.Unauthorized, "auth.unauthorized", "Authentication is required.");

    public string AccessToken => accessor.HttpContext?.Items[typeof(HttpCurrentUser)] as string
        ?? throw new ApplicationFailureException(FailureKind.Unauthorized, "auth.unauthorized", "Authentication is required.");
}
