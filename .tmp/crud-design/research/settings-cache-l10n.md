# Research: Tenant settings, localization, and the version cache (`settings-cache-l10n`, future spec 0012)

Verification date for everything below: **2026-09-01**. Two kinds of evidence are used:

- **Web sources** (primary sources first: Microsoft Learn, dotnet/extensions and dotnet/runtime source, NuGet, RFCs, vendor docs).
- **An empirical probe** run on this machine: .NET SDK 10.0.400, runtime 10.0.11, Windows 11 build 26200, ICU globalization mode (`UseNls=false`, `Invariant=false`, the docs' ICU-detection snippet returned `true`), package `MessageFormat` 8.0.0 (assembly `Jeffijoe.MessageFormat 8.0.0.0`, net10.0 build). The probe is a throwaway .NET 10 file-based app (`probe.cs`, `probe2.cs`) in the session scratchpad, outside the repo; raw output is quoted in the appendix. Linux was not probed; every "verified" globalization fact below is verified on Windows/ICU and stated to hold on Linux only where the docs say so.

Legend: **[Verified]** = observed in a primary source or in the probe. **[Inference]** = the researcher's reasoning from verified facts; treat as a design input, not a fact. **[Unverified]** = reported by a secondary source and not independently confirmed.

---

## 1. HybridCache in .NET 10 versus `IMemoryCache` with `SizeLimit`

### 1.1 Package and version line

- **[Verified]** `Microsoft.Extensions.Caching.Hybrid` latest stable is **10.9.0, released 2026-08-11**; it ships on the monthly dotnet/extensions train (10.8.0 2026-07-14, 10.7.0 2026-06-09, …, 10.0.0 2025-11-11). GA was **9.3.0 on 2025-03-11** (first non-preview; 9.2.0-preview.1 was 2025-02-11). Targets net8.0 / netstandard2.0 / net462; 10.9.0 depends on `Microsoft.Extensions.Caching.Abstractions`, `.Caching.Memory`, `.Logging.Abstractions`, `.Options` ≥ 10.0.11. `Microsoft.Extensions.Caching.Memory` latest is **10.0.11 (2026-08-11)**.
  Sources: https://www.nuget.org/packages/Microsoft.Extensions.Caching.Hybrid ; https://www.nuget.org/packages/Microsoft.Extensions.Caching.Hybrid/9.3.0 ; https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory ; GA announcement https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga/ (2025-03-12).
- **Implication:** the repo's `Microsoft.Extensions.*` pins at 10.0.11 already match HybridCache 10.9.0's floor; adding it is a one-line `PackageVersion`.

### 1.2 L1-only use without a distributed backend

- **[Verified]** Docs: "even without an `IDistributedCache` implementation, the `HybridCache` service still provides in-process caching and stampede protection." Source (article dated 2026-07-28): https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0
- **[Verified]** Source code: L1 is the **DI-registered `IMemoryCache`** (`_localCache = services.GetRequiredService<IMemoryCache>();`), L2 is optional (`_backendCache = _options.DistributedCacheServiceKey is null ? services.GetService<IDistributedCache>() : services.GetRequiredKeyedService<IDistributedCache>(...)`). The docs' sentence "By default `HybridCache` uses `MemoryCache` (System.Runtime.Caching)" is wrong against the code. Source: https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Caching.Hybrid/Internal/DefaultHybridCache.cs
- **Implication:** HybridCache cannot be given a private, size-limited memory cache; it shares the process-wide `IMemoryCache` that MVC, Razor, and other libraries also use (see 1.5).

### 1.3 Stampede protection

- **[Verified]** "A `HybridCache` instance ensures that only one concurrent caller for a given key calls the factory method, and all other callers using the same instance wait for the result of that call. The cancellation token passed to the factory is canceled when all callers awaiting the shared operation have canceled. This coordination doesn't extend to other `HybridCache` instances, even if they use the same secondary distributed cache." (hybrid doc above). The EventSource counts `total-stampede-joins`.
- **[Verified]** `IMemoryCache.GetOrCreate` has **no** such protection: "Multiple requests might discover the cached key value is empty because the callback isn't finished. This approach can result in several threads repopulating the cached item." Source (article dated 2026-05-01): https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-10.0
- **Implication:** if the design picks `IMemoryCache`, it must add its own per-key single-flight (a `Lazy<Task<T>>` or per-key semaphore); if it picks HybridCache it gets this for free, per process only.

### 1.4 Tag-based invalidation — when added, how it works

- **[Verified]** Tags shipped **at GA (9.3.0, 2025-03-11)**: the GA post lists "cache invalidation with tags" among the shipped features; the design issue "HybridCache - tags and invalidation" (dotnet/extensions#7098) was opened 2024-04-23. Sources: https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga/ ; https://github.com/dotnet/extensions/issues/7098
- **[Verified]** Mechanism is **logical, not physical**: "Calling `RemoveByTagAsync` doesn't remove values from either the local or distributed cache. Instead, it establishes an 'ignore anything created before this point' rule for entries associated with that tag." Old values stay until they expire. Implementation: `ConcurrentDictionary<string, Task<long>> _tagInvalidationTimes` keyed by tag; every entry carries `CreationTimestamp`; on read `IsTagExpired` compares `timestamp <= pending.Result`; a pending (not yet loaded) tag time is treated as invalid; wildcard `*` uses `_globalInvalidateTimestamp` and, with no L2, clears the tag dictionary. Glob matches are not supported; tags cannot be empty or `*`; "large sets of tags might negatively impact performance". Sources: hybrid doc above; https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Caching.Hybrid/Internal/DefaultHybridCache.TagInvalidation.cs
- **[Verified]** Cross-instance: "When invalidating cache entries by key or by tags, they're invalidated in the current server and in the secondary out-of-process storage. However, the in-memory cache in other servers isn't affected." (hybrid doc).
- **Implication:** HybridCache's tag model is itself a timestamp-stamp scheme, but the stamps are **process-local**. A tag read from the database on every request cannot be fed into HybridCache's own validity check; the DB stamp would have to be folded into the key (`settings:{tenantId}:{stamp}`) or compared by our code after `GetOrCreateAsync` returns. HybridCache therefore adds little for the brain dump's "compare DB tag on every call" design beyond stampede protection.

### 1.5 Size limits, and the `SizeLimit` interaction

- **[Verified]** `HybridCacheOptions`: `MaximumPayloadBytes` default **1 MB** ("Attempts to store values over this size are logged, and the value isn't stored in cache"), `MaximumKeyLength` default **1024** chars (longer keys bypass the cache), `DefaultEntryOptions` (`Expiration`, `LocalCacheExpiration`), `DisableCompression`, `ReportTagMetrics`, `DistributedCacheServiceKey`. Sources: hybrid doc; https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.hybrid.hybridcacheoptions
- **[Verified]** `IMemoryCache` `SizeLimit` is unitless; when set, **every** entry must set `Size` or the add throws; the runtime never trims on memory pressure; compaction order is expired → lowest priority → least recently used → earliest absolute expiration → earliest sliding expiration; entries with `NeverRemove` are never removed; expiration is not scanned in the background (any cache activity may trigger a scan). Docs warn: "If you use a shared memory cache from dependency injection and also use `SetSize`, `Size`, and `SizeLimit` to limit the cache size, the app can fail … To limit the cache size … create a cache singleton for caching." (memory doc above).
- **[Verified]** HybridCache and a size-limited DI `IMemoryCache`: the original failure ("Cache entry must specify a value for Size when SizeLimit is set", dotnet/aspnetcore discussion #57582, 2024-08-30) was fixed by dotnet/extensions **PR #5420 "HybridCache: ensure that Size is always specified in L1", merged 2024-09-18**. Current `SetL1` sets `cacheEntry.SetSize(size)` when `value.TryGetSize(out size)` succeeds; the size is the **serialized payload length in bytes** (`MutableCacheItem.TryGetSize` → `_buffer.Length`; `ImmutableCacheItem.Size` set from the serialized buffer). Sources: https://github.com/dotnet/aspnetcore/discussions/57582 ; https://github.com/dotnet/extensions/pull/5420 ; https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Caching.Hybrid/Internal/DefaultHybridCache.L2.cs (method `SetL1`, lines 179–204 on main at verification time).
- **Implication [Inference]:** a `SizeLimit` on the DI `IMemoryCache` is still unsafe because the framework's other consumers (MVC/Razor caches, `ResponseCaching`, third-party middleware) do not set `Size`; HybridCache can live under a limit only if the whole process does. The brain dump's "bounded LRU per cache" is naturally a **private `MemoryCache` per cache kind**, which HybridCache cannot use.

### 1.6 Serialization of custom types

- **[Verified]** "By default, the library handles `string` and `byte[]` internally, and uses `System.Text.Json` for everything else"; custom `IHybridCacheSerializer<T>` / `IHybridCacheSerializerFactory` via `AddSerializer` / `AddSerializerFactory`; Native AOT needs source-generated serializers; a type marked `sealed` + `[ImmutableObject(true)]` is handed out as the same instance instead of being deserialized per call (hybrid doc).
- **[Verified]** Source: HybridCache **serializes on every write even with no L2** — the only case that skips serialization is an immutable type with both L1 and L2 writes disabled (`bool skipSerialize = cacheItem is ImmutableCacheItem<T> && (activeFlags & FlagsDisableL1AndL2Write) == FlagsDisableL1AndL2Write;`, comment: "the only scenario in which we *do not* need to serialize is if it is an ImmutableCacheItem … "). Mutable types are stored in L1 as bytes and **deserialized on every read** (defensive copy); immutable types are stored as the object but still serialized once for the size/quota check. Source: https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Caching.Hybrid/Internal/DefaultHybridCache.StampedeStateT.cs (lines ~321–398 on main).
- **Implication [Inference]:** for the per-tenant caches in scope (settings DTO, permissions, user preferences, small entity lists) HybridCache costs a System.Text.Json round trip per write and, for non-immutable types, per read; entity classes with EF navigation properties may not serialize (cycles) without a DTO or a custom serializer. The `MaximumPayloadBytes` quota also applies to L1-only use, so a large cacheable-entity list (>1 MB serialized) is silently not cached and logged.

### 1.7 Observability

- **[Verified]** HybridCache exposes an `EventSource` named `Microsoft-Extensions-HybridCache` with counters `total-local-cache-hits`, `total-local-cache-misses`, `total-distributed-cache-hits`, `total-distributed-cache-misses`, `total-data-query`, `current-data-query`, `current-distributed-cache-fetches`, `total-local-cache-writes`, `total-distributed-cache-writes`, `total-stampede-joins`, `total-tag-invalidations`. A grep of `DefaultHybridCache.cs` on main found no `System.Diagnostics.Metrics` `Meter`; `ReportTagMetrics` is documented as "use tags data as dimensions on metric reporting". Source: https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.Caching.Hybrid/Internal/HybridCacheEventSource.cs
- **Implication [Inference]:** EventCounters are not OpenTelemetry `Meter` instruments; the repo convention (`IMeterFactory` meter per package, `tellma.<area>.<name>` instruments, constants in `.Abstractions`) would have to be layered on top in either choice, so observability is not a differentiator.

### 1.8 Recommendation for an in-process per-tenant cache validated by DB-read tags

**[Inference, medium confidence]** Prefer a thin **`IMemoryCache`-based store owned by the platform** over HybridCache for the version-tag caches, because the requirements the brain dump states (bounded LRU per cache kind, stampede safety, meters, a tag compared on every request, a hardcoded format version) map onto:

- one **private `MemoryCache` instance per cache kind** with `SizeLimit` in *entry count* units (`Size = 1`) — the only way to bound each cache independently without endangering the shared DI cache (1.5);
- entries stored as an immutable record `(Tag, FormatVersion, Value)`; the request's DB-read tag is compared to the stored tag and a mismatch is a miss — the Rails "recyclable cache key" pattern (6.1);
- a per-key single-flight guard (`ConcurrentDictionary<key, Lazy<Task<T>>>`) for stampede safety (1.3);
- `IMeterFactory` counters for hits/misses/evictions/size per cache kind.

HybridCache remains the right tool where its strengths matter — TTL-driven caching of serializable data, a future L2, stampede protection out of the box — but for this theme it forces serialization of every value (1.6), shares the DI memory cache (1.2/1.5), and its tag stamps cannot be validated against a database value (1.4). The judge should weigh the ~100 lines of platform code against those constraints; both choices are defensible, so this is a **review flag**.

---

## 2. The `MessageFormat` NuGet package (ICU MessageFormat for .NET)

### 2.1 Identity, version, maintenance

- **[Verified]** NuGet `MessageFormat`: owner **jeffijoe** (Jeff Hansen), repo https://github.com/jeffijoe/messageformat.net , MIT license (README). Latest stable **8.0.0 released 2026-02-23**; previous 7.1.3 (2025-03-03), 7.1.2 (2024-10-14), 7.1.1 (2024-09-28), 7.1.0 (2023-10-11), 7.0.0 (2023-08-22); ~3.5 M total downloads. Targets **netstandard2.0, netstandard2.1, net8.0, net10.0**; no dependencies on net8/net10 (System.Memory on netstandard2.0). Assembly name is `Jeffijoe.MessageFormat`. Sources: https://www.nuget.org/packages/MessageFormat ; https://github.com/jeffijoe/messageformat.net ; https://github.com/jeffijoe/messageformat.net/releases
- **[Unverified]** The fetched summary of the GitHub releases page showed the same versions dated exactly one year earlier (8.0.0 "Feb 23, 2025"); NuGet's dates are taken as authoritative.
- **[Verified — release notes for 8.0.0]** breaking: string `locale` replaced by `CultureInfo` (`MessageFormatter.Culture` read-only), pluralizer dictionary split into `CardinalPluralizers` and `OrdinalPluralizers`, net6.0 target dropped and net10.0 added; new: **full `selectordinal`** from auto-generated CLDR ordinal rules, **CLDR 48.1** plural data, partial LDML locale inheritance for plural lookup, per-call `culture` override on `FormatMessage`; internal object pool replaced `Microsoft.Extensions.ObjectPool`. Source: releases page above.
- **Implication:** the pinned 8.0.0 is the current release, actively maintained by a single author; the repo comment "the only maintained .NET implementation" is consistent with what was found, but bus factor is one.

### 2.2 Supported syntax (verified by probe on 8.0.0)

- **[Verified]** `plural` with `offset:` and exact matches (`=0`, `=1`), all six CLDR categories for Arabic (`0→zero, 1→one, 2→two, 3→few, 11→many, 100/101→other`), `select`, `selectordinal` (`1st 2nd 3rd 4th 11th 21st 22nd 23rd` for en), nesting (`select` containing `plural`), `#` substitution, `'` escaping (`It''s '{'not a var'}'` → `It's {not a var}`).
- **[Verified]** `number` styles: bare (`1,234,567.891`), `integer` (`1234568`), `currency` (`¤…` for neutral `en`, `$1,234.50` for `en-US`), `percent`; a custom pattern (`#,##0.00`) throws `UnsupportedFormatStyleException`. `date` styles: bare and `short` (`9/1/2026`), `full` (`Tuesday, September 1, 2026`); **`medium` and `long` throw `UnsupportedFormatStyleException`**; a custom pattern (`yyyy-MM-dd`) throws. `time`: bare/`medium` (`2:05:00 PM`), `short` (`2:05 PM`).
- **[Verified]** Date rendering follows the culture's `DateTimeFormat` including its calendar: with `ar-SA` (default calendar UmAlQura) `{d, date, short}` → `19/3/1448 بعد الهجرة`; `{x, number}` → `1٬234٫5` (Arabic separators). Per-call culture override works and changes the plural category (`shared en formatter, override ar, n=2 → two`).
- **[Verified]** A `CustomValueFormatter` hook exists (`TryFormatDate(CultureInfo, object, string style, out string)`, `TryFormatTime(...)`, `TryFormatNumber(...)`), injectable through the constructor.
- **[Verified]** Errors: missing argument → `VariableNotFoundException`; unbalanced braces → `UnbalancedBracesException`; unsupported style → `UnsupportedFormatStyleException`. The identity server's code comment states these do not share `MessageFormatterException` as a base (not independently re-verified here).
- **Implication:** plural/select/selectordinal/nesting cover the brain dump's needs; **calendar-aware dates inside messages must go through `CustomValueFormatter` or be pre-formatted by our own calendar code** (see §4), because the built-in `date` styles are limited to `short`/`full` and rely on `DateTimeFormatInfo`, which cannot carry an Ethiopian calendar.

### 2.3 Plural-rule source and locale fallback

- **[Verified]** Rules are generated from CLDR (48.1 in 8.0.0) for cardinal and ordinal; custom rules can be added via the `CardinalPluralizers`/`OrdinalPluralizers` dictionaries and take precedence for exact locale matches. Probe: `en-GB` inherits `en` rules; `am` gives `one` for 0 and 1 (correct per CLDR); an unknown culture (`zz`) does not throw and resolves to `other`.
- **Implication:** the Core language catalogue can rely on CLDR plural data shipped inside the package; adding a language needs no plural code.

### 2.4 API shape, caching, thread-safety

- **[Verified]** Constructor `MessageFormatter(bool useCache = true, CultureInfo? culture = null, CustomValueFormatter? customValueFormatter = null)`; `FormatMessage(string pattern, IReadOnlyDictionary<string, object?> args, CultureInfo? culture = null)`; static `Format(string pattern, object data, CultureInfo? culture = null)` uses reflection, carries `RequiresUnreferencedCode`, and its source comment says "Do not use in a tight loop, as a lock is being used to ensure thread safety" (a static lock on a singleton). Public members on 8.0.0: `Culture`, `CustomValueFormatter`, `Formatters`, `CardinalPluralizers`, `OrdinalPluralizers`, `Format`, `FormatMessage`.
- **[Verified]** The pattern cache is `private readonly ConcurrentDictionary<string, IFormatterRequestCollection>? cache;` (instantiated only when `useCache` is true), with no locking around it. The README makes no explicit thread-safety statement. Probe: a single cached instance shared by 16 threads for 50 000 `FormatMessage` calls over 200 distinct patterns produced **0 errors**.
  Sources: https://github.com/jeffijoe/messageformat.net/blob/master/src/Jeffijoe.MessageFormat/MessageFormatter.cs ; README.
- **Implication [Inference]:** a **singleton `MessageFormatter` per process** (culture passed per call) is safe for concurrent `FormatMessage` use; the identity server's `ThreadLocal<MessageFormatter>` is unnecessary. Two cautions: the pluralizer dictionaries are mutable, so configure them before sharing; the cache is keyed by full pattern text and unbounded, which is fine for resource strings and unsafe for user-supplied patterns.

---

## 3. .NET 10 localization

### 3.1 `IStringLocalizer` with `.resx` versus JSON

- **[Verified]** The framework's `IStringLocalizer`/`ResourceManager` path reads **`.resx` only**; resource naming (`Controllers.HomeController.fr.resx`, dot or path style under `ResourcesPath`), `RootNamespaceAttribute`/`ResourceLocation` when assembly and root namespace differ, and culture fallback `Welcome.fr-CA.resx → Welcome.fr.resx → Welcome.resx` are documented; the docs recommend having no culture-less `.resx` so a missing string returns the key. Docs dated 2025-06-20; no .NET 10-specific change in this area was found. Source: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization/provide-resources?view=aspnetcore-10.0
- **[Verified]** JSON resources need a third-party localizer; the reference implementation is **`My.Extensions.Localization.Json` 4.0.0 (2025-12-10, MIT, net10.0, author hishamco — an ASP.NET localization docs author)**, ~772 K downloads; registered with `AddJsonLocalization` and `ResourcesPath`. Sources: https://www.nuget.org/packages/My.Extensions.Localization.Json ; https://github.com/hishamco/My.Extensions.Localization.Json
- **Implication:** staying on `.resx` keeps the built-in fallback chain, tooling and satellite-assembly packaging (3.2) with zero dependencies; JSON buys translator-friendly files at the cost of a small third-party package and losing satellite-assembly trimming.

### 3.2 Satellite-assembly deployment

- **[Verified]** Neutral-culture resources live in the main assembly; each other culture is a satellite assembly `<culture>/<Assembly>.resources.dll` beside the main assembly (hub-and-spoke fallback to the parent culture and then the neutral resources; `NeutralResourcesLanguageAttribute`). Source: https://learn.microsoft.com/en-us/dotnet/core/extensions/resources
- **[Verified]** The MSBuild property **`SatelliteResourceLanguages`** (e.g. `<SatelliteResourceLanguages>en-US;de-DE</SatelliteResourceLanguages>`) restricts which satellite assemblies — including those coming from referenced NuGet packages — are copied on build and publish. Source: https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#satelliteresourcelanguages
- **Implication [Inference]:** the "Core supports dozens of languages, a distro ships a subset" hierarchy is expressible as satellite assemblies in `Tellma.Core` plus a per-distribution `SatelliteResourceLanguages` — but the runtime must then also know the shipped subset (it cannot enumerate satellites cheaply), so a declared language catalogue in code is still needed for validation of tenant settings.

### 3.3 `RequestLocalizationMiddleware` culture providers

- **[Verified]** Default providers in order: `QueryStringRequestCultureProvider` (`culture`, `ui-culture`), `CookieRequestCultureProvider` (`.AspNetCore.Culture`, `c=…|uic=…`), `AcceptLanguageHeaderRequestCultureProvider`; first provider that yields a result wins; `DefaultRequestCulture` otherwise; custom providers via `AddInitialRequestCultureProvider(new CustomRequestCultureProvider(async ctx => …))`; `RouteDataRequestCultureProvider` exists; `ApplyCurrentCultureToResponseHeaders` emits `Content-Language`; `CultureInfoUseUserOverride` affects Windows only. Source (dated 2025-06-20, updated 2026-02-11): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization/select-language-culture?view=aspnetcore-10.0
- **[Verified]** `AcceptLanguageHeaderRequestCultureProvider.MaximumAcceptLanguageHeaderValuesToTry` defaults to **3**. Source: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.localization.acceptlanguageheaderrequestcultureprovider?view=aspnetcore-10.0
- **[Verified]** Matching: names are compared to `SupportedCultures` with `StringComparison.OrdinalIgnoreCase`; with `FallBackToParentCultures` the middleware creates the culture via `CultureInfo.GetCultureInfo(name)` and walks `Parent` up to `MaxCultureFallbackDepth = 5`, swallowing `CultureNotFoundException`. Source: https://github.com/dotnet/aspnetcore/blob/main/src/Middleware/Localization/src/RequestLocalizationMiddleware.cs
- **Implication:** a platform-owned `RequestCultureProvider` that reads the user's stored preference (and the tenant's allowed set) is the intended extension point; `Accept-Language` is a fallback with at most three candidates considered.

### 3.4 BCP 47 `-u-ca-` calendar extensions in `CultureInfo` (probe, .NET 10.0.11 / ICU on Windows)

- **[Verified]** `new CultureInfo("ar-SA-u-ca-islamic-umalqura")` **succeeds**; `Name` and `IetfLanguageTag` keep the extension; `LCID` = 4096 (custom); `GetCultureInfo(name, predefinedOnly: true)` also succeeds; **`Parent` is `ar`, not `ar-SA`** (fallback skips the region).
- **[Verified]** The calendar keyword **does not change the calendar object**: `ar-SA-u-ca-gregory` still reports `Calendar`/`DateTimeFormat.Calendar` = `UmAlQuraCalendar` and `OptionalCalendars` = [UmAlQura, Gregorian, Hijri]; `en-US-u-ca-islamic-umalqura` and `en-US-u-ca-ethiopic` stay `GregorianCalendar` with `OptionalCalendars` = [Gregorian]. But **ICU-derived patterns do follow the keyword**, producing mixed output: `ar-SA-u-ca-gregory` prints `19/3/1448` (Hijri year, era text dropped); `am-ET-u-ca-ethiopic` prints `01/09/2026 ዓ/ም` (Gregorian year with the Ethiopic era marker).
- **[Verified]** `-u-nu-arab` sets `NumberFormat.NativeDigits` but `DateTime.ToString`/number formatting still emit ASCII digits (`ar-AE-u-nu-arab` → `2026`). `-u-co-phonebk` is stripped from `Name` (`de-DE-u-co-phonebk` → `de-DE`), matching `CultureData.Icu.cs`: the collation extension is removed from `_sName`, other extensions are retained "to ensure differentiation between names with extensions and those without".
- **[Verified]** Why: the native shim's `GlobalizationNative_GetCalendars` calls `ucal_getKeywordValuesForLocale("calendar", locale, commonlyUsed=true)` and maps ICU names `gregorian, japanese, buddhist, hebrew, dangi, persian, islamic, islamic-umalqura, roc` to `CalendarId`; anything else (including `ethiopic`) falls to Gregorian. Sources: https://github.com/dotnet/runtime/blob/main/src/native/libs/System.Globalization.Native/pal_calendarData.c ; https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Globalization/CultureData.Icu.cs
- **[Verified]** RFC 4647 §2.1 language-range ABNF (`1*8ALPHA *("-" 1*8alphanum)`) syntactically admits `-u-ca-islamic-umalqura` in `Accept-Language`, but §4.1 advises against extension/private-use subtags in ranges because they interfere with matching. Source: https://www.rfc-editor.org/rfc/rfc4647#section-2.1
- **Implication:** **never feed `-u-` names into `CultureInfo` or `RequestLocalization`**: they create custom cultures whose calendar does not change, whose formatting is internally inconsistent, and whose parent fallback skips the region. Carry calendar (and numbering) as separate request context values and strip extensions before culture negotiation.

### 3.5 ICU availability, invariant mode

- **[Verified]** .NET 7+ loads Windows' `icu.dll` on Windows 10 1703+/Server 2019+ and falls back to NLS where ICU is missing; on Linux the system `libicu` is required (Microsoft's Linux Docker images install it); app-local ICU via `System.Globalization.AppLocalIcu` + `Microsoft.ICU.ICU4C.Runtime` pins the CLDR version across deployments; `DOTNET_ICU_VERSION_OVERRIDE` selects a Linux ICU version (renamed from `CLR_ICU_VERSION_OVERRIDE` in .NET 10). Source (updated 2026-03-30): https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization-icu
- **[Verified]** Globalization-invariant mode: with `PredefinedCulturesOnly` (default true) "creation of any culture except the invariant culture is disallowed"; casing is ASCII-only, comparisons ordinal; IANA↔Windows time-zone conversion fails (3.5/5.1). Source: https://github.com/dotnet/runtime/blob/main/docs/design/features/globalization-invariant-mode.md
- **Implication:** the platform must **require ICU mode** (a startup check using the docs' `SortVersion` snippet is cheap) and should consider app-local ICU so Hijri/Arabic formatting does not drift with the host OS's CLDR.

---

## 4. Calendars in .NET 10 with ICU

### 4.1 What `System.Globalization` ships

- **[Verified — probe]** Non-abstract `Calendar` subclasses in CoreLib: `ChineseLunisolarCalendar, GregorianCalendar, HebrewCalendar, HijriCalendar, JapaneseCalendar, JapaneseLunisolarCalendar, JulianCalendar, KoreanCalendar, KoreanLunisolarCalendar, PersianCalendar, TaiwanCalendar, TaiwanLunisolarCalendar, ThaiBuddhistCalendar, UmAlQuraCalendar`. **No Ethiopic or Coptic calendar exists.**

### 4.2 `UmAlQuraCalendar`

- **[Verified]** Supported range **1900-04-30 … 2077-11-16 (Gregorian) = AH 1318 … 1500** (docs and probe agree); table-based algorithm licensed from the Saudi government; no `HijriAdjustment`. Source (updated 2026-08-13): https://learn.microsoft.com/en-us/dotnet/api/system.globalization.umalquracalendar?view=net-10.0
- **[Verified — probe]** `ar-SA` default calendar is UmAlQura (`OptionalCalendars` = [UmAlQura, Gregorian, Hijri]); `ar-AE` = [Gregorian, UmAlQura, Hijri]; `ar-EG` = [Gregorian, Hijri]. Formatting `2026-09-01` with `ar-SA`+UmAlQura gives `19/3/1448 بعد الهجرة`, and `"yyyy-MM-dd"` gives `1448-03-19`.
- **[Verified — probe]** `DateTimeFormatInfo.Calendar` accepts only calendars in the culture's `OptionalCalendars`: assigning `UmAlQuraCalendar` to `en-US` throws `ArgumentOutOfRangeException: Not a valid calendar for the given culture`.
- **Implication:** BCL formatting can render Hijri dates only under Arabic cultures; an English-language UI showing Umm Al-Qura dates (a real KSA case) needs our own calendar-aware formatter. The AH 1500 ceiling (2077) must be handled explicitly for long-dated schedules (**[Inference]**: fall back to `HijriCalendar` arithmetic or reject the date with a diagnostic).

### 4.3 `HijriCalendar`

- **[Verified]** Range 0622-07-18 … 9999-12-31 (AH 1 … 9666 per probe); one era; `HijriAdjustment` (0…±2 days) is read from the Windows registry (`HKEY_CURRENT_USER\Control Panel\International\AddHijriDate`) when not set explicitly. Source: https://learn.microsoft.com/en-us/dotnet/api/system.globalization.hijricalendar?view=net-10.0
- **Implication:** not suitable as the Saudi calendar (tabular, not Umm Al-Qura); usable only as an arithmetic fallback beyond 2077 with `HijriAdjustment` pinned to 0.

### 4.4 Ethiopian (Ge'ez) calendar

- **[Verified — probe]** `am-ET` has `Calendar` = Gregorian and `OptionalCalendars` = [Gregorian]; `am-ET-u-ca-ethiopic` does not add one (3.4). ICU/CLDR know `ethiopic` and `ethiopic-amete-alem`, but .NET's native mapping does not map them (4.1 source list in 3.4).
- **[Verified — probe]** A custom `Calendar` subclass **cannot be assigned to `DateTimeFormatInfo.Calendar`** on any culture (`en-US`, `am-ET`, `ar-SA` all throw `ArgumentOutOfRangeException`), so `DateTime.ToString(format, culture)` can never render Ethiopian dates; a custom `Calendar` works only for direct arithmetic (`GetYear`, `GetMonth`, …).
- **[Verified]** Prior art, Ahmad's own: `tellma-ltd/tellma` → `Tellma.Utilities.Calendars/EthiopianCalendar.cs`: `public class EthiopianCalendar : Calendar`, Julian-day-number conversion citing http://www.geez.org/Calendars/ , range `1752-09-14 … 2500-01-01`, single era (Amete Mihret), 13 months (Pagume 5/6 days), leap rule `year % 4 == 3`; `Calendars.cs` defines codes `"gc"` (Gregorian), `"et"` (Ethiopian), `"uq"` (Umm Al Qura) and `SupportedCalendars`. Sources: https://github.com/tellma-ltd/tellma/blob/master/Tellma.Utilities.Calendars/EthiopianCalendar.cs ; https://github.com/tellma-ltd/tellma/blob/master/Tellma.Utilities.Calendars/Calendars.cs
- **[Verified]** Third-party NuGet options are small hobby packages: `EthiopianCalendar` 2.1.2 (2026-01-05, Baakal Tesfaye, netstandard2.0, no dependencies, ~55 K downloads, license not shown on NuGet), `EthiopianDateConverter`, `EthiopianCalendarConverter`. Sources: https://www.nuget.org/packages/EthiopianCalendar ; https://www.nuget.org/packages/EthiopianDateConverter ; https://www.nuget.org/packages/EthiopianCalendarConverter
- **Implication:** the platform must own an Ethiopian `Calendar` **and its own date-pattern renderer** (month names, era, digits) in the Locale dimension; porting the existing Tellma implementation (MIT-licensed repo, same author) is the low-risk path, and the same renderer should serve Umm Al-Qura under non-Arabic UI cultures (4.2).

---

## 5. Time zones, request headers, and the browser Temporal API

### 5.1 `TimeZoneInfo` IANA / Windows ids on .NET 10

- **[Verified — docs]** Since .NET 6, `FindSystemTimeZoneById` accepts IANA ids on Windows and Windows ids on Unix **when ICU is available**; `TryConvertIanaIdToWindowsId(string, out string?)` and `TryConvertWindowsIdToIanaId(string[, string region], out string?)` are ICU-dependent and fail in NLS or invariant mode (app-local ICU is the remedy on old Windows). Sources: https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.tryconvertianaidtowindowsid?view=net-10.0 ; https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization-icu
- **[Verified — probe, Windows]** `FindSystemTimeZoneById("Asia/Riyadh")` → `Id = Asia/Riyadh`, `HasIanaId = true`, +03:00; `"Africa/Addis_Ababa"` likewise; `TryFindSystemTimeZoneById("asia/riyadh")` resolves case-insensitively to the canonical id; `"UTC"` and `"Etc/UTC"` both resolve (keeping their own ids); an unknown id returns false. Conversions: `Asia/Riyadh ↔ Arab Standard Time`; `("Arab Standard Time", "AE")` still yields `Asia/Riyadh` (no per-region entry); `("E. Africa Standard Time", "ET")` → `Africa/Addis_Ababa`.
- **Implication:** store **IANA ids** in tenant settings and user preferences, validate with `TryFindSystemTimeZoneById`, normalise to `TimeZoneInfo.Id`, and never run in invariant/NLS mode.

### 5.2 Is there a standard header or client hint for time zone or calendar?

- **[Verified]** **No.** The Client Hints registry (`Sec-CH-UA*`, `Sec-CH-Prefers-Color-Scheme`, `Sec-CH-Prefers-Reduced-Motion`, …) has no time-zone or calendar hint; the `Sec-CH-Lang`/`Sec-CH-Locale` proposal (WICG `lang-client-hint`, draft-west-lang-client-hint-00) was **archived on 2023-02-21** without shipping. Sources: https://github.com/WICG/lang-client-hint ; https://github.com/WICG/client-hints-infrastructure/blob/main/README.md ; https://github.com/WICG/ua-client-hints/blob/main/README.md
- **[Verified]** `Accept-Language` can syntactically carry `-u-ca-…` (RFC 4647, 3.4) but browsers do not send extensions and ASP.NET would mis-negotiate them (3.4).
- **[Verified — precedent]** GitHub's REST API accepts a **`Time-Zone` request header** with an Olson/IANA name (`Time-Zone: Europe/Amsterdam`) and documents the precedence: explicit ISO-8601 offset in the payload → `Time-Zone` header → the authenticated user's last known time zone → UTC. Source: https://docs.github.com/en/rest/using-the-rest-api/timezones-and-the-rest-api
- **Implication:** the platform defines its own request headers (calendar, time zone, optionally numbering system) and a documented precedence over user preference and tenant default, mirroring GitHub's shape; `Accept-Language` stays for the UI language only.

### 5.3 Browser Temporal API status (2026)

- **[Verified]** web-features explorer: Temporal is **"Limited availability"**; Chrome 144 (2026-01-13), Chrome Android 144, Edge 144 (2026-01-21), Firefox 139 (2025-05-27), Firefox Android 139; **Safari and Safari iOS: not supported** (WebKit bug 223166); "Baseline availability blocked since January 2026 by Safari". Source: https://web-platform-dx.github.io/web-features-explorer/features/temporal/
- **[Unverified]** Secondary sources report TC39 Stage 4 in March 2026 (ES2026) and Safari support only in Technology Preview behind a flag (https://socket.dev/blog/tc39-advances-temporal-to-stage-4 ; https://bryntum.com/blog/javascript-temporal-is-it-finally-here/).
- **Implication:** the client must not depend on native Temporal until Safari ships; the Angular date adapters need a polyfill (`@js-temporal/polyfill`) or their own calendar code for Umm Al-Qura/Ethiopic. Whether browser Temporal exposes `islamic-umalqura` and `ethiopic` calendars consistently was **not verified** here.

---

## 6. Version-tag / stamp-validated caches and a "cache format version"

### 6.1 Precedents for "opaque stamp stored with the entry, compared on every read"

- **[Verified]** Rails `ActiveSupport::Cache::Store` `:version` option ("recyclable cache keys"): "When reading from the cache, if the cached version does not match the requested version, the read will be treated as a cache miss." The version is stored with the entry; keys stay stable. `:namespace` prefixes every key (static or Proc). Source: https://api.rubyonrails.org/classes/ActiveSupport/Cache/Store.html
- **[Verified]** HybridCache's tags are the same family: per-tag invalidation timestamps compared to each entry's `CreationTimestamp` on read (1.4).
- **[Verified]** RFC 9110 §8.8.3: "An entity tag is an opaque validator for a selected representation of a resource"; `entity-tag = [ weak ] opaque-tag`; weak vs strong semantics; `Last-Modified` has one-second precision. Source: https://www.rfc-editor.org/rfc/rfc9110.html#name-etag
- **Implication [Inference]:** the brain dump's "version/etag/fingerprint" naming question has precedent on both sides: Rails/Django/HybridCache say **version** (and HybridCache internally uses timestamps); HTTP reserves **ETag** for representation validators, so "etag" is best kept for the blob endpoint (T7) and "version tag" or "stamp" for the cache. Whatever the name, the value should be opaque to consumers (compared for equality only), which lets it be a `uniqueidentifier` today and a monotonic `bigint` later without a contract change.

### 6.2 Precedents for a hardcoded format version that invalidates on deploy

- **[Verified]** Django: a **system-wide `VERSION`** setting combined into every key (`"%s:%s:%s" % (key_prefix, version, key)`), plus `KEY_PREFIX` explicitly to stop "data cached by one server [being] used by another server" when "the format of cached data is different between servers"; `incr_version`/`decr_version` per key. Source: https://docs.djangoproject.com/en/5.2/topics/cache/
- **[Verified]** HybridCache has no format-version concept; a changed serialized shape is only caught by `System.Text.Json` deserialization tolerance (1.6).
- **Implication [Inference]:** the "metaversion" is Django's `VERSION`: a `const` per cache kind (not one global) folded into the key or compared with the entry, bumped by hand when the cached DTO shape changes; because the in-process cache dies with the process, its real job is guarding an L2 or a client-side cache that outlives deploys, which argues for exposing the format version to the SPA alongside the tag.

### 6.3 Where the stamp can come from without application code (database-side alternatives)

- **[Verified]** SQL Server / Azure SQL Database **change tracking**: `CHANGE_TRACKING_CURRENT_VERSION()` "represents the version of the last committed transaction" (database-wide, monotonic); `CHANGETABLE(VERSION …)` gives a row's last version; must be enabled per database and per table; retention/cleanup (`CHANGE_RETENTION`) and snapshot-isolation guidance apply; supported on Azure SQL Database and Managed Instance; the docs also describe the backup-restore hazard (versions can go backwards after a restore) and recommend storing a "database version ID … updated whenever a database is recovered". Source: https://learn.microsoft.com/en-us/sql/relational-databases/track-changes/work-with-change-tracking-sql-server
- **Implication [Inference]:** change tracking can *audit* that every write path bumped a tag (compare `CHANGE_TRACKING_CURRENT_VERSION()` drift in tests) but is a poor primary tag: it is database-wide, needs per-table enablement and retention, and obtaining a per-table current version requires scanning the change table. The explicit tag table bumped by the emitter (every batch statement declares the tables it writes) remains the right primary mechanism; the restore hazard applies equally to any monotonic `bigint` stamp and favours an opaque `uniqueidentifier` regenerated on bump.

### 6.4 Multi-instance reality

- **[Verified]** Both `IMemoryCache` and HybridCache invalidation are per process ("the in-memory cache in other servers isn't affected", 1.4); the docs' only cross-instance answer is a distributed cache. Sources: 1.4, memory doc.
- **Implication:** reading the tenant's tags from the database on every request (or on the first business round trip, per the connect-collapse hint) is the only cross-instance freshness guarantee that needs no Redis or bus; the cache design should treat the stamp read as the invalidation channel and the in-process cache as a pure accelerator.

---

## Appendix A — probe environment and raw output (abridged)

Environment: `dotnet --version` = 10.0.400; `Environment.Version` = 10.0.11; Windows NT 10.0.26200; `GlobalizationMode.UseNls=False Invariant=False`; ICU check = True; `MessageFormat` 8.0.0 (`Jeffijoe.MessageFormat, Version=8.0.0.0`, TFM `.NETCoreApp,Version=v10.0`).

```
[ar-SA] Name=ar-SA LCID=1025 Parent=ar Calendar=UmAlQuraCalendar Optional=[UmAlQuraCalendar,GregorianCalendar,HijriCalendar] d=19‏‏/3‏‏/1448 بعد الهجرة
[ar-SA-u-ca-islamic-umalqura] Name=ar-SA-u-ca-islamic-umalqura LCID=4096 Parent=ar Calendar=UmAlQuraCalendar  (predefinedOnly OK)
[ar-SA-u-ca-gregory] Name=ar-SA-u-ca-gregory LCID=4096 Parent=ar Calendar=UmAlQuraCalendar d=19‏‏/3‏‏/1448 D=الثلاثاء، 19 ربيع الأول 1448
[en-US-u-ca-islamic-umalqura] Calendar=GregorianCalendar Optional=[GregorianCalendar] d=9/1/2026 AD
[am-ET] Calendar=GregorianCalendar Optional=[GregorianCalendar] d=01/09/2026
[am-ET-u-ca-ethiopic] Calendar=GregorianCalendar Optional=[GregorianCalendar] d=01/09/2026 ዓ/ም D=ማክሰኞ፣ 1 ሴፕቴምበር 2026 ዓ/ም
[ar-AE-u-nu-arab] Digits=[٠١٢٣٤٥٦٧٨٩] d=1‏‏/9‏‏/2026 ; ToString("yyyy") = 2026
[de-DE-u-co-phonebk] Name=de-DE
[ar-EG] Optional=[GregorianCalendar,HijriCalendar] ; [ar-AE] Optional=[GregorianCalendar,UmAlQuraCalendar,HijriCalendar]
Calendar subclasses in CoreLib: ChineseLunisolar, Gregorian, Hebrew, Hijri, Japanese, JapaneseLunisolar, Julian, Korean, KoreanLunisolar, Persian, Taiwan, TaiwanLunisolar, ThaiBuddhist, UmAlQura
UmAlQuraCalendar: Min=1900-04-30 Max=2077-11-16 MinYear=1318 MaxYear=1500
HijriCalendar:    Min=0622-07-18 Max=9999-12-31 MinYear=1 MaxYear=9666
en-US with DTF.Calendar=UmAlQura THROWS ArgumentOutOfRangeException: Not a valid calendar for the given culture.
[en-US]/[am-ET]/[ar-SA] custom Calendar subclass THROWS ArgumentOutOfRangeException: Not a valid calendar for the given culture.
ar-SA UmAlQura yyyy-MM-dd = 1448-03-19
TryConvertIanaIdToWindowsId(Asia/Riyadh)=True -> Arab Standard Time
TryConvertWindowsIdToIanaId(Arab Standard Time, AE)=True -> Asia/Riyadh
TryConvertWindowsIdToIanaId(E. Africa Standard Time, ET)=True -> Africa/Addis_Ababa
FindSystemTimeZoneById(Asia/Riyadh): Id=Asia/Riyadh HasIanaId=True Offset=03:00:00
TryFindSystemTimeZoneById(asia/riyadh lowercase)=True Id=Asia/Riyadh ; (UTC)=True Id=UTC ; (Etc/UTC)=True Id=Etc/UTC ; (Not/AZone)=False
ar plural n=0..101: zero one two few many other other
en selectordinal: 1st 2nd 3rd 4th 11th 21st 22nd 23rd
nested: She has 3 items ; offset: you and 4 others
{d, date} 9/1/2026 ; {d, date, short} 9/1/2026 ; {d, date, full} Tuesday, September 1, 2026 ; medium/long/yyyy-MM-dd THROW UnsupportedFormatStyleException
{d, time} 2:05:00 PM ; {d, time, short} 2:05 PM ; {d, time, medium} 2:05:00 PM
{x, number} 1,234.5 ; integer 1234 ; currency $1,234.50 ; percent 123,450% ; #,##0.00 THROWS
ar-SA {d, date, short} 19‏‏/3‏‏/1448 بعد الهجرة ; ar-SA {x, number} 1٬234٫5
shared en formatter, override ar, n=2: two ; no override: other
am plural n=0: one ; n=1: one ; n=2: other 2 ; unknown locale "zz": other ; en-GB inherits en: one
missing var THROWS VariableNotFoundException ; malformed THROWS UnbalancedBracesException
Parallel smoke test (shared cached formatter, 16 threads, 50k formats) errors=0
CustomValueFormatter members: TryFormatDate(CultureInfo, Object, String style, String&), TryFormatTime(...), TryFormatNumber(...)
```

## Appendix B — source-level facts quoted from dotnet/extensions `main` (fetched 2026-09-01)

- `DefaultHybridCache.cs`: `_localCache = services.GetRequiredService<IMemoryCache>();` and `_backendCache = _options.DistributedCacheServiceKey is null ? services.GetService<IDistributedCache>() : services.GetRequiredKeyedService<IDistributedCache>(_options.DistributedCacheServiceKey);`
- `DefaultHybridCache.L2.cs` `SetL1<T>`: `cacheEntry.AbsoluteExpirationRelativeToNow = expiry; cacheEntry.Value = value; if (value.TryGetSize(out long size)) { cacheEntry = cacheEntry.SetSize(size); }`
- `DefaultHybridCache.MutableCacheItem.cs` `TryGetSize`: `size = _buffer.Length;` (serialized bytes); `ImmutableCacheItem.Size` defaults to -1 and is set from the serialized buffer length.
- `DefaultHybridCache.StampedeStateT.cs`: `bool skipSerialize = cacheItem is ImmutableCacheItem<T> && (activeFlags & FlagsDisableL1AndL2Write) == FlagsDisableL1AndL2Write;` with the comment "the only scenario in which we *do not* need to serialize is if: it is an ImmutableCacheItem (so we don't need bytes for the CacheItem, L1) …".
- `DefaultHybridCache.TagInvalidation.cs`: `ConcurrentDictionary<string, Task<long>> _tagInvalidationTimes`; `_globalInvalidateTimestamp` for `*`; expiry test `timestamp <= pending.Result`.
