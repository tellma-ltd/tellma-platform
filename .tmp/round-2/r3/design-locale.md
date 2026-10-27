# Design: regional formats, time zones and launch redirects

This note designs decisions 16, 19 and 24 of `decisions.md`. It wins over the g4 and g5 analyses on
these topics. Part 4 is the edit plan. Contract blocks are C# or TypeScript sketches: names and
shapes are normative; bodies, imports and doc comments are omitted.

## 0. Verified facts

Run on 2026-10-26 with Node 24.18.0 (ICU 78.3, CLDR 48), Chromium 152 and .NET 10.0.12 (ICU mode).

- **Pairs.** .NET predefines 243 `<lang>-<RR>` cultures for the 15 catalogue languages (26 for `ar`,
  104 for `en`). Node resolves all 243 exactly (`resolvedOptions().locale` equals the tag). Chromium
  resolves 235: it has no `om`, `ti` or `so` data, and `om-ET` becomes `en-US`.
- **`en-SA` exists nowhere.** .NET throws `CultureNotFoundException`; browsers fall back to `en`
  (US dates). `en-AE` exists on both ends.
- **`-u-nu-latn` works on both ends.** .NET never prints native digits, whatever the tag.

  | Tag | Browser | Browser week | .NET `N2` |
  |---|---|---|---|
  | `ar-SA` | ١٬٢٣٤٬٥٦٧٫٨٩ | Sunday | 1٬234٬567٫89 (hybrid) |
  | `ar-SA-u-nu-latn` | 1,234,567.89 | Sunday | 1,234,567.89 |
  | `ar-AE` / `ar-AE-u-nu-arab` | 1,234,567.89 / ١٬٢٣٤٬٥٦٧٫٨٩ | Monday | 1,234,567.89 / 1٬234٬567٫89 |

- **V8 drops unknown `nu` values:** `ar-SA-u-nu-native` resolves to `ar-SA`, so the client names
  the system (`arab`). A clone of `ar-SA-u-nu-latn` accepts `GregorianCalendar` in .NET.
- **Zone names.** `shortOffset` gives "GMT+3", "GMT+5:30", "غرينتش+٣" (`ar-SA`); `longGeneric`
  gives "Arabian Standard Time", "United Kingdom Time".

## 1. Regional formats and digits (decision 24)

Formats become their own axis, separate from the UI language. A user and a tenant each choose a
**Region** and **Digits**. The client builds one locale string from them, and the server reads the
same values, so both ends format alike.

### 1.1 Vocabulary

- **Region** — an ISO 3166-1 alpha-2 code (`SA`) that picks date and number patterns, separators
  and the first day of the week.
- **Digits** — `Latin` or `Native`. `Native` means the language's own digit set (Arabic-Indic for
  `ar`). Unset means the region's default.
- **Offered pair** — a (language, region) pair in the catalogue (§1.3).
- **Locale** — the BCP 47 string the client formats with and sends: `ar-SA-u-nu-latn`.

### 1.2 Storage

| Table | Column | Type | Null | Meaning |
|---|---|---|---|---|
| `core.Users` | `PreferredRegion` | `varchar(2)` | yes | upper case; null = the tenant's `Region` |
| `core.Users` | `PreferredDigits` | `varchar(6)` | yes | `Latin` or `Native`; null = the tenant's `Digits` |
| `core.Settings` | `Region` | `varchar(2)` | yes | null = each language's distribution default |
| `core.Settings` | `Digits` | `varchar(6)` | yes | null = the region's default |

```csharp
// Tellma.Core.Abstractions.Localization
public enum DigitStyle { Latin, Native }                  // stored as strings, like every enum

// User (spec 0014 §3.1) gains
public string? PreferredRegion { get; set; }              // ISO 3166-1 alpha-2; null = the tenant's Region
public DigitStyle? PreferredDigits { get; set; }          // null = the tenant's Digits

// TenantSettings and TenantSettingsForClient (spec 0013 §5.6) gain Region and Digits, the same types
```

### 1.3 The catalogue: offered pairs, defaults, native digits

The catalogue (spec 0013 §7.2) stays data in code. Each language gains three facts.

```csharp
// Tellma.Core.Abstractions.Localization
public sealed record LanguageFormats(
    string Language,                   // "ar"
    string DefaultRegion,              // the distribution's choice for an offered language ("SA"), else the catalogue's ("EG")
    IReadOnlyList<string> Regions,     // the offered regions, ordinal order: "AE", "BH", …, "YE"
    string? NativeDigits);             // a CLDR numbering-system id: "arab"; null when the language's digits are Latin

public interface ILanguageCatalog      // existing: Offered, Find, IsOffered
{
    LanguageFormats Formats(string language);   // any catalogued language; throws KeyNotFoundException for an unknown code
    bool IsRegionOffered(string region);        // offered with at least one offered language
}
```

- **Regions** are every region `R` for which .NET 10 predefines `<lang>-R` today: the 243 pairs of
  §0, plus `zh` with `CN`, `HK`, `MO`, `SG`, `TW` (alias names that both ends resolve). The list is
  frozen in code, so a host's ICU version never changes what users may store.
