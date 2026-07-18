# Tellma.Identity.E2E

Browser end-to-end tests (`[Trait("Category", "E2E")]`) for the identity server UI, driven by
Playwright (Chromium):

- The real `Tellma.Identity.Web` host runs in-process on Kestrel at an ephemeral port, against a
  Testcontainers SQL Server; emails are captured in-process for code/link scraping.
- Passkey ceremonies use the CDP **virtual authenticator** (`WebAuthn.addVirtualAuthenticator`,
  ctap2/internal, resident keys, user verification). The browser's conditional-mediation account
  chooser cannot be driven deterministically over CDP, so conditional-UI coverage asserts the wiring
  (autofill attribute + options request), not the chooser UX — verify that path manually.
- Scenarios: passkey register/sign-in, email code, TOTP, external login (stubbed provider),
  recovery (passkey loss and temporary access pass), invitation, consent, logout, branding (the
  token stylesheet loads and resolves) and RTL rendering.

First run requires Playwright browsers: `pwsh bin/Debug/net10.0/playwright.ps1 install chromium`.
