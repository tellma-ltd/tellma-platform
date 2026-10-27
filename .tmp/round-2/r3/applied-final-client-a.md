# Final fix pass, client-a (specs 0023–0029)

Findings applied from `verify-fable-session.md`, `verify-fable-data.md` and `verify-residual.md`.
`verify-fable-reports.md` has no heading for 0023–0029.

## 0010–0022 and ARCHITECTURE.md

- No changes (outside this range).

## 0023–0035 (this range)

**0023**
- §9 Tokens: "One new semantic group, `categorical`" → "The semantic group it reads, `categorical`" (VR-04).

**0024**
- §3.1: `Distribution_Copyright` reads "in the active language, else `copyright`" (VR-01).
- §4.2: "The boot language" bullet defines it once (steps 1–3: `record.lang`, else the first offered
  `navigator.languages` entry, else the default; step 4: its own choice); 3d's `ui_locales` names it
  (FB-05).
- §4.2 boot page: `sign-in-failed` Try again re-runs 3d with the old mark's `returnUrl`, `auto`,
  `expired` and `sub`, writing a fresh mark; §13 `tmBoot` vectors pin it (FB-06).
- §4.3 step 2: the boot step's one read includes the device partition (FC-02).
- §5.6: write cells name the 5xx dialog (Reload / Try again); `server-outdated` read cites §10.5; the
  rollback ask runs under the same 3 s cap (`none` keeps `server-outdated`; `ready`, any other result
  or the cap gives `client-outdated`); §13 TmUpdates row pins the cap (FB-09, FB-10).
- §6 and Appendix A `unhashed-asset`: `ngsw-worker.js`, `safety-worker.js` and `tm-boot.js` excepted
  (FB-02).
- §7.7 step 5 and "Server text": the browser-check fallback changes only `locale`; `Accept-Language`
  keeps step 4's string. §10.2 header row says so; §13 TmLocalization row and Review flag 21
  ("for client formatting") follow (FC-03).
- §8.1: "a present cookie means a live server session (the session cookie is persistent, spec 0011
  §5.5)" — 0011 §5.5 already carries the persistent cookie (FB-04, after FB-01).
- §8.4: diagram and step 4 order it: `TmTelemetry.flush()`, then `state = signing-out` and
  `session.signing-out`, then `/bff/logout`; `state` stays `active` during the wait. "Signing out"
  bullet: other tabs set `state` on the message. §13 sign-out flow pins the flush order (FB-03).
- §10.5: retry table gains "`412` or `404 route-not-found` judged `server-outdated` | 30 s | 1";
  `tm.api.retry` reason gains `server-outdated`; §13 TmApi row pins it (FB-08).
- §10.6: the 412/route-not-found row adds "`server` (`contract-mismatch`) when the build is the same"
  (FB-07).
- §11: `{field}` resolves `arguments.path` through `TmEntities.resolve(entity, path).label` against
  the context's `entity`, raw path otherwise (FC-04); the 5xx dialog's buttons follow the view's
  `actions` (Reload, Reload anyway, Try again, Close) (FB-10).
- Review flag 2: keeps only the client side (`TmDecimal` strings, plain `JSON.parse`, two lint rules)
  and cites spec 0016's flag 2 for the wire format (VR-02).

**0025**
- §2.6 boot step: the one `readAll` covers the device partition too; §14 Store row pins `zone` in the
  mirror after it (FC-01).
- §3.2: "five paths"; new bullet for a cold sync failing `client-outdated` (0024 §11's view: Reload,
  or Try again for `server-outdated`) (FB-11).

**0026**
- §3.4: sign-out's `flush()` runs before `state` turns `signing-out` and before `/bff/logout` (FB-12).
- §7: the About dialog shows `label` for the UI and raw versions for server, platform and features
  (FB-13, settles FB-15).
- §8.3: the hand-written `event.code`/`event.key` rule is replaced by "Matching": spec 0027 §4's
  registry with `tmKeysMatch`, in fields and over modals per spec 0027 §4.2 (VR-03).

**0027**
- §7.5: new shell-state row "Cold, sync failed `client-outdated`" (FB-14).

**0029**
- §5.3: new bullet "Ids by key type": `label`, `remember`, `resolve` and `entitySet().byId` lookups
  coerce an id to the entity's `key.type`; §17 descriptors row pins it (FC-06).

## Skipped findings

- FB-15 (0027 §11.1): no edit. FB-15 lets the fixer pick raw values plus the FB-13 fix. The Server
  row already shows a raw build, so raw platform and feature versions match it. 0026 §7 was
  corrected instead.

## Choices to confirm

- FB-09: on a rollback's second `server-outdated`, the worker check has the same 3 s cap. `none` keeps
  `server-outdated`. `ready`, `failed`, `unreachable` or the cap gives `client-outdated`, and Reload
  finishes the download.
- FB-10: `client-outdated` and `server-outdated` on a write use the 5xx dialog (`TmErrorDialog`),
  not the 400 dialog.
- FB-13/FB-15: the About dialog keeps raw platform and feature versions instead of a label form.
- FC-03: `Accept-Language` sends the resolved `<language>-<region>[-u-nu-…]` string even when the
  browser lacks it. So on Chromium an `om` user gets Oromo server text while the client formats in
  the fallback locale.
- VR-02: Review flag 2's alternative is now a client-side one, "a decimal class created from the
  strings at the wire edge".

## Unresolved for other ranges

- None. FB-04 relies on 0011 §5.5's persistent session cookie (FB-01), which the server range
  has already applied.