- **Native digits:** `ar` → `arab`, `ur` → `arabext`, `hi` → `deva`; every other catalogue
  language → null.
- **Default regions:** `en` → `GB` (0024 Decision 12); every other language → its CLDR likely
  region (`ar` → `EG`, `am` → `ET`, `fr` → `FR`, `ur` → `PK`).
- **The distribution's choice.** `Languages(codes)` accepts a region per code. Acme declares
  `tellma.Languages(["en-GB", "ar-SA"])`. A region outside the language's list is a startup problem.
- **Browser gaps** fall back on the client (§1.4 step 5). The cross-engine test asserts the known
  gap list (`om`, `ti`, `so` on Chromium), so a new gap fails the build.
- **One owner.** The default locale moves from the client to the server: `TmLanguageDefinition`
  loses `locale`, and Acme writes `tmArabic()`.

### 1.4 Resolution and the locale string

The client and the server resolve the same three values in the same order.

1. **Language** — unchanged (0024 §7.1; 0013 §9.2).
2. **Region** — the first of `me.PreferredRegion`, `settings.Region`, the language's
   `DefaultRegion` that is offered with the active language. Outside a tenant: `DefaultRegion`.
3. **Digits** — `me.PreferredDigits`, else `settings.Digits`, else unset. Outside a tenant: unset.
4. **The string.** Base = `<language>-<region>`. `Latin` appends `-u-nu-latn`; `Native` appends
   `-u-nu-<NativeDigits>` when the language has native digits. The extension is appended only when
   it changes the base's default system, so the string stays canonical.
5. **Browser check (client only).** Once per base, `TmLocalization` compares
   `resolvedOptions().locale` of `DateTimeFormat` and `NumberFormat` with the base. A miss falls
   back to `<language>-<DefaultRegion>`, then to the default language's locale. It emits
   `tm.l10n.locale-fallback { language }` once per launch.

**Example —** Acme (`en-GB`, `ar-SA`):

