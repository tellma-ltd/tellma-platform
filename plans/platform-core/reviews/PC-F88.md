# PC-F88: Acme Windows nightly HTTPS trust

## Implementer notes

### Evidence

- No Windows Acme nightly leg had run when this item started. The latest nightly-tier run on
  platform-core, 45395656478 (workflow_dispatch, 2026-11-24T12:39Z, green), predates PC-114's
  landing (7a27e3c6, 2026-11-24 20:34Z), and its jobs are client-e2e-quarantine, dotnet,
  integration-windows, client-e2e and dotnet-windows only: neither `acme-e2e` nor
  `acme-e2e-windows` has ever run.
- So this fixes the predictable failure rather than an observed one. On a fresh windows-latest
  runner the e2e host runs `dotnet dev-certs https` without `--trust` (acme-e2e-host.mjs), so
  Kestrel serves a certificate whose root no store holds. The BFF's sign-in fetches the in-process
  identity engine's discovery document from the host's own `https://localhost` origin, and .NET's
  chain check fails with UntrustedRoot: the sign-in 500 Linux hit at PC-114's integration (D-278).

### What was built (`.github/workflows/nightly-tier.yml` only, plus docs)

- **A trust step in `acme-e2e-windows`**, "Trust the development certificate", between the publish
  and the suite. pwsh, non-interactive:
  1. `dotnet dev-certs https` (creates the certificate; the host's own later call is then a no-op);
  2. selects the valid certificates in `Cert:\CurrentUser\My` that carry ASP.NET Core's marker
     extension `1.3.6.1.4.1.311.84.1.1` and throws if there is none;
  3. adds the public part (`X509Certificate2(RawData)`, no private key) of each one whose chain does
     not already build to `LocalMachine\Root` through `X509Store`. That store takes a certificate
     from an administrator with no confirmation dialog, unlike `CurrentUser\Root`, which
     `dotnet dev-certs https --trust` uses on Windows and which Windows confirms in a dialog. The
     hosted runner's account is an administrator. The CurrentUser chain engine includes the machine
     roots, so .NET's `HttpClient` (SChannel) trusts the certificate;
  4. builds every selected certificate's chain again (`X509Chain`, revocation off: the certificate
     names no CRL or OCSP endpoint) and throws, naming the thumbprint and the chain status, when one
     still does not build. So a trust that did not take fails this step, before the suite, rather
     than surfacing as a sign-in 500 twenty minutes later.
  It is Windows-only by construction (it lives in the `windows-latest` job), and skipping
  certificates already trusted makes it safe to re-run. No product code changed and no
  certificate validation is relaxed anywhere: the host, the BFF and the browsers' settings are as
  they were.
- **Linux is byte-identical.** `acme-e2e-host.mjs` is untouched, so the Linux legs keep
  `dotnet dev-certs https --trust` (exit 0 or 4) exactly as D-278 left them, and no Linux job gained
  or lost a step.
- **The tier can run on a working branch (D-300).** Every job's guard is now
  `github.ref == 'refs/heads/platform-core' || (github.event_name == 'workflow_dispatch' && startsWith(github.ref, 'refs/heads/pc/'))`,
  and the concurrency group is per ref (`nightly-tier-${{ github.ref }}`), so a pc dispatch never
  shares platform-core's group, where GitHub's one-pending-run-per-group rule could cancel a queued
  nightly. A pc branch runs the tier only on a manual dispatch; the file has no schedule or push
  trigger, and the platform-core trampoline is unchanged. The file's header, CONTRIBUTING.md's
  nightly section and ARCHITECTURE.md's CI paragraph say so.
- **Docs.** The Acme E2E README and CONTRIBUTING.md's Acme browser-suite paragraph now say that the
  host trusts the certificate on Linux, a Windows developer trusts it once with
  `dotnet dev-certs https --trust`, and the nightly Windows leg adds it to `LocalMachine\Root` itself.

### Proof

- **The trust step itself is proven only in CI.** Running it locally was refused by the permission
  check (D-300), and this machine already trusts both of its development certificates
  (`dotnet dev-certs https --check --trust` exits 0), so a local run could not have exercised the
  add path anyway.
- Static check: the workflow parses (js-yaml 4.3.0); every job carries the new guard; the step's
  shell is pwsh; it runs after the publish and before the suite. By reading: it has no prompt
  (`X509Store.Add` on LocalMachine never shows UI; only CurrentUser\Root does), and it throws
  whenever no certificate exists or a chain still fails to build.
