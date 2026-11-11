#!/usr/bin/env bash
#
# Installs Playwright browsers and, on Linux, the OS packages they need, so that an outage of the
# Ubuntu apt mirrors does not by itself fail a CI run. README.md beside this script explains the
# behaviour on each path.
#
# Usage:
#   install-browsers.sh <browser>... -- <playwright CLI command>...
#
# <browser> is chromium, firefox or webkit. The words after `--` invoke the Playwright CLI whose
# version the caller pins, for example:
#   install-browsers.sh chromium -- pnpm --dir client exec playwright
#   install-browsers.sh chromium -- pwsh test/apps/Tellma.Identity.E2E/bin/Release/net10.0/playwright.ps1

set -euo pipefail

# --- Tunables -----------------------------------------------------------------------------------
# A healthy `install-deps` takes seconds on a hosted runner. When the mirrors' host names stop
# resolving, apt waits about 20 seconds on each lookup and one run takes eight minutes or more
# before it gives up, so each attempt is cut short and only one retry is made: enough to ride out
# a blip, and bounded at a little over five minutes when the outage lasts.
readonly deps_attempts=2
readonly deps_attempt_timeout_seconds=150
readonly deps_retry_delay_seconds=20
# A headless launch and a screenshot of a blank page take a few seconds.
readonly probe_timeout_seconds=120

# Prints the usage to stderr.
usage() {
  echo "Usage: $0 <chromium|firefox|webkit>... -- <playwright CLI command>..." >&2
}

# --- Arguments: the browsers, then `--`, then the Playwright CLI command ------------------------
browsers=()
while (($# > 0)) && [[ $1 != -- ]]; do
  case $1 in
    chromium | firefox | webkit) browsers+=("$1") ;;
    *)
      echo "::error::Unsupported browser '$1': expected chromium, firefox or webkit." >&2
      exit 2
      ;;
  esac
  shift