| Language | User region | Tenant region | Digits (user → tenant) | Locale |
|---|---|---|---|---|
| `ar` | — | — | — | `ar-SA` |
| `ar` | — | `AE` | — | `ar-AE` |
| `ar` | — | — | `Latin` | `ar-SA-u-nu-latn` |
| `ar` | `AE` | `SA` | `Native` | `ar-AE-u-nu-arab` |
| `en` | `SA` | — | `Latin` | `en-GB` (`en-SA` not offered; Latin is `en`'s default) |
| `en` | `AE` | — | — | `en-AE` |

### 1.5 Client metadata

`SettingsClientAssetContributor` (0013 §1.2) writes each offered language with its formats.

```ts
interface TmLanguageMetadata {           // the Core chunk's languages[] (spec 0013 §1.2)
  readonly code: string; readonly nativeName: string; readonly dir: 'ltr' | 'rtl';
  readonly region: string;               // the distribution's default region: 'SA'
  readonly regions: readonly string[];   // offered regions, ordinal order
  readonly nativeDigits: string | null;  // 'arab'; null when Latin
}
```

```json
{ "code": "ar", "nativeName": "العربية", "dir": "rtl", "region": "SA",
  "regions": ["AE", "BH", "DJ", "DZ", "EG", "ER", "IL", "IQ", "JO", "KM", "KW", "LB", "LY",
              "MA", "MR", "OM", "PS", "QA", "SA", "SD", "SO", "SS", "SY", "TD", "TN", "YE"],
  "nativeDigits": "arab" }
```

### 1.6 Headers and server negotiation

- **The header.** The SPA sends `Accept-Language: <locale>` (`ar-SA-u-nu-latn`), as today.
- **Culture (0013 §9.2).** The language matches as today. The region is the header's, else
  `PreferredRegion`, else the tenant's `Region`, else `DefaultRegion`, skipping any region not
  offered with that language. Extensions are stripped; the server ignores `nu`.
- **Server digits.** `CultureInfo` is the Gregorian clone of `<lang>-<RR>-u-nu-latn`. Server text
  (emails, MCP, `message`) always shows "1,234,567.89", never the hybrid "1٬234٬567٫89". A `Native`
  user sees Arabic-Indic digits in the app and Latin ones in server text (Review flag R3).
- **`RequestContext.Culture`** is `ar-SA`: language and region, no extension (spec 0011 §4.1).
- **The broken path goes.** `PreferredLanguage` holds a bare code; `ar-SA` is
  `Users.LanguageNotOffered`, as `IsOffered` already rules. 0013 §9.2's "regioned
  `PreferredLanguage`" sentence is deleted.
- **`NegotiationInput`** gains `PreferredRegion`; the prologue's profile set loads both new columns.

### 1.7 Writes, requests and validation

```csharp
// Tellma.Core.Abstractions.Access (spec 0018 §3.1)
public sealed record MeSaveRequest(
    string Name, string? Name2, string? Name3, int? ImageId, int? SignatureId,
    string? PreferredLanguage, string? PreferredCalendar, string? PreferredTimeZone,
    string? PreferredRegion, DigitStyle? PreferredDigits, Gender? Gender,
    string? ContactEmail, string? ContactMobile, DateTimeOffset ModifiedAt,
    ConcurrencyMode Concurrency = ConcurrencyMode.Check);   // thirteen self-editable members

public sealed record MeLocaleRequest(string? Language, string? Calendar, string? TimeZone);  // null member = unchanged; at least one set
public sealed record MeLocaleResult(
    string? PreferredLanguage, string? PreferredCalendar, string? PreferredTimeZone,
    DateTimeOffset ModifiedAt, string PreferencesTag);
```

- **Region and digits travel in `me/save` only**, since no in-place switch sets them (§1.8).
- **`me/locale` gains the time zone** for the prompt and the automatic update (§2.4, §2.5), checked
  by the `PreferredTimeZone` row at path `timeZone`.
- **Settings save.** `TenantSettingsPatch` gains `Region` and `Digits`, under `Settings.General`.

| Rule | Code (path) |
|---|---|
| `PreferredRegion` is null or `ILanguageCatalog.IsRegionOffered` | `Users.RegionNotOffered` (`PreferredRegion`) |
| `Settings.Region` is null or `IsRegionOffered` | `Settings.RegionNotOffered` (`Region`) |
| `PreferredDigits`, `Digits` | no code: an unknown enum value fails spec 0016's enum binding |
| a `me/locale` request sets `Language`, `Calendar`, `TimeZone` or several | `Users.LocaleEmpty` (`language`) |

Preprocessing trims and upper-cases `PreferredRegion` and `Region`, and lower-cases
`PreferredLanguage`.

**Example —** the automatic time-zone update (§2.5):

```json
POST /17/api/web/users/me/locale
{ "timeZone": "Europe/London" }
200 { "preferredLanguage": "ar", "preferredCalendar": "uq", "preferredTimeZone": "Europe/London",
      "modifiedAt": "2026-10-26T09:14:02.1234567+00:00", "preferencesTag": "1.7f3a…" }
```

### 1.8 Where they are edited

- **My profile and the Users page.** `tm-user-locale-fields` (0032 §4.1) renders Language,
  Calendar, Time zone, Region and Digits, each nullable: "Tenant default ({effective})".
- **`tm-region-field`** lists the regions offered with the active language, named by
  `Intl.DisplayNames` and sorted by `Intl.Collator`. A live sample formats `1234.5` and today's date
  with the candidate locale: "١٬٢٣٤٫٥ · ١٩/٠٦/٢٠٢٦".
- **`tm-digits-field`** offers "Latin (123)" and "Arabic-Indic (١٢٣)" (a `tmUi` name per system).
  It hides when the active language has no native digits.
- **Settings, General tab.** A *Formats* card after *Time zone*: Region ("Language default") and
  Digits ("Region default").
- **Not in the session menu.** Both are set once, not switched like language, and My profile is one
  click away (Review flag R1).

### 1.9 Live switching

- **Inputs.** `TmLocalization.locale` is computed from `me`, `settings` and the metadata. A My
  profile save adopts `me`, a Settings save adopts `settings`, and other tabs follow `store.changed`.
- **Apply.** The new locale applies as 0024 §7.2's step 2, in one pass, with no string load and no
  extra request. Grids, date pickers (`tmFirstDayOfWeek`) and server-text arguments re-render.
- **Telemetry:** `tm.l10n.locale-changed { from, to }`, carrying locale tags only.

## 2. Time zones (decision 19)

Absolute date-times carry a GMT offset label when the display zone's offset differs from the
device's. When the device's zone **changes**, a one-time dialog offers to switch the user's time
zone. A per-device option makes later switches automatic. A zone that merely differs never prompts.

### 2.1 The label rule

The display zone is unchanged: `me.PreferredTimeZone`, else the tenant's `TimeZone` (0024 §7.1).
The **device zone** is the browser's resolved zone. Offsets are compared **at the instant shown**,
never by zone id, so `Asia/Kuwait` and `Asia/Riyadh` (both +3) never label.

| Surface | Label |
|---|---|
| A details field, `tm-value` in view mode, an absolute time ("Updated 10:42"), the tooltip of `tm-relative-time` | appended when the offsets differ at that instant: "20/10/2026, 10:42 GMT+3" |
| A grid or report column of instants | once, in the header: "Created at (GMT+3)", when the offsets differ now; cells stay bare |
| A cell in such a column whose display offset differs from the header's (a DST change between rows) | its own label: "12/01/2026, 10:42 GMT+2" |
| Relative time ("3 hr ago"); dates; `datetime` wall-clock values; a format with `timeStyle: 'none'` | never |
| Server text and Excel | never (out of scope) |

- **Offsets, never abbreviations.** "IST" and "CST" are ambiguous; "GMT+5:30" is not, and it
  localizes ("غرينتش+٣").
- **Re-read.** `TmLocalization.deviceTimeZone` re-reads the browser zone on `visibilitychange` to
  visible and on `pageshow` with `persisted`. No browser event reports a zone change.

**Example —** Farah's display zone is Asia/Riyadh, and her laptop is in London. A Sales invoice
shows "Created at 20/10/2026, 10:42 GMT+3". The Sales invoices grid header reads "Created at
(GMT+3)". The bell says "3 hr ago", and its tooltip "20 Oct 2026, 10:42 GMT+3".

### 2.2 The format amendment (0010 §D.4)

```ts
// @tellma/core-ui/l10n — pure, DOM-free, DI-free
interface TmDateTimeFormatOptions { /* existing */ readonly zoneLabel?: boolean }  // true appends the zone's 'shortOffset' name
interface TmTimeFormatOptions { /* existing */ readonly zoneLabel?: boolean }
declare function tmFormatZoneOffset(instant: string | number, locale: string, timeZone: string): string; // "GMT+3"
declare function tmFormatZoneName(timeZone: string, locale: string, at: number): string;                 // 'longGeneric': "United Kingdom Time"

// @tellma/core-ui (primary)
declare const TM_DEVICE_TIME_ZONE: InjectionToken<Signal<string>>; // default: the browser's zone, read once; @tellma/core provides a live one
type TmZoneLabelMode = 'auto' | 'column' | 'none';
// TmL10n gains:
readonly deviceTimeZone: Signal<string>;
readonly zoneMismatch: Signal<boolean>;          // the offsets differ now; recomputed when a zone changes and on visibilitychange
zoneLabel(instant?: string | null): string;      // the display zone's offset at instant (default now) when it differs from the device's; else ''
// formatDateTime(instant, options?) and formatTime(instant, options?) gain zoneLabel?: TmZoneLabelMode (default 'auto')
```

- **`'auto'`** labels when `zoneLabel(instant)` is not empty.
- **`'column'`** is for grid cells. It labels when the header shows a label and the instant's
  display offset differs from it, or when the header shows none and `'auto'` would label.
- **`'none'`** never labels.
- **The golden.** `dateTime` and `time` rows gain an optional `zoneLabel: true`, with rows for
  `en-GB`, `ar-SA` and `ar-SA-u-nu-latn` in Asia/Riyadh, and Asia/Kolkata (+5:30). A new array
  `zoneLabel` holds `{ locale, timeZone, deviceTimeZone, instant, mode, header, expected }` rows:
  equal offsets with different ids, Asia/Beirut across its DST change, and UTC. The server twin
  skips label rows.

### 2.3 Detecting a change

`TmDeviceZone` (a primary service; its dialog is a deferred chunk it imports on demand) checks
after first render at idle, on each tenant entry, and on `visibilitychange` to visible. It acts
only in a visible tab of a tenant, once that tenant's `me` and `settings` are present.

Its memory is one document in the **device partition** of spec 0025's store:

```ts
interface TmZoneDoc {                                  // device partition, document 'zone'
  readonly users: Readonly<Record<string, {            // keyed by sub; at most 20 users, the oldest `at` dropped
    readonly auto: boolean;                            // "change it automatically" on this device
    readonly seen: Readonly<Record<string, string>>;   // tenant id → the device zone last handled in that tenant
    readonly at: number;                               // the last write, for the cap
  }>>;
}
```

- **Why the device partition.** The option is per user and per device. User partitions die at
  sign-out and expiry (0025 §2.6), so they would forget it after every sign-out and after any trip
  longer than the 7-day idle window. The device partition survives both (Review flag R6).
- **Why per tenant.** `PreferredTimeZone` lives on each tenant's `core.Users` row, so each tenant
  notices the change on its next entry, and an automatic update reaches every tenant the user opens.

The check, step by step (`device` = the device zone; `seen` = `seen[t]`):

1. **First run.** No `seen`: record `device` and stop. A new device or tenant never prompts.
2. **No change.** `device` equals `seen`: stop.
3. **Already matching.** `device` equals the display zone: record and stop.
4. **Read-only tenant.** Record and stop: `me/locale` would answer 403.
5. **Automatic.** The option is on: run §2.5's update, record, stop.
6. **Same offset now.** Record and stop: nothing visible would change.
7. **Prompt.** Take the Web Lock `tm:<slug>:<sub>:zone-prompt` with `ifAvailable`. Without it,
   stop: another tab asks. With it, wait for the input-idle gate (0028 §9.3) and for no open modal.
   Re-run steps 2–6, then open the dialog (§2.4). Record when the user answers or dismisses; a tab
   closed before that records nothing, so the next check asks again.

### 2.4 The prompt

**The surface is a dialog** (`TmModal`, size `sm`, `role: 'dialog'`, `describedBy` the message).

- **Not a toast.** Toasts serve arrivals and outcomes, a closed list (0028 §8.1). They time out,
  never take focus and hold no checkbox, so an unanswered question would vanish.
- **Not a banner.** A banner holds 45 characters and one action, may be paged behind others (0028
  §9.1), and serves lasting conditions; the labels already show this one.
- **A dialog fits.** It holds the question, two answers and the checkbox, with standard focus
  handling. It is rare (once per change, per tenant), waits for 2 s of input idle, never opens over
  another dialog, and floats, so nothing shifts.

| Part | Copy |
|---|---|
| Title | "Use your device's time zone?" |
| Message | "Your device is now on {deviceZone} ({deviceOffset}). {displayName} shows times in {displayZone} ({displayOffset})." |
| Checkbox | "Change it automatically next time" |
| Its hint | "Only on this device. You can turn this off in My profile." |
| Primary | "Change time zone" |
| Secondary | "Keep current" (also Escape and the close button) |
| Unsupported zone (`Users.TimeZoneUnknown`) | inline `tm-alert`: "{displayName} doesn't support your device's time zone. Choose one in My profile." |

- **Names.** Zones come from `tmFormatZoneName`, offsets from `tmFormatZoneOffset`, and
  `{displayName}` is the localized display name (decision 25).
- **Change time zone** turns busy in the click's frame and writes `users/me/locale { timeZone }`.
  The caller adopts the result (0025 §5.3), so every tab re-renders its times, and the labels
  vanish. With the box ticked, it also turns the option on. Any other failure shows
  `TmErrorPresenter.describe(error, { kind: 'save' })` inline; the dialog stays.
- **Keep current** never turns the option on, even with the box ticked.

**Example —** Farah flies from Riyadh to London.

1. Her laptop switches to Europe/London. She returns to the Acme tab: the device zone is re-read.
2. `seen[17]` is Asia/Riyadh. The option is off, and +1 differs from +3.
3. After 2 s without input, the dialog asks: "Your device is now on United Kingdom Time (GMT+1).
   Acme shows times in Arabian Standard Time (GMT+3)."
4. She ticks the box and chooses Change time zone. Her preference becomes Europe/London, every tab
   re-renders, and `seen[17]` becomes Europe/London.
5. A week later, back in Riyadh, the change applies by itself (§2.5).

Omar's office desktop stays in Riyadh while his preference is Europe/London. His device zone never
changes, so he is never asked; his times show "GMT+1".

### 2.5 The option and the automatic update

```ts
declare class TmDeviceZone {                     // @tellma/core primary; My profile reads it too
  readonly auto: Signal<boolean>;                // this user's option on this device
  setAuto(on: boolean, source: 'prompt' | 'profile'): Promise<void>; // patches the 'zone' document
  check(): Promise<void>;                        // §2.3; also run on tenant entry and visibility
}
```

- **My profile** shows the option in the *Regional* card, under the Time zone field:
  `tm-auto-time-zone`, a `tm-checkbox` "Change my time zone automatically when this device's time
  zone changes", hint "Saved on this device only." It applies at once in view and edit mode and is
  not part of the form's Save. The Users page never shows it.
- **The update** (step 5 of §2.3) writes `users/me/locale { timeZone: device }` in the background
  and adopts the result. It then toasts "Times now show in {zone} ({offset})." with "Change", which
  opens My profile. This is an outcome with no inline home (0028 §8.1).
- **Failures.** `offline` retries once when `TmApi.online` turns true. `Users.TimeZoneUnknown`
  records the zone and stops. Every other failure records nothing, so the next check retries.
- **Change, not difference.** Only a detected change triggers the update. A user who sets another
  zone in My profile keeps it until the device moves again, and two devices never ping-pong.

### 2.6 Telemetry

- `tm.l10n.zone-change { action }`: `first-run`, `matches`, `read-only`, `same-offset`, `auto`,
  `prompt`.
- `tm.l10n.zone-prompt { answer, auto }`: `change`, `keep` or `dismiss`, and the box.
- `tm.l10n.zone-auto { outcome }`: `ok`, `offline`, `rejected`. `tm.l10n.zone-option { on, source }`.
- No event carries a zone id or an offset: both reveal where the user is.

### 2.7 Tests

- **Unit:** `TmL10n` over the golden (DST, equal offsets, `'column'`); `TmDeviceZone` through each
  step of §2.3 with fake clock, locks and store (two tabs, one dialog; Keep with the box ticked;
  each failure).
- **Harness:** the dialog in `en` and `ar`, both themes, 390 and 1440 px; focus; Escape; axe.
- **e2e:** `device-zone.spec` (Chromium, zone moved by CDP `Emulation.setTimezoneOverride`): silent
  first run, a prompt after the idle gate, every tab re-rendered, the automatic toast.
  `zone-labels.spec` (`@cross-engine`, `timezoneId` London vs Riyadh): labels present or absent.

## 3. Launch redirects (decision 16)

The launch picks the tenant whenever the user did not: the BFF after a sign-in to `/`, `tm-boot.js`
after an expiry, and the SPA at `/`. Each pick marks the entry **automatic**. A
`404 tenant-not-found` on an automatic entry goes to the picker silently; on any other entry the
picker shows its notice.

### 3.1 The landing rule and its shared vectors

```csharp
// Tellma.Core.Abstractions.Tenancy
public static class TenantLanding
{
    public static int? SingleLiveTenant(IReadOnlyList<TenantMembership> memberships); // the one Live, Active, IsActive membership; sandboxes never count; else null
}
```

```ts
// @tellma/core primary
declare function tmSingleLiveTenant(tenants: readonly TmMembership[]): number | null; // the same rule
```

Both suites read one file, `launch-landing-vectors.json` in `@tellma/core` (`src/launch/`); the C#
test project links it, as 0029's display-name golden does.

Each row is `{ name, tenants, expected }`, with `tenants` in `/bff/user`'s entry shape. Rows: one
live (1); a live tenant and its sandbox (1); sandbox only, two live, `ReadOnly`, deactivated,
suspended, none (each `null`).

### 3.2 The BFF after sign-in (spec 0011 §5.5)

- **When.** `OnTicketReceived`, only when the target (`TicketReceivedContext.ReturnUri`) is exactly
  `/` or absent. Deep links, `/tenants` and step-up returns pass through, so the picker still never
  redirects.
- **What.** One `ITenantMembershipDirectory.ListAsync(sub)` catalog query, then
  `TenantLanding.SingleLiveTenant`. A hit sets `ReturnUri = "/{id}"`.
- **Failure.** A directory error keeps `/` and logs `BffLandingFailed` (Warning). The SPA's
  launch redirect then decides, as before.
- **Gain.** A single-tenant user signing in at `/` sees the tenant shell first: no tenantless
  skeleton, no shell swap.

### 3.3 `tm-boot.js`: an expired session returns to the last tenant (spec 0024 §4.2)

The sign-in loop guard grows into the record of who chose the target.

```ts
interface TmLoginMark {     // sessionStorage 'tm.login'; written by step 3d and TmSession.signIn
  at: number;               // Date.now()
  expired: boolean;         // the session expired (telemetry, 0024 §8.6)
  returnUrl: string;        // what /bff/login received
  auto: boolean;            // the platform chose returnUrl: '/' or the rebuilt '/{lastTenantId}'
  sub: string | null;       // whose session this was: the expired record's sub, or the signed-in user's
}
```

- **Step 3c** copies `record.sub` and `record.lastTenantId` into locals before the expiry delete
  nulls them.
- **Step 3d** builds `returnUrl`:
  - path `/` with a last tenant → `/{lastTenantId}`, `auto: true`;
  - path `/` without one → `/`, `auto: true` (the BFF may pick, §3.2);
  - any other path → the path and query, `auto: false`.
- **`TmSession.signIn`** writes the same mark: `auto` is true only for `returnUrl: '/'` (the
  signed-out page's Sign in, 0027 §6.3).
- **Step 4** (a valid cookie) reads the mark before removing it, and sets `__tmBoot.auto = t` when
  the path is under `/{t}` and either:
  - `auto` is true and the path is exactly `/{t}`; or
  - `sub` is set and differs from the profile's `sub`: another user chose this tenant.
- **The warm `/` rewrite** to `/{lastTenantId}` also sets `__tmBoot.auto`.

**Example —** Farah returns after nine days to the installed app, which opens `/`. Step 3c deletes
her data, and 3d sends `/bff/login?returnUrl=%2F17&ui_locales=ar` with `auto: true`. After sign-in
she lands on `/17`, a cold entry (0024 §4.4), not on the picker.

### 3.4 The mark in the app: `TmLaunch` (spec 0024 §9.3)

```ts
declare class TmLaunch {                     // @tellma/core primary; seeded from __tmBoot.auto
  readonly automatic: Signal<number | null>; // the tenant this document entered without the user choosing it
  markAutomatic(tenantId: number): void;     // TmLaunchRedirect, when it replaces '/' with a tenant
  settle(tenantId: number): void;            // TmApi, on the first 2xx from that tenant: the entry worked
  takeAutomatic(tenantId: number): boolean;  // TmErrorPresenter on tenant-not-found: true once, then cleared
}
```

| Where the mark lives | How long | Who consumes it |
|---|---|---|
| `tm.login` in sessionStorage (this tab) | across the sign-in round trip; removed by the next boot's step 4 | `tm-boot.js` step 4 |
| `__tmBoot.auto` | one document load | `TmLaunch` at its first injection |
| `TmLaunch.automatic` (memory) | until the first 2xx from that tenant, a route into another tenant, or a 404 | `TmErrorPresenter` |

- **On `404 tenant-not-found`**, `TmErrorPresenter` sweeps the tenant and clears a matching
  `lastTenantId`, as today (0024 §11). Then:
  - `takeAutomatic(t)` true → replace to `TmUrls.picker()`, with no notice;
  - otherwise → replace to `TmUrls.picker({ notice: 'tenant-not-found', from: t })`.
- **Why the picker, not `/`.** `/` would re-run the single-tenant rule on a possibly stale hint,
  which can point at the same tenant again: a loop. The picker never redirects (Review flag R7).
- **Telemetry:** `tm.launch.tenant-gone { automatic }`.

### 3.5 The flows

| Case | Lands on | Automatic |
|---|---|---|
| Warm launch at `/`, `lastTenantId` 17 | `/17` by `tm-boot.js`'s rewrite | yes |
| Cold launch at `/`, valid cookie, no last tenant | `TmLaunchRedirect`: one live tenant → `/1`; else `/tenants` | yes |
| Expired at `/`, last tenant 17 | sign-in, then `/17` | yes |
| Expired at `/`, no last tenant, one live tenant | sign-in; the BFF picks `/1` | yes |
| Expired at `/17/centers` | sign-in, then `/17/centers` | no; yes when another user signs in |
| Shared device: Sara signs in after Farah's session expired at `/` | `/17` (Farah's last tenant) when Sara is a member; else the picker, silently | yes |
| A followed link `/99` (email, pin, typed URL) or a reload | `/99`; a 404 shows the notice | no |
| Signed-out page, Sign in | `/`; the BFF may pick | when it picks |

- **Cold entry (0024 §4.4).** The shell holds its skeleton during the cold sync; a 404 on an
  automatic entry swaps it for the picker, with no notice. A warm entry's 404 (leader sync or page
  read) is handled the same; the picker's badges ("Deactivated") explain a removed membership.
