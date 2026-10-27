# Verify (Fable, key: data) — scenario walk-through findings

Scenarios walked: 1 device-zone trip (Riyadh → Dubai → London, option on/off, second user); 2 region
and digits resolution (server, client, emails, `me/save`, validation, Users page); 3 decimal and
bigint on the wire (type, totals, body, response, 409, lint); 4 retry classes and the watchdog;
5 the three `forbidden` reasons from raise site to message.

Everything else on the five paths agrees across 0010 §D.4, 0013, 0014, 0015, 0016, 0018, 0019,
0021, 0023 §4.5, 0024, 0025, 0028, 0029, 0030, 0031 and 0032: the negotiation order, the thirteen
`MeSaveRequest` members and their paths, the decimal/long converters and key-typed ids, the
`Idempotent`/`Long` flags from attribute to chunk to `TmApi`, and the `Reason`/`Path` arguments
from `ForbiddenException` to the `TmErrorPresenter` table.

## 0025

- [FC-01] (must) 0025 §2.6 — "One `readAll` of P's user partition and, on a tenant path, P's
  tenant partition. This one transaction is the warm path's only store access." -> "One `readAll`
  of the device partition, P's user partition and, on a tenant path, P's tenant partition. …"
  (one more key range, `bound(['device'], ['device', []])`, in the same transaction). Why:
  scenario 1, the check after the device zone changes. §2.1 says the mirror is filled only by
  `readAll`, `put`, `patch` and other tabs' `store.changed`, and no text anywhere reads
  `{ kind: 'device' }`, so `doc(device, 'zone')` is `undefined` in every new tab. 0024 §7.8 step 1
  ("No `seen`: record `device` and stop") then fires on every launch: the prompt never opens, the
  automatic update never runs, and `TmDeviceZone.auto` reads false on My profile although the
  option was saved. Also add the device partition to §14's Store row ("the boot step reads `zone`").

## 0024

- [FC-02] (must) 0024 §4.3 step 2 — "runs its boot step's one store read: the user partition
  and, on a tenant path, the tenant partition (0025 §2.6)" -> "… one store read: the device
  partition, the user partition and, on a tenant path, the tenant partition (0025 §2.6)". Why:
  the same gap as FC-01: §7.8 reads `TmZoneDoc` from the device partition and nothing loads it
  into the mirror.
- [FC-03] (should) 0024 §7.7 step 5 and §10.2 — "A miss falls back to the language's default
  region, then to the default language's locale." / "`Accept-Language` | `TmLocalization.locale()`"
  -> in §10.2 send `<language>-<region>` plus the digits extension, built from the `language` and
  `region` signals (§7.7 steps 1–4, before the browser check); in §7.7 step 5 say the fallback
  changes only the locale the client formats with, never the header. Why: scenario 2's server-side
  order (0013 §9.2 takes the header's language first). On Chromium an `om`, `ti` or `so` user's
  `locale` becomes the default language's (`en-GB`), so the SPA asks for English: `Content-Language`,
  `ContentLanguageIndex`, problem titles and Excel labels switch language although .NET resolves
  every catalogue pair (0013 §7.2) and the user chose Oromo.
- [FC-04] (should) 0024 §11 — "`{field}` is the label of `arguments.path`, the related column
  beyond the projection." -> add: "resolved through `TmEntities` against the context's `entity`,
  the query's root (spec 0029 §4.3; the slash label of §11.4); without an `entity`, or when the
  path does not resolve, the raw path shows." Why: scenario 5, the restricted-path 403 on a query.
  `arguments.path` is the path read from the root (0015 §14.1) while `arguments.resource` names the
  related entity `E` (0014 §5.3), so an implementer has no stated entity to label the path against.

## 0016

- [FC-05] (must) 0016 §6.1, `Accept-Language` row — "language: `User.PreferredLanguage` → tenant
  primary language → `en`" -> "language: `User.PreferredLanguage` → tenant primary language, when
  offered → the distribution default (spec 0013 §7.3)". Why: scenario 2's server resolution. 0013
  §9.2 ends its language row at "the distribution default", which 0013 §7.3 defines as the first
  declared code (`ar` for `Languages(["ar-SA", "en-GB"])`), not `en`.

## 0029

- [FC-06] (should) 0029 §5.3 — "label(entity: string, id: TmId): Signal<string | null>; // a known
  row's display name; null while unknown" -> add a bullet: "**Ids by key type.** `label`, `resolve`
  and `remember` key the cache by the entity's `key.type` (§2.2): an id given as a `TmDecimal` for
  an `int`-keyed entity (a `long` pointer such as an assignment's `targetId` "1284", spec 0028
  §5.1) is coerced to a number before the lookup, so `label('acme.SalesInvoice', "1284")` finds
  the row remembered under `1284`." Why: scenario 3's bigint-keyed related record and its pointer.
  0016 §3.2 writes a `long` member as a string and an `int` key as a number, and 0025 §3.1 keys
  `byId` by `TmId`, so without a stated coercion a string id never matches a numeric key and the
  bell's actor and target names fall through to `resolve` or "#1284".

## 0032

- [FC-07] (should) 0032 §4.2, digits row — "\"Latin (123)\" and the native system, named by
  `tm.digits.<system>`; hidden when no language in scope has native digits" -> say which system
  the Native option shows under `offered` scope: "the active language's native system, else the
  first offered language's that has one (`arab` before `arabext` and `deva` in Acme)". Why:
  scenario 2's administrator editing the user's digits on the Users page (`scope` `offered`, §4.1).
  `Native` is one stored value, but `offered` scope can span `ar` (`arab`), `ur` (`arabext`) and
  `hi` (`deva`), and nothing says which sample digits label the option.