- Local Windows path, as the CI leg runs it: `dotnet publish` of the Acme web host in Release, then
  `TELLMA_E2E_SQL=localdb TELLMA_E2E_CONFIGURATION=Release TELLMA_E2E_WEBROOT=<published wwwroot>
  node distributions/acme/test/Tellma.Distro.Acme.E2E/scripts/e2e.mjs --grep-invert "@quarantine"`
  (without `CI`, so no retry and no perf multiplier). LocalDB migrated `Tellma.e2e.pc3.acme.*`, the
  web host came up on HTTPS, and every sign-in went through: 85 passed, 46 skipped (fixme flows),
  3 failed in 5.2 min. The three are `@perf` gates, not sign-in or trust:
  - nightly.spec.ts "a warm launch shows its chrome within 300 ms with the CPU slowed four times":
    median 1142.6 ms against 300;
  - route-feedback.spec.ts "a link click shows the route skeleton in the first frame after it":
    frame 3 (chromium) and 2 (chromium-ar) against 1.
  This laptop was under heavy load from the other slots (Bash fork failures during the run), and
  these are CPU-bound timing gates, so this run does not judge them. The CI dispatch below does.
- CI: commit 1 is dispatched with `gh workflow run nightly-tier.yml --ref pc/PC-F88`. Its run id and
  the acme-e2e-windows and acme-e2e results go in the return summary and in the Review 1 responses.
- Gates: `dotnet build Tellma.slnx` (Build succeeded, 0 warnings) and
  `dotnet format Tellma.slnx --verify-no-changes` (clean). No .NET or TypeScript source changed, so
  no unit or integration suite is touched.

### Open risks

- If a future windows-latest image ran steps without administrator rights, step 3 would fail with
  "access denied", loudly and before the suite, not silently.
- `X509Chain.Build` is the same chain engine SChannel uses, but the step proves trust, not the
  HTTPS call itself; the suite's sign-ins are that proof.

### What reviewers should examine hardest

- Whether `LocalMachine\Root` really is trusted by the web host's backchannel `HttpClient` on
  Windows, and whether the step could ever block on UI.
- The widened guards: that a pc branch can never reach this tier by schedule or push, and that the
  platform-core nightly is otherwise unchanged.

## Review 1

Reviewed commit d07d94e2 on the pc-3 slot, with CI run 45596696902 (workflow_dispatch on
pc/PC-F88, head d07d94e2). The scope is LEDGER.md's PC-F88 row; CHECKPOINTS.md has no entry for
fix items. Summary: 0 blocking, 2 major, 2 minor. One of the two majors (R1-2) is outside this
diff: it is the product gap D-306 opens as PC-F100, and is recorded here with its root cause
checked against the code.

What I checked and found sound:

- **(a) The trust step** (`.github/workflows/nightly-tier.yml:250-303`).
  - It cannot prompt. `dotnet dev-certs https` runs without `--trust`, and `X509Store.Add` on
    `LocalMachine\Root` never shows UI. Only `CurrentUser\Root` does.
  - It is Windows-only. It lives in `acme-e2e-windows` (`runs-on: windows-latest`), and no Linux
    job or `acme-e2e-host.mjs` changed. The host still runs `--trust` on Linux and only creates the
    certificate on Windows (`acme-e2e-host.mjs:127-133`).
  - It adds the public part only: `X509Certificate2(RawData)`. It relaxes no validation: no
    callback, no environment switch, and the host and browsers are as they were. The step's own
    `RevocationMode = 'NoCheck'` applies only to its own check chain, and it matches what the
    host's `HttpClient` does by default (no revocation check).
  - It fails the step when a chain still does not build. It throws on a non-zero `dev-certs` exit,
    on an empty selection, and on any certificate still untrusted after the add.
  - It is idempotent on one runner. A valid certificate makes `dev-certs` a no-op, a trusted one is
    skipped, and `X509Store.Add` of a certificate already present is a no-op too.
  - Kestrel serves the certificate from `CurrentUser\My` (the host passes no certificate path), so
    the certificate trusted is the one served.
  - The CurrentUser chain engine, which SslStream uses, includes the machine roots.
  - CI 45596696902: step 10 is green ("The development certificate ADC58EF3… is trusted."). On
    Windows every sign-in flow passed: 86 passed, 46 skipped (fixme), and 2 failed, both on the one
    @perf gate.
- **(b) The guards.**
  - All seven jobs carry the same guard:
    `github.ref == 'refs/heads/platform-core' || (github.event_name == 'workflow_dispatch' && startsWith(github.ref, 'refs/heads/pc/'))`.
  - No job has `needs:`, so no chain can run on the wrong ref.
  - The file's only trigger is `workflow_dispatch`. The schedule lives in main's trampoline
    (origin/main `nightly-tier.yml`: cron, guarded to main, dispatching `--ref platform-core`),
    which is unchanged and has no concurrency group. So schedules still reach only platform-core,
    and nothing runs on a push.
  - The per-ref group `nightly-tier-${{ github.ref }}` keeps a pc dispatch out of platform-core's
    group. Its prefix does not collide with ci.yml's groups.