- **Sign-out forgets.** An explicit sign-out still clears `lastTenantId` (0025 §2.6), because the
  device may change hands. Only expiry returns to the last tenant.

### 3.6 Tests

- **Vectors** (§3.1) in both suites. **BFF:** one live → `/1`; two or sandbox only → `/`;
  `/tenants` and deep links untouched; a directory failure keeps `/`.
- **`tmBoot(env)`:** 3d's three cases, step 4's two cases, the warm rewrite. **`TmLaunch`:** each
  way the mark ends.
- **e2e (Acme):** expired at `/` with one live tenant → `/1`, no tenantless skeleton; expired with
  a last tenant among two → that tenant; a shared device whose second user is not in it → the
  picker without a notice; a typed `/99` → the notice.

## 4. Edit plan

"§n.m" alone points into this note. Every new name is defined above.

**0010 Part D**
- §D.4: §2.2's sketch, its three mode bullets and golden rows; the example adds "20/10/2026, 10:42
  GMT+3". Requested by 0024 §7.1, 0029 §12.2. Budget `./l10n` ≈ +0.4 KB. Testing: the `zoneLabel`
  array. Decisions: "Offset labels, never abbreviations, compared at the instant".

**0011**
- §1.3 and §2.2: `Languages(["en-GB", "ar-SA"])`; a code may carry the default region (0013 §7.3).
- §3.2 last paragraph, §3.8 Readers: the post-sign-in landing (§5.5) also reads the directory.
- §4.1: `Culture` is "language and region, no extensions"; `CultureInfo` is the Gregorian clone of
  `Culture` + `-u-nu-latn`; the table row cites 0013 §9.2 for the region.
