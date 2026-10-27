# Verify (Fable, key `session`): scenario walk-through findings

Scenarios walked: cold launch after expiry (two tenants, last tenant 17; deleted tenant; followed
link), in-app expiry then a later `/`, sign-out across three tabs, 412 in all three SemVer cases,
white labelling in Arabic, `TmUrls`/`TmApi` outside a tenant. Finding ids FB-nn.
`must` = contradiction, blocking gap or wrong fact; `should` = misleading or incomplete text.

What walked through cleanly (no finding): `tm-boot.js` 3c/3d → BFF pass-through of `/17` → step 4
`auto` → cold entry → silent picker on 404, and the notice on a followed link; the in-app expiry
keeping `sub`/`lastTenantId` (0024 §8.6, 0025 §2.6) so a later `/` returns to the last tenant; the
sign-out wait, cap, confirm (Sign out / Cancel on the confirm, none on the wait, per g3 item 5)
and broadcast; the SemVer verdicts, `check()` result mapping (matches Angular's driver:
`checkForUpdate()` resolves only after the download, `VERSION_INSTALLATION_FAILED` then `false`,
degraded worker serves a reloaded client from the network); `Distribution_*` keys, `name_localized`,
`Boot_*`, About and the support dialog copy; `forTenant` on Connect and the sandbox marker.

## 0011

- [FB-01] (must) 0011 §5.5 — "Cookie scheme `Tellma.Session`. Cookie `__Host-tellma.session` … sliding `Tellma:Session:IdleLifetime` (default 7 days…)" -> add, in the same bullet: the OIDC challenge issued by `/bff/login` sets `AuthenticationProperties.IsPersistent = true` (with `ExpiresUtc` = the idle expiry, capped by the absolute one), so the session cookie carries `Expires` and survives a browser restart; a renewal re-issues it with the new `Expires`. Why: scenario 1 and 2 (a launch days later; "Farah returns after nine days"). ASP.NET Core's cookie handler writes `Expires` on the auth cookie only when `IsPersistent` is set; otherwise `__Host-tellma.session` is a browser-session cookie that dies when the browser closes while `tellma.profile` (which has an explicit `Expires`, §5.5) lives on. Then 0024 §8.1's invariant "a present cookie means a live server session" and the 7-day warm window (0025 §2.4, ARCHITECTURE "Warm launch") break: every launch after a browser restart renders warm chrome, then the first call 401s into the expiry path.

## 0024

