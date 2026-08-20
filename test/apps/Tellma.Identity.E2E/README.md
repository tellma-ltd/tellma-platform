# Tellma.Identity.E2E

Browser end-to-end tests (`[Trait("Category", "E2E")]`) for the identity server UI, driven by
Playwright (Chromium):

- The real `Tellma.Identity.Web` host runs in-process on Kestrel at an ephemeral port, against a
  Testcontainers SQL Server; emails are captured in-process for code/link scraping.
- Passkey ceremonies use the CDP **virtual authenticator** (`WebAuthn.addVirtualAuthenticator`,
  ctap2/internal, resident keys, user verification). The browser's conditional-mediation account
  chooser cannot be driven deterministically over CDP, so conditional-UI coverage asserts the wiring
  (autofill attribute + options request), not the chooser UX — verify that path manually.
- Scenarios: passkey register/sign-in, email code, invitation acceptance, consent and step-up
  (each including that the browser follows the redirect back to the client, which the integration
  suite cannot see), the device-bound tier refusal, logout, and branding (the token stylesheet
  loads and resolves).
- The redirect assertions above are the point of several of these. A browser applies the
  content-security policy's `form-action` directive to every hop of the navigation a form
  submission produces, and refuses the last one silently: the server issues a perfectly good
  redirect, nothing is logged, and the page simply does not move. Only a browser sees that, so a
  flow that ends at a client callback belongs here even when the server side is covered elsewhere.
- `AccessibilityTests` runs the axe rule set over every page a user can reach, at three viewport
  widths and in Arabic, with the content-security policy left in force and its refusals asserted
  alongside. It also asserts the three things axe cannot: reflow at 320px, the `dir` attribute,
  and the size of inline row actions.
- Not covered here: the external-login failure page, which needs a stubbed upstream provider, and
  TOTP enrolment past its entry screen. Both are integration-tested instead; the accessibility
  suite scans the states they reach by URL.
- Wait for a destination with `WaitForPathAsync`/`ReachedPathAsync`, never Playwright's
  `WaitForURLAsync`. The latter waits for the `load` lifecycle event whenever the address already
  matches, and that event is reported once per document, so a click that happened to return after
  its navigation committed leaves nothing left to observe and the call hangs for its whole timeout.
  Which behaviour you get turns on a few milliseconds, so it passes until something unrelated
  changes the pace of the flow.
- Sign-in codes are rate-limited to 10 per IP per hour, and every test here reaches the server
  from the same loopback address, so the whole run shares one budget. The accessibility suite
  therefore signs in once and hands the session to every test that needs one
  (`IdentityServerFixtureBase.SignedInStorageStateAsync`); a suite that signs in per test would
  exhaust the budget partway through and then time out waiting for a code that the server declined
  to issue. Reuse the shared session for anything new unless the test is about signing in.

First run requires Playwright browsers: `pwsh bin/Debug/net10.0/playwright.ps1 install chromium`.
