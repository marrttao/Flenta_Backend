# Desktop API
The desktop uses the existing Domain → Application → Infrastructure/API boundaries. Features contain application services; storage/provider interfaces stay in Application. New repositories extend the private banking/kyc schemas. Existing account, transfer, savings, exchange and Didit history is retained.

## Configuration
Use the existing Supabase, Banking and Didit options. The client directory requires the server-only Admin__SupabaseSecretKey (sb_secret_... or legacy service_role). It is never returned to the browser. Roles come only from freshly fetched Supabase app_metadata.role=admin and a confirmed email. The application does not assign roles, change client emails/passwords, or overwrite legal names in Auth metadata.

Banking__CardEncryptionKey must remain stable: it protects virtual card secrets and authenticated document-identity snapshots with distinct associated data. Back it up securely; changing it without a planned re-encryption makes old ciphertext unreadable. Supabase__PublishableKey remains the ordinary Auth API key. dotnet does not automatically read .env; inject variables through the launcher/runtime. No new secret belongs in source control.

Banking remains a sandbox. Card transfers route only to issued sandbox cards; IBAN transfers route only inside this sandbox. Mobile/loan/cash operations post virtual ledger movements and make no external telco, lender or issuer request. Administrative notifications are in-app inbox messages; no external push, SMS or email delivery is claimed.

## Customer endpoints
- /api/settings GET/PUT: international contact phone, supported default currency and transaction-notification preference. A phone is contact data, not a verified login factor.
- /api/settings/avatar GET/PUT: private JPEG up to 256 KiB, maximum 1024×1024. The web client resizes and re-encodes uploads.
- /api/recipients GET/POST and /{id} DELETE: owner-scoped saved IBAN recipients (100 max).
- /api/cards/{id}/settings PUT: online, contactless, cash toggles and monthly limit. Purchase/withdrawal checks hold the card row lock. Counters use UTC calendar months and gross principal, without optional savings or cashback. Legacy purchases did not identify their card; the migration conservatively counts an account’s current-month purchases against every existing card. New operations update the selected card.
- /api/payments POST and /card-transfers POST: virtual mobile/loan payments and keyed-fingerprint card routing. Full card numbers are not persisted as transfer metadata.
- /api/statements GET?from=YYYY-MM-DD&through=YYYY-MM-DD: inclusive UTC dates; at most 367 days and 10,000 rows, with an explicit error instead of truncation.
- /api/support/requests GET/POST, /{id} GET, /{id}/messages POST, /{id}/close POST: private conversations and callback requests. Available during onboarding so unverified clients can contact support. Staff answers use /api/admin/support. Messages are immutable.
- /api/notifications GET and /{id}/read POST: last 100 messages with owner-bound read receipts.
- /api/profile/identity GET: legal identity from the current verification’s encrypted snapshot, after KYC access checks. Editable display names remain separate.

Money-changing requests require Idempotency-Key (nonempty UUID). Lost-response retries keep the same key and body; changing the request requires a new key. All postings remain balanced per currency. Completed transaction and entry history is immutable.

## Authenticator and sessions
/api/auth/mfa lists TOTP factors. POST /enroll returns a temporary setup secret. POST /verify accepts factorId/code and returns a replacement session; the frontend must accept the new access/refresh tokens. DELETE /{factorId} unenrolls a factor. Verified-factor accounts are blocked from protected operations until their Supabase-validated token has AAL2. Verification/factor removal cannot bypass this gate.

POST /api/auth/sessions/sign-out-others revokes other refresh sessions. Supabase access tokens can remain valid until expiry; this is stated in the UI. Existing password confirmation uses a short-lived separate login which is signed out locally.

## Administration
Read routes: /api/admin/access, /overview, /clients, /clients/{id}, /transactions, /kyc and /kyc/{id}/evidence. Clients retain email search and pagination. Transaction search/filtering uses client, UUID, status, kind and UTC date. KYC search supports display name/email/UUID and an exact RNOKPP using a keyed fingerprint; identity fields are encrypted at rest.

Privileged POST routes:
- /clients/{id}/controls: block outgoing account operations/new accounts and freeze cards. Client unfreeze cannot remove an administrative freeze.
- /clients/{id}/adjustments: credit/debit through a new ledger transaction. No direct balance rewrite.
- /transactions/{id}/actions: immutable review flag or compensation. Compensation reverses every posting in a new transaction, requires sufficient balances in all affected accounts and is allowed once per original transaction.
- /kyc/{id}/reviews: separate manual decision for the latest submitted attempt. Approval rechecks provider/session/owner/workflow/environment and requires document evidence plus the mandatory ID/biometric checks. The reviewer cannot approve their own identity. A later attempt or provider status update invalidates the old override.
- /notifications: target a client or at most 5,000 directory clients, with an audited inbox insertion.

All require current admin access, password confirmation, a 5–1000 character reason, and Idempotency-Key. Financial changes, the audit record and generated transaction notifications commit atomically. Audit and manual-review rows cannot be updated/deleted.

Identity snapshots contain authenticated ciphertext only (plus keyed digests for deduplication/search). Document media remains with Didit: evidence retrieval returns temporary provider links, never persisted links or document copies. Provider status/history is preserved. Configure both status.updated and data.updated webhooks to capture status and extracted-data updates. Older verifications can populate identity snapshots on status refresh.

## Migrations and checks
Five additive migrations are prepared: DesktopWorkflows, DesktopStatementsAndProfile, DesktopDataConstraints, EncryptedIdentitySnapshots and KycReviewSearch. Private tables enable RLS and revoke browser roles. Notification receipts have a composite foreign key tying the message to its owner. Rollbacks that would discard identity/audit/user content are guarded.

Review the SQL on an isolated database before applying anything to an existing environment. Working/production database migrations require the owner’s separate approval. The normal API startup never runs Database.Migrate.

Use SDK 10.0.402:

    dotnet restore Flenta.Backend.sln --locked-mode
    dotnet build Flenta.Backend.sln --no-restore -c Release
    dotnet test Flenta.Backend.sln --no-restore -c Release

For PostgreSQL integration tests, set BANKING_TEST_CONNECTION to a disposable server where the test user can create/drop databases. Each fixture creates flenta_test_<random> and drops it afterwards. Tests cover real migration/application/ledger behavior without modifying the application database. Without that variable, PostgreSQL tests explicitly skip.