- [FB-02] (must) 0024 §6 — "`build` fails a JS, CSS, font or `tm-metadata` file without one (`unhashed-asset`)" -> "… without one (`unhashed-asset`), excepting the three unhashed scripts the table above serves `no-cache`: `ngsw-worker.js`, `safety-worker.js` and `tm-boot.js`." Also Appendix A's `unhashed-asset` row. Why: §6's own table lists `tm-boot.js`, `ngsw-worker.js` and `safety-worker.js` as unhashed `no-cache` files, and §4.2 writes `tm-boot.js` unhashed (index.html references it by name); as written, the step-6 output check fails every build.
- [FB-03] (must) 0024 §8.4 — "A->>BFF: TmTelemetry.flush(), then POST /bff/logout {scope: global}" -> state when `TmSession.state` becomes `signing-out` and order it after the flush: in step 4, "the acting tab calls `TmTelemetry.flush()`, then sets `state` to `signing-out` (every other tab sets it on `session.signing-out`), then posts `/bff/logout`." Why: scenario 3. 0026 §3.4 says the telemetry queue holds while `state` is not `active` and that "Sign-out calls `flush()` before `/bff/logout`"; §8.4 never says when `state` turns `signing-out` (only that it "returns to `active`" on failure), so an implementer who sets it at step 1 sends no telemetry at sign-out, and 0025 §2.5's `session` guard refuses the acting tab's own adoptions during the wait. The test "flush before logout" cannot be written from the text.
- [FB-04] (should) 0024 §8.1 — "So a present cookie means a live server session." -> "So a present cookie means a live server session (the session cookie is persistent, spec 0011 §5.5)." Why: FB-01; the sentence is true only with a persistent session cookie.
- [FB-05] (should) 0024 §4.2 — "`document.title`, the boot language's display name;" -> define the boot language once, under "The script reads": "the boot language: `record.lang` when a record exists, else the first offered `navigator.languages` entry, else the default; step 4 refines it from the profile", and use it in step 3d's `ui_locales`, the boot page and `document.title`. Why: scenario 1 (3d sends `ui_locales=lang` with no cookie) and scenario 5 (`document.title` and the boot page in Arabic): "boot language" is used in §4.2 and 0027 §6.3 but defined nowhere; step 4's language rule needs the profile, which 3d does not have.
- [FB-06] (should) 0024 §4.2 — "`sign-in-failed` | … | clears the sign-in mark, runs 3d" -> "clears the sign-in mark and re-runs 3d with the mark's own `returnUrl`, `auto` and `sub`". Why: scenario 1 after one failed sign-in: 3b ends the script before 3c, and the previous run's 3c already nulled the record's `sub` and `lastTenantId`, so a fresh 3d has no copies and sends `returnUrl=/`; the expired user's last tenant is lost, against decision 16 ("ALWAYS returns to the last tenant").
- [FB-07] (should) 0024 §10.6 — "404 `route-not-found`, 412 `client-outdated` | `client-outdated` (`client-outdated` or `server-outdated`) | §5.6" -> "… | `client-outdated` (`client-outdated` or `server-outdated`); `server` (`contract-mismatch`) when the build is the same | §5.6". Why: scenario 4; §5.6's fourth row gives kind `server`, code `contract-mismatch`, which this row omits.
- [FB-08] (should) 0024 §10.5 — "Only reads retry, on the failures below." -> add a row to the retry table: "412 `server-outdated` | 30 s | 1 (§5.6)", or append "and once after 30 s on `server-outdated` (§5.6)". Why: scenario 4 rollback: §5.6 says a read is "retried once after 30 s, then inline", but §10.5 lists only network, 429 and 503, and `tm.api.retry { reason }` has no value for it.
- [FB-09] (should) 0024 §5.6 — "A second `server-outdated` verdict for the same server build therefore asks the worker first" -> add whether the 3 s cap applies and the verdict past it: "under the same 3 s cap; past the cap, `client-outdated` (the download finishes before Reload)". Why: scenario 4 rollback to 1.3.0: the worker must download the whole older version before `ready`, which rarely fits 3 s; the unordered row has a cap and a fallback, this one has neither, so the verdict after the cap is undefined.
- [FB-10] (should) 0024 §11 — "Buttons: Try again (with `retry`) and Close." (the 5xx dialog) -> "Buttons follow `view.actions`: Reload, Reload anyway, Try again (with `retry`) and Close." Why: scenario 4 on a write: §5.6 says `client-outdated` on a write shows "the dialog with Reload", and the Reload flow "keeps the dialog" with "Reload anyway", but neither dialog's button list includes those actions (the 400 dialog has "one Close button"), so an implementer cannot place them.

## 0025

- [FB-11] (must) 0025 §3.2 — "Cold, and the sync fails `tenant-not-found` or `tenant-unavailable`. Spec 0024 §11 applies…" -> add a row: "Cold, and the sync fails `client-outdated`: the tenant shell shows `tm-error-state` from 0024 §11's `client-outdated` view (Reload, with its busy, failed and 'Reload anyway' states; `server-outdated` shows Try again, which reruns the sync)". Why: scenario 4 on an old tab entering a cold tenant after a deploy: `client/sync` is a read with no page behind it; 0024 §5.6 presents a read's 412 "inline" through the page, 0024 §11 says `present` on reads covers only `tenant-not-found` and `tenant-unavailable`, and §5.3 step 7 only says "`TmApi` handles … `client-outdated`"; nothing renders, so the shell holds its skeleton forever.

## 0026

- [FB-12] (should) 0026 §3.4 — "No active session (`TmSession.state` not `active`): the queue holds. Sign-out calls `flush()` before `/bff/logout`" -> "… the queue holds, except the sign-out `flush()`, which runs before `state` turns `signing-out` and before `/bff/logout` (spec 0024 §8.4)". Why: FB-03; the two sentences contradict unless the order is fixed.
- [FB-13] (should) 0026 §7 — "the About dialog (spec 0027 §11.1) shows `label` for the UI, platform and features" -> "shows `label` for the UI and the raw versions for the platform and features". Why: scenario 5 About walk-through; 0027 §11.1's table shows `TmAppVersion.platform` and `features[].version` raw (`X.Y.Z+<40 hex>`), not a `label` form.

## 0027

- [FB-14] (should) 0027 §7.5 — "| Cold, sync failed `offline` or `unavailable` | as pending | empty | `tm-error-state` "Couldn't open …" with Retry" -> add the row "Cold, sync failed `client-outdated` | as pending | empty | 0024 §11's `client-outdated` view with Reload (spec 0025 §3.2)". Why: FB-11; the shell's state table has no state for it.
- [FB-15] (should) 0027 §11.1 — "| Platform | `TmAppVersion.platform` |" -> either show it as `label` does for the UI (`X.Y.Z (7-char sha)`) or keep the raw value and fix 0026 §7 (FB-13). Why: the two specs describe the same row differently; a 40-hex sha beside "1.4.0 (3f9c2ab)" is the misleading reading.
