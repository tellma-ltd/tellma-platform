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