- §5.5 `/bff/login`: §3.2's bullets and `TenantLanding` replace "the SPA's launch redirect decides".
- §10 Sessions: §3.6's BFF cases. Decision 29 → "The BFF picks a tenant only for a sign-in to `/`,
  by the SPA's rule; one vector file keeps both in step".

**0013**
- §1.1: `DigitStyle`, `LanguageFormats`. §1.2: `languages` carries §1.5's members. §1.3: each
  declared region is in its language's `Regions`.
- §5.1: the typed row adds the tenant's formats. §5.2: `Region`, `Digits` after `TimeZone`. §5.6:
  both on `TenantSettings` and `TenantSettingsForClient`. §6.1: both on `TenantSettingsPatch`.
  §6.5: `Settings.RegionNotOffered`.
- §7.1: §1.3's sketch. §7.2: §1.3's regions, native digits and default regions (prose; data in
  code). §7.3: regioned codes in `Languages`.
- §9.2: `NegotiationInput.PreferredRegion`; the Culture row takes §1.6's region steps; §1.6's
  server-digits bullet replaces the "regioned `PreferredLanguage`" sentence; `CultureInfo` from
  `<lang>-<RR>-u-nu-latn`. §9.3: the initializer reads both regions.
- §12: every pair passes `predefinedOnly: true` on Ubuntu and Windows; `nu-latn` formatting;
  negotiation rows per region step. Review flag 7 → R3; add R2. Decisions: "Region is its own
  axis".

