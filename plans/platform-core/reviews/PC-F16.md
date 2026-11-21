# PC-F16: identity redirects that ignore the in-proc path base; passkey E2E helper race

## Implementer notes

### How the in-proc path base works

In-proc mode does not use `UsePathBase`. `TellmaIdentityOptions.PathBase` (`/id`) is applied as a
**route prefix**: `TellmaIdentityPageRouteConvention` and `TellmaIdentityControllerRouteConvention`
prepend it to every identity page and controller template. So `Request.PathBase` is empty in the
test in-proc host. A literal `/Identity/...` path skips the engine's mount point and lands on the
host's own routes. Routing-generated URLs (`Url.Page`) carry both the route prefix and any host
`Request.PathBase`. That is why every fix below uses `Url.Page` and not `Request.PathBase`.

### Sites fixed (3)

1. `Areas/Identity/Pages/Account/EmailCode.cshtml.cs`. The post-verification fallback (no valid
   `returnUrl`) was `LocalRedirect(Sanitize(ReturnUrl, "/Identity/Account/Login"))`. It is now
   `Url.Page("/Account/Login", new { area })`, the same pattern as `Login.cshtml.cs` and
   `ExternalLogin.cshtml.cs`.
2. `Controllers/EndSessionController.cs`. The sign-out `AuthenticationProperties.RedirectUri`, which
   OpenIddict redirects to when there is no `post_logout_redirect_uri`, was the literal
   `/Identity/Account/LoggedOut`. It is now `Url.Page("/Account/LoggedOut", new { area })`.
3. `Controllers/AuthorizationController.RedirectToLogin`. This was `prefix + "/Identity/Account/Login"`
   with `prefix = engineOptions.Value.PathBase`. It already reached `/id` in-proc but dropped a host
   `Request.PathBase`, which the `returnUrl` it carries already includes. It is now
   `Url.Page("/Account/Login", new { area })`. The `IOptions<TellmaIdentityOptions>` constructor
   parameter was used only for that prefix, so it is removed along with its two usings. The output
   is unchanged for every supported configuration.

### Sites left as they are (each honours the path base)

- `Hosting/IdentityConfigurator.cs`: cookie `LoginPath`, `LogoutPath`, `AccessDeniedPath` =
  `options.PathBase + "/Identity/Account/..."`. These are `PathString` options set at registration,
  with no request or URL helper available. The cookie handler prepends `OriginalPathBase` itself and
  compares `Request.Path` against `LoginPath`, so they must stay paths relative to the request path
  base. Covered by the existing `AccountEntryTests.Mounted_in_a_host_the_entry_and_its_challenge_carry_the_path_base`.
- `Services/Invitations/InvitationLinkBuilder.cs` and `Pages/Account/ForgotPassword.cshtml.cs`:
  absolute emailed links built as `new Uri(Issuer, $"{PathBase}/Identity/Account/...")`. They include
  the configured path base. They are deliberately anchored to the configured issuer and not the
  request host (a link emailed to a user must not follow a forged `Host` header). The invitation
  builder also runs outside a request. `LinkGenerator.GetPathByPage` could replace the literal page
  path. That would be a refactor, not a path-base bug, so it was left alone.
- `_ViewStart`/`partial name="/Areas/Identity/Pages/Shared/..."` entries are view-file paths, not URLs.
  `IdentityPageLinks`' `/Identity/Account|Manage` are folder matchers applied after the path base
  has been stripped.
- Out of scope, noted for later: `AuthorizeReturnFormAction.ClientIdOf` compares the return path
  with `engineOptions.PathBase + "/connect/authorize"` and ignores a host `Request.PathBase`. This
  matters only if a host mounts the engine under its own `UsePathBase` as well, which the options
  validator effectively rules out today (the InProc issuer path must equal `PathBase`).

### Tests

New `test/apps/Tellma.Identity.IntegrationTests/Flows/PathBaseRedirectTests.cs`, which runs on the
existing `InProcFactory` (the `TestInProcHost` asset with the engine at `/id`) against the shared
Testcontainers SQL fixture:

- `An_email_code_sign_in_with_nowhere_to_return_lands_on_the_sign_in_page_under_the_path_base`:
  Location is `/id/Identity/Account/Login`. Following it signed in goes to `/id/Identity/Manage`.
- `An_end_session_request_with_nowhere_to_return_lands_on_the_logged_out_page_under_the_path_base`:
  signs in, then `/id/connect/endsession` with no hint, then confirmation, then Location
  `/id/Identity/Account/LoggedOut`, which answers 200. The account pages then challenge again.