done
if ((${#browsers[@]} == 0)) || (($# < 2)); then
  usage
  exit 2
fi
shift # the `--`
cli=("$@")

# --- 1. The browser builds ----------------------------------------------------------------------
# Downloaded without `--with-deps`, so the download, which comes from Playwright's CDN and not from
# the apt mirrors, succeeds or fails on its own.
echo "==> Installing ${browsers[*]}"
if ! "${cli[@]}" install "${browsers[@]}"; then
  echo "::error title=Playwright browsers::Could not download ${browsers[*]}." >&2
  exit 1
fi

# --- 2. The OS dependencies (Linux only) --------------------------------------------------------
# One of: present (nothing was missing), installed, unavailable (apt failed on every attempt, or
# could not run for want of root or passwordless sudo), or
# not-applicable (not Linux: the apt mirrors are a Linux concern, and the Windows and macOS runner
# images carry what the browsers need).
deps_status=not-applicable
if [[ $(uname -s) == Linux ]]; then
  # The dry run asks apt, from its local package lists and without touching the network, which of
  # the packages Playwright wants are not installed yet. It exits 0 only when none is missing, and
  # then apt is not needed at all.
  echo "==> Checking the OS dependencies of ${browsers[*]}"
  if dry_run_output=$("${cli[@]}" install-deps --dry-run "${browsers[@]}" 2>&1); then
    printf '%s\n' "$dry_run_output"
    deps_status=present
  else
    printf '%s\n' "$dry_run_output"

    # Playwright lists each missing package on its own line, indented by two spaces. The list
    # only enriches the warning below, so a change in that format costs nothing but detail.
    missing_packages=$(printf '%s\n' "$dry_run_output" | sed -n 's/^  \([^ ][^ ]*\)$/\1/p' | paste -sd ' ' -)

    # The time limit must run as root. Playwright's installer runs apt as root (through sudo when
    # it is not root itself), and a `timeout` running as the runner user is not allowed to signal
    # root processes: on expiry it would end the CLI but leave apt running, still holding the apt
    # lock against the retry and installing packages into the middle of the test steps. A root
    # `timeout` owns the whole tree: the CLI, seeing uid 0, runs apt directly, in the process group
    # that `timeout` signals and then kills. `sudo -n` never prompts: where sudo needs a password,
    # as on a developer's machine, it fails at once instead of hanging on a prompt it cannot show.
    # `env PATH=...` keeps the CLI's tools (pnpm, node, pwsh) resolvable past sudo's reset PATH.
    if (($(id -u) == 0)); then
      as_root=()
    elif command -v sudo >/dev/null && sudo -n true 2>/dev/null; then
      as_root=(sudo -n env "PATH=$PATH")
    else
      as_root=(unavailable)
    fi

    if [[ ${as_root[*]} == unavailable ]]; then
      deps_status=unavailable
      echo "::warning title=Playwright OS dependencies::Cannot install the OS dependencies of ${browsers[*]}: that needs root or passwordless sudo, which CI runners have. On a developer machine, run the CLI's install-deps under sudo by hand. Still missing: ${missing_packages:-see the log}. Continuing only if every browser launches without them."
    else
      # Bounded retries, each attempt cut off by the root `timeout` above.
      for ((attempt = 1; ; attempt++)); do
        echo "==> Installing the OS dependencies (attempt $attempt of $deps_attempts)"
        if "${as_root[@]}" timeout --kill-after=15 "$deps_attempt_timeout_seconds" "${cli[@]}" install-deps "${browsers[@]}"; then
          deps_status=installed
          break
        fi
        if ((attempt >= deps_attempts)); then
          deps_status=unavailable
          break
        fi
        echo "Attempt $attempt failed; retrying in ${deps_retry_delay_seconds}s."
        sleep "$deps_retry_delay_seconds"
      done

      # Out of attempts. Not fatal yet: the runner image usually carries every shared library the
      # browsers link against, and what apt could not add is often only fonts. The launch probe
      # below decides; the warning keeps the degraded install visible on the run's summary page.
      if [[ $deps_status == unavailable ]]; then
        echo "::warning title=Playwright OS dependencies::Could not install the OS dependencies of ${browsers[*]} after $deps_attempts attempts; is the apt mirror reachable? Still missing: ${missing_packages:-see the log}. Continuing only if every browser launches without them; a test that needs one of them (a font, for instance) can still fail."
      fi
    fi
  fi
fi

# --- 3. The launch probe ------------------------------------------------------------------------
# Each browser opens a blank page headlessly and saves a screenshot of it, through the same CLI and
# so the same Playwright version the tests use. A missing shared library makes the launch fail here,
# with Playwright's own list of what is missing, rather than in the middle of a test run.
probe_dir=$(mktemp -d)
trap 'rm -rf "$probe_dir"' EXIT
failed_browsers=()
for browser in "${browsers[@]}"; do
  echo "==> Launching $browser"
  screenshot="$probe_dir/$browser.png"
  if timeout --kill-after=15 "$probe_timeout_seconds" "${cli[@]}" screenshot --browser "$browser" about:blank "$screenshot" &&
    [[ -s $screenshot ]]; then
    echo "$browser launched."
  else
    failed_browsers+=("$browser")
  fi
done

# A browser that cannot launch fails the step whatever happened before: when the dependencies
# could not be installed, the outage is a real problem for this run after all.
if ((${#failed_browsers[@]} > 0)); then
  case $deps_status in
    unavailable) reason="and the OS dependencies they need could not be installed (see the warning above)" ;;
    not-applicable) reason="see the Playwright error above" ;;
    *) reason="although their OS dependencies are $deps_status" ;;
  esac
  echo "::error title=Playwright browsers::${failed_browsers[*]} failed to launch, $reason." >&2
  exit 1
fi

echo "==> ${browsers[*]} installed and launching (OS dependencies: $deps_status)."