- **(d) The docs.**
  - CONTRIBUTING.md (Acme paragraph and Nightly tier), ARCHITECTURE.md's CI paragraph, the Acme E2E
    README and the workflow header all describe pc runs as dispatch-only, never on a schedule or
    push, in a group per branch. That is accurate.
  - None of them references `docs/` in a place AGENTS.md forbids. The workflow's "spec 0024 §14" is
    an inline comment.
  - `git diff --check` is clean.
- **Tests.** Workflow and docs only, so PLAN §7.1 item 4 touches no suite. Running the step locally
  was refused (D-300), and CI is the proof, with the gap in R1-3. The local Windows suite run and
  the build and format gates are listed in the commit message.

### Findings

**R1-1 (major): the Acme report uploads never upload anything, so a red Acme nightly leg leaves no
report or trace.**

- Where: `.github/workflows/nightly-tier.yml:196-202` (acme-e2e) and `:310-316`
  (acme-e2e-windows), and the same step in `.github/workflows/ci.yml:391-397`, which predates this
  commit.
- What happens: `actions/upload-artifact@v4` skips hidden files and folders by default
  (`include-hidden-files: false`, as the step's own log shows). The path given is the hidden
  folder `.artifacts` itself, so everything under it is skipped. Both legs of 45596696902 logged
  "No files were found with the provided path: distributions/acme/test/Tellma.Distro.Acme.E2E/.artifacts.
  No artifacts will be uploaded."
- Effect: the error-context and `trace.zip` files that Playwright names in the log are lost, on
  exactly the legs this checkpoint makes runnable. The `client/.artifacts/e2e` uploads are not
  affected, because their path's last segment is not hidden.
- Fix (D-306 folds this into fix1): add `include-hidden-files: true` to the three Acme upload steps.

**R1-2 (major, outside this diff; root cause of the @perf gate, PC-F100): `TmUrls.preload` never
warms the tenant shell, so the first tenant entry from the picker misses the click's frame.**

- Where: `client/projects/core/tellma-core/src/urls/tm-urls.ts:578-604` (`#loadersOf`), together
  with `src/routes/tm-route-hosts.ts:73-79` (`tmShellRoute`) and `src/routes/tm-routes.ts:250`.
- I confirm the implementer's root cause from the code.
  - The tenant route's `loadComponent` is `tmShellRoute('TmTenantShell')`. It returns the component
    synchronously only once `loaded.TmTenantShell` is set, and only that name's own loader sets it
    (`tmShellComponent`, line 62).
  - On `/tenants` the shell module is already in memory, because the picker came through
    `ɵTM_SHELL.TmTenantPicker`. But nothing has called the `TmTenantShell` loader.
  - The picker's hover and focus preload (`tm-tenant-picker.ts:197-198, 668-669`) goes through
    `#loadersOf`. For a `home` match that returns only `[TM_ROUTE_HOSTS.home]` (the placeholder),
    and for any tenant match it returns nothing that warms the tenant shell.
  - So the click's navigation waits on `import('@tellma/core/shell')` and its `.then` hops. The
    tenant shell, and the `TmRouteHost` with its `tm-page-skeleton` inside it, activates a task
    later. That gives frame 2 on CI on both OSes, and 2 or 3 locally.
  - The guard on that route, `tmSessionGuard`, is synchronous, so the shell load is the only
    asynchronous step on this path. A second entry hits `loaded` and would pass.
- Failure scenario: every first entry into a tenant from the picker paints a frame without the
  skeleton, which breaks spec 0024 §14's "Instant feedback" rule.
  - The PR tier cannot see it. `route-feedback.spec.ts:81` only waits for the skeleton, and the
    frame gate (`:99`) is @perf, so it runs nightly.
  - The `TmUrls` unit tests (`tm-urls.spec.ts:188-195`) cover entity routes only.
- Suggested fix, for PC-F100:
  1. Export one shared `tmShellComponent('TmTenantShell')` loader from `tm-route-hosts.ts`.
  2. Make `#loadersOf` prepend it for every match with a `tenantId`, so `#preloaded` dedupes it.
  3. Add a unit test: after `preload('/17')` and its idle callback, `tmShellRoute('TmTenantShell')()`
     returns a `Type`, not a promise.
- Check this with the gate itself. The preload runs in `requestIdleCallback` after `mouseenter`.
  Playwright's `click()` sends mousedown a few milliseconds after it hovers, so the idle callback
  may not have run before the click. If the gate still shows frame 2, also warm the tenant shell
  when the picker renders, since it is in the same `/shell` chunk and costs nothing to resolve.

**R1-3 (minor): the step's log cannot show whether the `LocalMachine\Root` add ran, so CI proves
the outcome but not the add path.**

- Where: `.github/workflows/nightly-tier.yml:282-303`.
- What happens: in 45596696902, `dotnet dev-certs https` printed "A valid HTTPS certificate is
  already present." So the certificate existed before the step: the SDK's first run during
  restore or publish created it, or the image did. The step then printed only "… is trusted."
  It writes nothing on the add path. So the run cannot tell "added to `LocalMachine\Root`, then
  trusted" from "already trusted on this image".
- Why it matters: the implementer notes say "the trust step itself is proven only in CI". If the
  image already trusts its certificate, the add path, and the claim that the runner account can
  write the machine store without a prompt, were never exercised. The Windows leg would also have
  passed without the step, which changes what PC-F88 fixed.
- Fix: write one line per certificate on each branch, for example
  `Write-Output "Adding $($certificate.Thumbprint) to LocalMachine\Root."` inside the add loop,
  and an "already trusted" line for the skipped ones. Then state in the notes, from the next
  dispatch's log, which path ran.

**R1-4 (minor): the selection trusts any marker-OID certificate in `CurrentUser\My`, which is
broader than the certificate Kestrel serves.**

- Where: `.github/workflows/nightly-tier.yml:259-262`.
- What happens: the filter is the marker OID plus the validity dates. Anything in
  `CurrentUser\My` that carries `1.3.6.1.4.1.311.84.1.1` becomes a machine-wide trust anchor,
  whatever its subject or issuer. That includes a certificate that is not self-signed, which
  would then make its issuer's chain moot.
- Scope: on an ephemeral hosted runner only `dotnet dev-certs` writes such certificates, and the
  account is already an administrator. So this is hardening, not an exploit. But the step's
  comment says it takes "the valid development certificates", and the filter does not hold it to
  that.
- Fix: add `$_.Subject -eq 'CN=localhost' -and $_.Issuer -eq $_.Subject -and $_.HasPrivateKey`
  to the `Where-Object`.

### Responses

R1-1: fixed in fix 1.
- `include-hidden-files: true` is on the three Acme report uploads: nightly-tier.yml's acme-e2e and
  acme-e2e-windows, and ci.yml's acme-client. Each has a comment saying why. The
  `client/.artifacts/e2e` uploads are left alone (D-307).

R1-2: PC-F100 (D-306).

R1-3: fixed in fix 1.
- Before any add, the step writes one line per selected certificate: "<thumbprint> already
  trusted." or "Adding <thumbprint> to LocalMachine\Root." The final check still writes
  "The development certificate <thumbprint> is trusted." for each one, or throws.
- What the next dispatch's log says is recorded under "Proof after fix 1" below.

R1-4: fixed in fix 1.
- The selection now also requires `Subject -eq 'CN=localhost'`, `Issuer -eq Subject` (self-signed)
  and `HasPrivateKey`. If nothing qualifies, the step throws.

#### Proof after fix 1

Fix 1 (f94aeabe) was dispatched with `gh workflow run nightly-tier.yml --ref pc/PC-F88`, as run
45604475795.
- **The add path ran on the Windows runner.** The step logged, in order:
  - "A valid HTTPS certificate is already present.": the SDK's first run, during restore or
    publish, had already created the certificate;
  - "Adding 5E92BD4C4C555D68CD8A1B47678B883FCEA600E1 to LocalMachine\Root.";
  - "The development certificate 5E92BD4C… is trusted."
  So the image does not trust its development certificate. The step did the trusting, without a
  prompt, and the runner account could write the machine store. The step is the fix, not only a
  safeguard. Without it, the Windows leg's sign-ins would hit the UntrustedRoot 500 (D-278).
- **acme-e2e-windows: red only on the PC-F100 gate.** 86 passed, 46 skipped (fixme), 2 failed.
  Both failures are route-feedback.spec.ts:99, the skeleton-frame @perf gate, in chromium and
  chromium-ar. Every sign-in flow passed.
- **acme-e2e (Linux): red only on the same gate.** 3 passed, 2 failed.
- **The reports upload now (R1-1).** The run's artifacts include nightly-acme-e2e-windows-report
  (39 files, 4.6 MB) and nightly-acme-e2e-report, beside nightly-quarantine-e2e-report.
- The other five jobs are green. The cold launch's largest paint warned at 8740 ms on Windows,
  against its 4000 ms budget. That gate is a warning by design.