**0014**
- §3.1: §1.2's properties and columns; the Preferences bullet. §7.1 `UserProfile`, §7.3 result
  set 3 and §7.8 add `PreferredRegion` and `PreferredDigits`.

**0016**
- §3.4 `UserProfileView` gains both. §6.1 `Accept-Language` row: "the SPA sends its locale
  (`ar-SA-u-nu-latn`); language and region pick the culture"; precedence adds the two regions.

**0018**
- §1.2's example: `Languages(["en-GB", "ar-SA"])`. §3.1: §1.7's three records.
- §3.2: §1.7's preprocessing and rows; `me/locale` applies the time-zone row at `timeZone`.
- §3.5: "eleven" → "thirteen"; `SetMyLocale`'s callers add the zone prompt and update (0024 §7.8);
  its `SELECT` adds `[PreferredTimeZone]`.

**0023**
- §4.5: the tooltip is labelled under `'auto'`.

**0024**
- §1 import rules: the dynamic imports add the zone dialog's chunk. §2.2, §14:
  `TmLanguageDefinition` loses `locale`; `tmEnglish()`, `tmArabic()`. §3.1: the `languages` comment.
- §4.2: `TmLoginMark`; 3c keeps `sub` and `lastTenantId`; 3d's `returnUrl` rule; step 4 and the
  warm rewrite set `__tmBoot.auto`; §3.3's example beside the deep-link one.
