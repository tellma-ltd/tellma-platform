# Playwright browsers

`install-browsers.sh` installs the Playwright browsers a CI job needs and, on Linux, the OS packages
they depend on, so that an outage of the Ubuntu apt mirrors does not by itself fail the job. It
replaces `playwright install --with-deps`, which fails the whole step the moment apt cannot reach a
mirror, even when the runner image already carries every library the browsers link against and
only a few optional fonts were missing.

## Running

Pass the browsers, then `--`, then the command that runs the Playwright CLI. The caller picks the
CLI, so the browser builds always match the Playwright version the tests run with: the .NET one from
the E2E project's build output, or the Node one from the pnpm lockfile.

```bash
# From the repository root, on Linux or on Windows under Git Bash:
bash eng/playwright-browsers/install-browsers.sh chromium -- pnpm --dir client exec playwright
bash eng/playwright-browsers/install-browsers.sh chromium -- pwsh test/apps/Tellma.Identity.E2E/bin/Release/net10.0/playwright.ps1
```

The browsers are `chromium`, `firefox` and `webkit`.

The apt step is meant for CI. It runs Playwright's `install-deps` as root, through `sudo -n`, which
a hosted runner allows without a password. Where sudo asks for a password, as on most developer
machines, the script does not prompt: it warns that it cannot install the OS dependencies and
leaves the decision to the launch probe. To install them on such a machine, run the CLI's
`install-deps` under sudo by hand, for example
`sudo env "PATH=$PATH" pnpm --dir client exec playwright install-deps chromium`.

## What it does

1. **Download the browsers** with `playwright install <browsers>`, without `--with-deps`. The builds
   come from Playwright's CDN, not from the apt mirrors; a failed download fails the step.
2. **Install the OS dependencies** (Linux only). A dry run (`install-deps --dry-run`) first asks apt,
   from its local package lists, whether anything is missing; when nothing is, apt is not touched.
   Otherwise `install-deps` runs up to twice, 20 seconds apart. Each attempt runs under a `timeout`
   that is itself root, so that after 150 seconds it stops apt as well as the CLI and the retry
   does not find the apt lock still held. When every attempt fails, or root is not available
   without a password, the script emits a `::warning::` that names the packages still missing,
   and carries on to the probe instead of failing.
3. **Probe each browser**: launch it headlessly through the same CLI and screenshot a blank page.
   A browser that cannot launch fails the step, whatever happened in step 2.

## Outcomes

| apt mirrors | Libraries on the runner | Result |
| --- | --- | --- |
| Reachable | Any | Dependencies installed, the browsers launch, the step passes. |
| Unreachable | All present (only fonts or other optional packages missing) | A warning on the run's summary page; the browsers launch; the step passes. |
| Unreachable | A shared library missing | The launch probe fails with Playwright's list of missing libraries; the step fails. |
| Any | Nothing missing | apt is skipped; the browsers launch; the step passes. |

When a run passes with the warning, a test that depends on a package that could not be installed
(a CJK or Thai font, for instance) can still fail later; the warning names the packages to look at.

Windows and macOS skip step 2: the apt mirrors are a Linux concern, the Windows CI jobs never asked
for `--with-deps`, and those runner images carry what the browsers need. The probe still runs.
