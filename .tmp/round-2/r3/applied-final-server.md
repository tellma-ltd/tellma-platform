# Final fix pass: server range (0011–0022, ARCHITECTURE.md)

Findings in range: FB-01 (0011, verify-fable-session.md) and FC-05 (0016, verify-fable-data.md).
verify-fable-reports.md and verify-residual.md have no heading for 0011–0022. Both findings were
confirmed against the current text and applied.

## Changes to 0010–0022 and ARCHITECTURE.md

### 0011

- §5.5 "Cookie scheme `Tellma.Session`": the session cookie is persistent. `/bff/login` challenges
  with `AuthenticationProperties.IsPersistent = true`, so `__Host-tellma.session` carries `Expires`
  (the session row's `ExpiresAt`, §7.4: idle expiry capped by the absolute expiry) and survives a
  browser restart; every renewal re-issues it with the new `Expires`. Without it ASP.NET Core
  writes a browser-session cookie while `tellma.profile` outlives it, breaking the 7-day warm
  window. (FB-01)
- §5.5 "The profile cookie", Lifetime: `Expires` now reads "with the session cookie's `Expires`"
  instead of restating the idle/absolute rule (one fact, one place). (FB-01)
- §10 Testing, `Tellma.Core.AspNetCore.Tests` "Sessions": adds the session cookie written
  persistent, its `Expires` equal to the row's `ExpiresAt` at sign-in and after a renewal. (FB-01)

### 0016

- §6.1 request headers, `Accept-Language` row, "Precedence when absent": the language chain reads
  `User.PreferredLanguage` → tenant primary language, when offered → the distribution default
  (spec 0013 §7.3), replacing the hard-coded `en` (Acme's default is the first declared code).
  (FC-05)

### 0012–0015, 0017–0022, ARCHITECTURE.md

- No change. ARCHITECTURE.md's "Warm launch" text (session idle window, 7 days) already matches
  FB-01 once the cookie is persistent.

## 0023–0035

- None (outside this range).

## Skipped findings

- None.

## Choices to confirm

- FB-01: persistence is unconditional (no "keep me signed in" opt-out); a user on a shared
  computer relies on sign-out. This follows from the 7-day warm window (0011 Decision 30,
  ARCHITECTURE "Warm launch"); the alternative is an opt-in persistent cookie with a
  browser-session default, which would make every launch after a browser restart cold.

## Unresolved for other ranges

- None required. 0024 §8.1 (FB-04, already a finding for the 0024 owner) may now cite spec 0011
  §5.5 for the persistent session cookie, as that finding proposes.