- §4.3: a sign-in at `/` by a single-tenant user has one wait. §4.4 Cold entry: an automatic
  `tenant-not-found` goes to the picker without a notice.
- §7.1: `region`, `digits`, `deviceTimeZone` and the `locale` rule. §7.2: a region or digits change
  applies as step 2 with no load. New §7.7 "Regional formats" (§1.4, §1.9). New §7.8 "The device
  time zone" (§2.1, §2.3–§2.6).
- §8.3: `signIn` writes `TmLoginMark`. §9.3: §3.1's TS rule and vectors, §3.4, §3.5; both replaces
  mark the entry; the BFF sentence. §10.3 step 5: `TmLaunch.settle`. §10.2: the `ar-SA-u-nu-latn`
  example. §11: the two `tenant-not-found` branches.
- §13: §2.7, §3.6, and `regional-formats.spec` (Acme `ar` + `Latin` shows "1,234,567.89" and a
  Sunday-first picker; a My profile save re-renders every tab with one request); the cross-engine
  pair test with its gap list. §14: the new telemetry.
- Decisions: the BFF landing; region and digits as preferences with tenant defaults; the zone prompt
  on change with a per-device option; the silent automatic fallback. Review flags: delete 7(b);
  rewrite 8(b) and 8(d); add R1, R4–R9.