- `An_authorization_request_sends_the_browser_to_sign_in_under_the_path_base_and_completes`: the
  full in-proc PAR, authorize, email code, authorize and code flow. It covers site 3. This flow had
  no in-proc integration coverage before.

Negative control: with the three `src` changes reverted (tests unchanged), the email-code and
end-session tests fail with `Actual: "/Identity/Account/Login"` and
`Actual: "/Identity/Account/LoggedOut"`. The authorize test passes, as expected, because site 3 was
not broken in-proc.

Harness changes needed for these tests:
- `InProcFactory` now installs the same capturing `IEmailSender` and `RemoteIpStartupFilter` that
  `StandaloneFactory` does. It also exposes `PathBase` and `Emails`.
- `OidcFlowClient` gains an `InProcFactory` constructor. It holds the email sink and path base
  instead of the `StandaloneFactory`, and prefixes its PAR and token endpoints with that path base.
- `TestData.CreateActiveUserAsync` gains an `InProcFactory` overload, delegating to a private
  `IServiceProvider` one.

### Passkey E2E helper

`PasskeyCeremonies.RegisterThenSignInAsync` used to wait 5 s for the login page's conditional
ceremony and then press "Sign in with a passkey" unconditionally. Under load, a conditional ceremony
still in flight at 5 s was aborted by the press (the button dispatches `tellma:passkey-explicit`), or
it submitted while the explicit ceremony started.

The helper now decides deterministically, from the same condition the page's `login.page.js` uses:
`PublicKeyCredential.isConditionalMediationAvailable()`. When that is true, the page runs the
conditional ceremony on load, the virtual authenticator answers it with no prompt, and the test only
waits for the destination. When it is false, the test presses the button; the locator waits for the
button to be actionable. Two details came from the first E2E run:
- The question has to be asked **before** navigating to the login page, on the settled logged-out
  page. Asking on the login page itself failed both passkey tests with "Execution context was
  destroyed": the conditional ceremony had already submitted and navigated. That also confirms
  empirically that the conditional path is the one that runs in this Chromium.
