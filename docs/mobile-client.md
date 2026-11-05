# Mobile authentication callbacks

`POST /api/auth/register` and `POST /api/auth/recover` accept an optional `client`:

- `desktop` (default): the existing `Supabase:FrontendUrl` and web callback paths.
- `mobile`: `Supabase:MobileFrontendUrl` and the same callback paths.

Other client values are rejected before contacting the identity provider. The
caller cannot supply an arbitrary redirect URL. The application layer passes a
typed `AuthClient` to the identity-provider adapter without depending on URLs.
Login, refresh, PKCE exchange, logout, password change and banking contracts are
unchanged; older Desktop clients need no updates.

To enable installed mobile builds, configure:

```text
Supabase__MobileFrontendUrl=flenta-mobile://app
```

The configuration validator accepts only this registered mobile origin. Empty is
the default, and mobile email requests then return 503 with
`auth.mobile_not_configured`; existing Desktop behavior is unchanged.

Add the two callback URLs to the existing Supabase Auth redirect allowlist:

```text
flenta-mobile://app/auth/confirm-email
flenta-mobile://app/auth/reset-password
```

Keep existing Desktop URLs. The installed app registers `flenta-mobile` and
exchanges the returned code via `/api/auth/exchange` using its locally stored
PKCE verifier. Expo Go cannot own the app's custom URL scheme. No hosted settings
or database migrations are applied by this code change.

For a physical device, expose the API through the team's trusted HTTPS endpoint.
LAN development also requires an appropriate listening interface and AllowedHosts.
Browser preview needs an explicit Cors:AllowedOrigins entry for its actual origin;
native requests do not use browser CORS. Do not put backend secrets in Expo public
environment variables.

Verification: the API test suite covers both email flows for desktop/mobile,
omitted-client compatibility, unknown-client rejection, unconfigured mobile
callbacks and strict mobile-origin validation. Run the existing locked restore,
Release build and test commands from the repository root. End-to-end email and
camera behavior must also be checked in an installed app with the configured
Supabase/Didit environment.