**0025**
- §2.1: the device partition "survives sign-out; holds only `zone`". §2.3: the `zone` row. §2.4:
  no idle rule for it. §2.6: no sweep touches it. §5.3 `locale` and §9.5: `PreferredTimeZone`.
  §14: `zone` survives `session-ended` and the expiry delete; the 20-user cap.

**0026**
- §9.1: an instant cell is ISO 8601 in the display offset (`2026-10-20T10:42:00+03:00`). §9.7: the
  context adds the locale and both zones. §10.1 `locale`: `region`, `digits`, `deviceTimeZone`,
  `zoneAuto`.

**0027**
- §6.2: the notice shows only for an entry the user chose. §7.5: the `tenant-not-found` row says
  the same. §6.3: Sign in's `'/'` lets the BFF land a single-tenant user.
- §10.1: "Region and digits are edited on My profile". §12.1: the zone prompt and update also call
  `users/me/locale`. §13: the picker harness case "automatic, no notice".

**0028**
- §8.1 outcomes: "Time zone changed automatically" | "Times now show in {zone} ({offset})." |
  "Change" → My profile. §9.3: export the gate as `TmInputIdle.whenIdle()` for the zone dialog.

**0029**
- §5.2: the golden's `ar-SA` digit row is client-only (server text uses Latin digits).
- §12.1: `TmFormats.zoneSuffix(of): Signal<string>`, " (GMT+3)" for an `instant` while
  `zoneMismatch()`; `cell` formats instants in `'column'` mode. §12.2 `instant` row: labelled per
  0010 §D.4.

**0030**
- §7.1 Headers: `header(col)` appends `zoneSuffix(col)`. 0034's report grids do the same.

**0032**
- §4.1: five locale fields. §4.2: `tm-region-field`, `tm-digits-field` and the two codes (§1.8).
- §7.5: the *Formats* card. §8: "thirteen" members; `<tm-auto-time-zone />` in *Regional* (§2.5).
- §12.1, §12.2: the new members. §13: harness cases for the two fields and the option.

**ARCHITECTURE.md** (misalignment)
- The Locale dimension lists "ArAe". Add: "Regional formats (region, digits) are Core's, from
  CLDR; a Locale pack never carries them", as for calendars.

## 5. Review flags

| # | The call (§) | Alternative | What would flip it |
|---|---|---|---|
| R1 | Region and digits only on My profile (§1.8) | a "Format" row in the session menu | users switching formats often |
| R2 | Offered regions: every .NET pair, frozen in code (§1.3) | a short market list, or the host's ICU at startup | users lost in 104 English regions; a host whose ICU lacks a pair |
| R3 | Server text always uses Latin digits (§1.6) | substitute native digits after formatting | `Native` users objecting to Latin digits in emails |
| R4 | The zone prompt is a dialog (§2.4) | a level-5 banner whose action opens the dialog | users finding the dialog intrusive after travel |
| R5 | `seen` per tenant, so a multi-tenant user is asked once per tenant (§2.3) | one `seen` per user | multi-tenant users finding repeated prompts noisy |
| R6 | The device partition keeps a `sub`, a flag and zones after sign-out (§2.3) | the user partition (forgotten at sign-out), or a hashed `sub` | a privacy review |
| R7 | The silent fallback goes to `/tenants`, not `/` (§3.4) | `/`, re-running the single-tenant rule | single-tenant users landing on the picker; `/` needs a loop guard |
| R8 | A path chosen by another user's session counts as automatic (§3.3) | the notice for every deep link | support confusion on shared devices |
| R9 | Chromium's missing `om`, `ti`, `so` data falls back to the default language's locale (§1.4) | ship ICU data for them | an Oromo-first distribution |
