# Tellma.Identity.E2E

Browser end-to-end tests (`[Trait("Category", "E2E")]`) for the identity server UI, driven by
Playwright (Chromium):

- The real `Tellma.Identity.Web` host runs in-process on Kestrel at an ephemeral port, against a
  Testcontainers SQL Server; emails are captured in-process for code/link scraping.
- Passkey ceremonies use the CDP **virtual authenticator** (`WebAuthn.addVirtualAuthenticator`,
  ctap2/internal, resident keys, user verification). The browser's conditional-mediation account
  chooser cannot be driven deterministically over CDP, so conditional-UI coverage asserts the wiring
  (autofill attribute + options request), not the chooser UX — verify that path manually.
- Scenarios: passkey register/sign-in, email code, invitation acceptance, consent (including that
  the browser follows the grant's redirect, which the integration suite cannot see), the
  device-bound tier refusal, logout, and branding (the token stylesheet loads and resolves).
- `AccessibilityTests` runs the axe rule set over every page a user can reach, at three viewport
  widths and in Arabic, with the content-security policy left in force and its refusals asserted
  alongside. It also asserts the three things axe cannot: reflow at 320px, the `dir` attribute,
  and the size of inline row actions.
- Not covered here: the external-login failure page, which needs a stubbed upstream provider, and
  TOTP enrolment past its entry screen. Both are integration-tested instead; the accessibility
  suite scans the states they reach by URL.
- Sign-in codes are rate-limited to 10 per IP per hour, and every test here reaches the server
  from the same loopback address, so the whole run shares one budget. The accessibility suite
  therefore signs in once and hands the session to every test that needs one
  (`IdentityServerFixtureBase.SignedInStorageStateAsync`); a suite that signs in per test would
  exhaust the budget partway through and then fail with "No sign-in code was captured". Reuse the
  shared session for anything new unless the test is about signing in.

First run requires Playwright browsers: `pwsh bin/Debug/net10.0/playwright.ps1 install chromium`.