- The login `GotoAsync` waits only for `Commit`, because the ceremony can move the browser on before
  `load` is observed. Nothing after it needs `load` (see the E2E README's waiting rule).

The final wait is `WaitForPathAsync` with its default 30 s budget. Before, the budget was 5 s and
then 15 s.

Not changed, same class of race: `StepUpTests.DriveCeremonyAsync` (5 s, then press) and
`SyncedPasskeyTierTests.DriveCeremonyUntilRefusedAsync` (15 s, then press). Both could reuse
`PasskeyCeremonies.OffersConditionalMediationAsync`. They were left out because the LEDGER flake row
names only this helper. Worth a follow-up if either flakes.

### Checks

- Build of `Tellma.Identity.Tests`, `Tellma.Identity.IntegrationTests` and `Tellma.Identity.E2E`
  (which build `src/apps/Tellma.Identity*`): Build succeeded, 0 warnings, 0 errors.
- `dotnet format --verify-no-changes`: clean for `Tellma.Identity`, `Tellma.Identity.IntegrationTests`
  and `Tellma.Identity.E2E`.
- Identity unit tests: 1023/1023 passed.
- Identity integration tests (`Category=Integration&Category!=Nightly&Live!=true`): 205/205 passed.
- Identity E2E (`Category=E2E&Category!=Nightly`): 39/39 passed. The first full run, with the check
  asked on the login page, failed both passkey tests as described above. After the fix, the two
  passkey tests (standalone and in-proc) also passed three repeated runs in a row (2/2 each).
- Containers: none started by hand. The Testcontainers reaper (ryuk) removes the suites' SQL
  containers when each test process exits. TELLMA_TEST_SQL was left unset.

## Review 1

### Findings

Checked and found sound (no finding): the three `Url.Page` sites, in-proc (the route convention puts
`id/` in the page template, and routing adds `Request.PathBase`) and standalone (empty prefix, so the
output is `Request.PathBase` + `/Identity/...`). The same controller-context pattern is already in
`AccountController` and `DeviceVerificationController`. Open-redirect protection is unchanged:
end-session's `RedirectUri` and the authorize-to-login target are now server-generated and never
take request input. Neither uses `X-Forwarded-Prefix`, so `PathBase` is configuration only.
EmailCode still goes through `ReturnUrlValidator.Sanitize` + `LocalRedirect`. Removing
`IOptions<TellmaIdentityOptions>` from `AuthorizationController` gives the same output in every
configuration the options validator accepts. A host `Request.PathBase` is effectively ruled out
(standalone issuer must be the bare origin, in-proc issuer path must equal `PathBase`). For a
trailing-slash `PathBase` the new code is strictly better (`/id//Identity` before). Each item on the
"left as is" list is correct: the cookie paths are request-path-base-relative, the emailed links
are deliberately issuer-anchored, the partial/layout names are file paths, and the `IdentityPageLinks`
folders are matched after both bases are stripped. A fresh sweep of `src/apps/Tellma.Identity`
(`.cs`, `.cshtml`, `.js`) found no other literal `/Identity/...` or `/connect/...` URL. The new
email-code and end-session tests assert exact `/id/...` Locations, so they fail against the old
literals. The authorize test is coverage only, as the notes say. One side effect is a gain: the
EmailCode fallback is now recognised by `IdentityPageLinks` in-proc, so `CarriedQueryRedirectFilter`
carries the request's values onto it. No spec, PC-id or `plans/` references were added.

Run by the reviewer: the Identity integration tests on Testcontainers, with TELLMA_TEST_SQL unset.
The filter was `PathBaseRedirectTests|CompositionParityTests|AccountEntryTests|InProcManagementAudienceTests|ManagementApiRouteTests`,
which covers the new tests and every other `InProcFactory` user, to check the harness change.
Result: 23/23 passed. No container was left behind; ryuk removed it.

- **R1-1 (minor). The explicit-button branch can click before the button has a handler.**
  `test/apps/Tellma.Identity.E2E/Infrastructure/PasskeyCeremonies.cs:59-66`. The login navigation
  now returns at `Commit`, and when `conditional` is false the helper clicks "Sign in with a
  passkey" at once. That button is `type="button"` (`Login.cshtml:76`). Its only behaviour is the
  click listener that `login.page.js` attaches. The script is an external, parser-blocking
  `<script>` at the end of the body (`Login.cshtml:123-125`, rendered at `_AuthLayout.cshtml:50`).
  While the parser waits on that fetch, the already-parsed button can be laid out and painted, and
  Playwright's actionability checks (visible, stable over two frames, enabled, hit-testable) can
  pass. The click is then a silent no-op, and the test fails after `WaitForPathAsync`'s 30 s. The
  old code avoided this because `GotoAsync` waited for `load`. The suite's "locators wait for their
  own element" rule does not cover this case: the element exists before its behaviour does. Today
  the branch is not taken in the suite's Chromium (conditional mediation is available), so this is
  latent. But it is exactly the path the helper offers as the deterministic fallback, and
  `StepUpTests`/`SyncedPasskeyTierTests` are expected to reuse the probe. Fix: wait for the
  document only when no ceremony can move the browser on. For example, use
  `WaitUntil = conditional ? WaitUntilState.Commit : WaitUntilState.DOMContentLoaded`.
  `DOMContentLoaded` fires only after the parser-blocking scripts have run. Update the comment at
  lines 52-57 and 64-65 to match.
- **R1-2 (minor). A renamed private field has no XML doc.**
  `test/apps/Tellma.Identity.IntegrationTests/Infrastructure/OidcFlowClient.cs:27`. AGENTS.md
  requires an XML doc comment on every C# member. The field `_factory` was replaced by `_emails`
  with no `<summary>`, while its new sibling `_pathBase` (line 29-30) got one. `_backchannel`
  (line 26), on the edited block, has none either. Fix: add one-line summaries to both. For
  example: "The host's captured outbound email, where sign-in codes are read." and "The cookie-less
  client the confidential client's back-channel calls go through."

### Responses

- **R1-1: fixed in 526c4b9.** `PasskeyCeremonies.RegisterThenSignInAsync` now navigates with
  `WaitUntil = conditional ? WaitUntilState.Commit : WaitUntilState.DOMContentLoaded`. When the
  conditional ceremony runs, it still waits only for commit, so the ceremony can navigate away freely.
  When the button will be pressed, it waits for `DOMContentLoaded`, which fires only after the
  parser-blocking `login.page.js` has attached the button's listener. The comment above the
  navigation now explains both cases. The comment in the button branch now says the button is wired,
  not that actionability is enough.
  Checked: the branch is not taken in the suite's Chromium, so I ran it on purpose. A temporary local
  edit (never committed) forced `conditional` to false and stubbed the page's
  `isConditionalMediationAvailable` to false through an init script. With it, both passkey E2E tests
  (standalone and in-proc) took the button path and passed in two runs (2/2 each). The edit was then
  reverted.
- **R1-2: fixed in 526c4b9.** `OidcFlowClient._backchannel` and `_emails` now have one-line `<summary>`
  docs, using the wording the review suggested.
