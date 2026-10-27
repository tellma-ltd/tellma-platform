# Spec: Stock Pages

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every Tellma distribution ships the same administration pages. The platform provides Users, Roles,
Settings, My profile, Schedules, Outgoing emails, Exports and Imports. The GL pack provides Centers.
This spec ships them. A distribution writes nothing to get them, and a few lines to extend one.

- **Seven entity pages.** Each is a declaration on the search page (spec 0030) and the details
  page (spec 0031), plus a markup-only layout. A few page-specific components fill the gaps: the
  permissions editor, the user's Sign-in aside and Access tab, the invite and credentials dialogs,
  and the email body viewer.
- **Two source pages.** Settings and My profile are details pages over a source that is not an
  entity. Editing, saving, conflicts, drafts and the leave guard therefore come from spec 0031 too.

The server side already exists in specs 0013, 0014, 0016, 0018, 0019, 0020 and 0022, and §12
lists the members these pages read. This spec adds the client view of setting keys and securables.

Other specs that carry parts of this design:

- spec 0024 §2.2, §7.7, §7.8, §9.2, Appendix A — the `settingsTabs` feature member; the regional
  formats and the device time zone that the locale fields edit; the `settings` and `me` routes; the
  `unknown-setting-category` check.
- spec 0027 §3.2 — the Administration section and the order of its entries.
- spec 0029 §3, §12.1 — `actions[].visible`, the column `filter`; `TmValueDescriptor`.
- spec 0030 §13.3 — `TmImportResultView`.
- spec 0031 §3.1, §5.3, §6.6, §10.4 — `TmDetailsSource` and `tm-details-page`; the step-up notice;
  the tabs' `selected` model; action dialogs with `run({ keepOpen, ids })`.

## Goals / Non-goals

**Goals**

- Every stock page works with no distribution code; extending one costs a declaration override and
  a layout that reuses the stock field groups.
- Settings renders its tabs, labels and controls from the metadata, so a warm launch shows the
  page before any request. Two administrators editing different keys never conflict.
- My profile renders entirely from the stored `me` document and saves through `users/me/save`.
- The role editor checks a permission's filter as the user types and shows drifted permissions.
- An administrator can invite users, issue service credentials, see why a user can or cannot do
  something, and read exactly what an outgoing email contained.

**Non-goals (explicitly out of scope)**

- **Other platform pages.** Jobs, Notifications, Notification settings and Assignments belong to
  spec 0028. Support requests belong to spec 0026.
- **The frameworks underneath.** Search and details pages (specs 0030, 0031), the entity model and
  typed controls (spec 0029), and generic components (spec 0023).
- **Deferred features.** Editing role memberships from the role page; "Schedule this export"; a
  cron preview; editing another user's preferences or notification settings; showing remote images
  in emails; drawing a signature on screen.

## 1. Placement and vocabulary

The declarations are data in the eager primary entry point. Everything they render loads in one
lazy chunk, `@tellma/core/admin`. The Centers page ships in the GL pack's package.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

| Part | Entry point | Folder (`client/projects/`) | Loaded |
|---|---|---|---|
| the six declarations, `TmSettingsTabDeclaration`, the settings metadata types | `@tellma/core` | `core/tellma-core/src/admin/` | eager, ≤ 2 KB gzip |
| layouts, field groups, the two source pages, editors, cells, dialogs | `@tellma/core/admin` | `core/tellma-core/admin/` | lazy, one chunk, 28 KB self-weight |
| `tmGlFeature()`, the `gl.Center` declaration | `@tellma/module-gl` | `module/tellma-module-gl/src/` | eager, < 1 KB |
| `TmGlCenterDetails`, `TmGlCenterFields` | `@tellma/module-gl/centers` | `module/tellma-module-gl/centers/` | lazy |

- **What `/admin` imports.** The primary, `/entity`, `/search` (for the import result view),
  `/details` and `/attention` (for job labels).
- **Lazy only.** The declarations reach `/admin` only through `import()` (spec 0024 §1).
- **Packs split the same way.** A pack's primary holds declarations only. Its layouts ship in a
  secondary entry point, because they import `/details`.
- **Dialogs.** The invite and credentials dialogs load with `import()` on first use, ≤ 4 KB each.
- **Testing exports** (`@tellma/core-testing`): `TmSettingsPageHarness`,
  `TmPermissionsEditorHarness`, `TmUserAccessHarness`, `tmFakeSettingsDetails()`.
- **Strings.** Client scope `tm.admin`; server keys per spec 0013 §10.1.

**Vocabulary**

- *Stock page* — a page this spec ships: the seven entity pages and the two source pages.
- *Source page* — a details page on spec 0031's host fed by a `TmDetailsSource` that is not an
  entity route: Settings and My profile.
- *Field group* — a component that renders a page's stock fields inside a card, such as
  `TmUserFields` or `TmGlCenterFields`, so a distribution's layout can reuse them.
- *Extra* — a named result the details read returns beside the record, such as a role's `members`
  (spec 0015 §5.3). Every extra a page lists is *eager*: it arrives with every read (spec 0031 §6).
- *Securable* — a resource and action pair that a permission grants (spec 0014 §2). A *sensitive*
  securable makes the user confirm their identity first; that confirmation is the *step-up* (spec
  0014 §4.3).
- *Filter root* — the entity a permission's filter is written over (spec 0014 §3.7).
- *Category* — a settings tab: `general` (the typed row plus `general` keys) or a key category.
- *Group* — a titled card inside a category, derived from a key's name (§7.3).
- *Entry stamp* — the `ModifiedAt` of a key's `core.SettingEntries` row, or `null` when none exists.
- *Drift* — a stored permission that grants nothing (spec 0014 §9).
- *Built-in schedule* — a schedule whose id is in the reserved band, `Id ≤ 999` (spec 0020 §7.1).

## 2. What a distribution writes

A distribution writes nothing for the stock pages. The table lists the uncommon needs and what
each costs in lines.

| Need | What the distribution writes | Lines |
|---|---|---|
| Centers with an extra field | an override and a layout reusing `TmGlCenterFields` (§6, spec 0024 §2.3) | 4 + 6 |
| Users with extra columns (`MyUser : User`) | an override and a layout reusing `TmUserFields`, `TmUserTabs`, `TmUserSignIn` | 4 + 7 |
| Its own settings | `SettingKey<T>` fields in a C# holder (spec 0013 §5.3) and resx labels | 0 client |
| A settings tab with its own editor | `settingsTabs: [{ category, component }]` and a component (§7.5) | 1 + ~14 |
| Hide a stock launcher entry; the page stays reachable (spec 0024 §2.3) | `hide: ['entry:core.Schedule']` | 1 |
| Drop an earlier feature's settings-tab editor; the category's generic tab returns (spec 0024 §2.3) | `hide: ['settingsTab:invoicing']` | 1 |

**Example —** a distribution whose `MyUser` adds `EmployeeNumber` overrides `details` only:

```ts
export const usersOverride = tmEntity('core.User', { details: () => import('./my-user-details') });

@Component({ selector: 'acme-user-details', imports: [TmDetailsKit, TmUserFields, TmUserTabs, TmUserSignIn],
  template: `<tm-details-card><tm-user-fields /><tm-field path="EmployeeNumber" /></tm-details-card>
             <tm-user-tabs /><tm-user-sign-in /><tm-details-audit />` })
export default class MyUserDetails {}
```

## 3. The core feature's declarations

The implicit `core` feature (spec 0024 §2.3) declares the six stock entities. Each declaration
sets its launcher place, its search defaults, its lazy layout and its promoted actions.

```ts
// @tellma/core primary — src/admin/admin.entities.ts
const admin = () => import('@tellma/core/admin');

export const tmUsers = tmEntity('core.User', {
  section: 'administration', order: 10, icon: 'users',
  columns: ['Image', 'Name', 'Email', 'Kind', 'State', 'IsActive'], orderBy: 'Name',
  tile: { image: 'Image', title: 'Name', subtitle: 'Email', topEnd: 'Kind', badge: 'State' },   // the tile view (spec 0030 §2.3)
  details: () => admin().then(m => m.TmUserDetails),
  actions: [
    { name: 'invite', icon: 'send', button: true, dialog: () => admin().then(m => m.TmInviteDialog),
      visible: (u) => u.kind === 'Individual' && u.state !== 'Joined' && u.isActive },
    { name: 'issue-credentials', icon: 'key-round', button: true, dialog: () => admin().then(m => m.TmCredentialsDialog),
      visible: (u) => u.kind === 'Service' && u.isActive },
  ],
});
export const tmRoles = tmEntity('core.Role', {
  section: 'administration', order: 20, icon: 'shield',
  columns: ['Name', 'Code', 'IsPublic', 'IsActive'], orderBy: 'Name',
  details: () => admin().then(m => m.TmRoleDetails),
});
export const tmSchedules = tmEntity('core.Schedule', {
  section: 'administration', order: 50, icon: 'calendar-clock',
  columns: ['Name', { path: 'HandlerKey', display: () => admin().then(m => m.TmJobHandlerCell) },
            'CronExpression', 'RunAsUser', 'PausedReason', 'IsActive'], orderBy: 'Name',
  details: () => admin().then(m => m.TmScheduleDetails), validate: tmJsonTextSchema('argumentsJson'),  // §9
  actions: [                                                     // s.id > 999: not a built-in schedule (§9)
    { name: 'run-now', icon: 'play', button: true },
    { name: 'take-over', icon: 'user-check', visible: (s, c) => s.id > 999 && s.runAsUserId !== c.userId },
    { name: 'deactivate', visible: (s) => s.id > 999 },             // the built-in action (spec 0015 §2.2)
  ],
});
export const tmOutgoingEmails = tmEntity('core.OutgoingEmail', {
  section: 'administration', order: 90, icon: 'mail',
  columns: ['CreatedAt', 'To', 'Subject', 'State', 'DeliveryStatus', 'AttemptCount'], orderBy: 'CreatedAt:desc',
  filters: [{ key: 'attention', labelKey: 'tm.admin.emails.attention',            // emails that need attention (§10.1)
    queryex: "State in ('Failed', 'Rejected') or DeliveryStatus in ('Dropped', 'Failed', 'Bounced', 'SpamReported')" }],
  details: () => admin().then(m => m.TmOutgoingEmailDetails),
  actions: [
    { name: 'retry', icon: 'rotate-cw', button: true,
      visible: (e) => e.state === 'Failed' || e.state === 'Sending' || (e.state === 'Pending' && e.jobId == null) },
    { name: 'cancel', visible: (e) => e.state === 'Pending' },
  ],
});
const resource = { path: 'Resource', display: () => admin().then(m => m.TmEntityNameCell) };  // shared by Exports and Imports (§11)
export const tmExports = tmEntity('core.Export', {
  section: 'administration', order: 70, icon: 'file-down',
  columns: ['CreatedAt', resource, 'Kind', { path: 'File', display: () => admin().then(m => m.TmDownloadCell) },
            'RowCount', 'ExpiresAt', 'CreatedBy'], orderBy: 'CreatedAt:desc',
  details: () => admin().then(m => m.TmExportDetails),
});
export const tmImports = tmEntity('core.Import', {
  section: 'administration', order: 80, icon: 'file-up',
  columns: ['CreatedAt', resource, 'Mode', 'Job.Status', 'RowCount', 'ErrorCount', 'CreatedBy'], orderBy: 'CreatedAt:desc',
  details: () => admin().then(m => m.TmImportDetails),
});
```

- **`visible`** (spec 0029 §3) hides a promoted button or menu item for one record, built-in
  actions included. It never replaces the server's check. Search-page bulk actions ignore it, and
  the server refuses row by row instead (spec 0030 §12.2).
- **Launcher.** The `order` values place the entries in spec 0027 §3.2's Administration section.
  Settings is the core entry `core.settings` (§7.1). My profile is `core.me`, a search-only entry.
- **Derived by the frameworks.** This spec declares none of these:
  - routes `/{t}/{segment}` and `/{t}/{segment}/{id}`, plus `/new` where the stack saves (Users,
    Roles, Schedules);
  - view-only details, with no Edit or New, on the other pages;
  - Export and Import menus where the operations exist;
  - Activate, Deactivate, Delete and entity actions, from the metadata;
  - pickers for `core.User`, `core.Role` and `core.Schedule` (spec 0029 §14).
- **Links from notifications** reach these pages through `TmUrls.target` (spec 0024 §9.4).
- **Members** see their own Exports and Imports (§11), so the launcher shows both entries to
  everyone (spec 0027 §3.2).

## 4. Users

The user page shows the user's fields, their role memberships and their effective access, with a
Sign-in aside for the invitation or credential state. Invite and Issue credentials are entity
actions with their own dialogs.

### 4.1 The layout and field groups

The user layout is built from three reusable parts: the fields, the tabs and the Sign-in aside. A
distribution's own layout reuses them (§2).

```ts
// @tellma/core/admin — users/
@Component({ selector: 'tm-user-details', imports: [TmDetailsKit, TmUserFields, TmUserTabs, TmUserSignIn], template: `
  <tm-details-card><tm-user-fields /></tm-details-card> <tm-user-tabs /> <tm-user-sign-in /> <tm-details-audit />` })
export class TmUserDetails {}

@Component({ selector: 'tm-user-tabs', imports: [TmDetailsKit, TmUserAccess], template: `
  <tm-details-card flush><tm-details-tabs>
    <tm-details-tab id="roles" collection="RoleMemberships"><ng-template tmDetailsTabContent>
      <tm-child-grid collection="RoleMemberships" [columns]="[{ path: 'Role', filter: 'IsPublic = false' }, 'Notes']" />
    </ng-template></tm-details-tab>
    @if (canSeeAccess()) {
      <tm-details-tab id="access" labelKey="tm.admin.users.access">
        <ng-template tmDetailsTabContent><tm-user-access /></ng-template></tm-details-tab> }
  </tm-details-tabs></tm-details-card>` })
export class TmUserTabs { protected readonly canSeeAccess = inject(TmPermissions).can('core.Role', 'Read'); }

@Component({ selector: 'tm-user-fields', hostDirectives: [TmFieldGroup], imports: [TmDetailsKit, TmUserLocaleFields, TmSignatureField], template: `
  <tm-field path="Image" /> <tm-field path="Name" /> <tm-field path="Kind" />
  @if (d.model()?.kind === 'Individual') {
    <tm-field path="Email" [readonly]="d.mode() !== 'create' && d.entity()?.state !== 'New'" hintKey="tm.admin.users.emailHint" />
    <tm-field path="ContactEmail" /> <tm-field path="ContactMobile" /> <tm-field path="Gender" />
    <tm-user-locale-fields /> <tm-signature-field path="Signature" />
  }` })
export class TmUserFields { protected readonly d = injectTmDetails<TmEntityType<'core.User'>>(); }
```

- **Kind** is write-once (spec 0014 §3.1). A service account, like the system user (`Kind =
  System`), shows Image, Name and Kind only (`Users.EmailNotAllowed`).
- **Email** locks once the user leaves `New` (`Users.EmailLockedAfterInvite`). Its hint: "Sign-in
  email. It can't change after the invitation is sent."
- **Memberships.** The Role picker offers active roles that are not public: public roles take no
  members (spec 0014 §3.5). A hint under the grid says "Public roles apply to everyone and don't
  need members." Lockout and escalation errors land on the row (spec 0031 §8.5).
- **`tm-user-locale-fields`** renders §4.2's five fields for `PreferredLanguage`,
  `PreferredCalendar`, `PreferredTimeZone`, `PreferredRegion` and `PreferredDigits`, each nullable.
  It has two inputs, which only My profile sets (§8):
  - `scope` (default `offered`) passes to the region and digits fields. My profile sets
    `language`, so users pick among the formats of the language they read. The user page keeps
    `offered`, since an administrator's language says nothing about the user's.
  - `autoTimeZone` adds `tm-auto-time-zone` (§8) on its own row under the Time zone field.
- **Step-up.** Saving a user is sensitive (`core.User × Save`, spec 0014 §4.3). Spec 0031 §8.6
  keeps the draft across the identity confirmation.

### 4.2 Language, calendar, time-zone, region and digits fields

Five platform field components render a code as a name in view mode and offer only valid codes in
edit mode. The server stays the authority.

```ts
@Component({ selector: 'tm-language-field', hostDirectives: [TmFieldGroup] })
declare class TmLanguageField { readonly path: InputSignal<string>; readonly scope: InputSignal<'tenant' | 'offered'> }
@Component({ selector: 'tm-calendar-field', hostDirectives: [TmFieldGroup] })
declare class TmCalendarField { readonly path: InputSignal<string>; readonly scope: InputSignal<'tenant' | 'registered'> }
@Component({ selector: 'tm-time-zone-field', hostDirectives: [TmFieldGroup] })
declare class TmTimeZoneField { readonly path: InputSignal<string> }
@Component({ selector: 'tm-region-field', hostDirectives: [TmFieldGroup] })
declare class TmRegionField { readonly path: InputSignal<string>; readonly scope: InputSignal<'language' | 'offered'> }
@Component({ selector: 'tm-digits-field', hostDirectives: [TmFieldGroup] })
declare class TmDigitsField { readonly path: InputSignal<string>; readonly scope: InputSignal<'language' | 'offered'> }
```

Each wraps `tm-field` with `tmFieldView` and `tmFieldEdit` templates (spec 0031 §6.2):

| Field | View | Edit options (`tm-select`) |
|---|---|---|
| language | "العربية (ar)" | `tenant`: the tenant's content languages; `offered`: the metadata's `languages` |
| calendar | "Umm al-Qura" (`tm.calendar.<code>`) | `tenant`: the tenant's calendars; `registered`: the metadata's `calendars` |
| time zone | "Asia/Riyadh (GMT+03:00)" | searchable; `Intl.supportedValuesOf('timeZone')` grouped by region, each with its `longOffset` |
| region | "Saudi Arabia (SA)" and its sample | searchable; `language`: the regions offered with the active language; `offered`: every region offered with any offered language (spec 0024 §7.7). Named by `Intl.DisplayNames`, sorted by `Intl.Collator`, each with its sample |
| digits | "Arabic-Indic (١٢٣)" | "Latin (123)" and the native system, each named by `tm.digits.<system>`. The native system is the active language's. If it has none, under `offered` it is the first offered language's that has one (`arab` in Acme). Hidden when no language in scope has native digits |

- **Nullable** fields list "Tenant default ({effective value})" first. On the Settings page,
  Region and Digits label that option differently (§7.5). A stored value outside the options stays
  as an option, so it is never lost.
- **The sample** formats `1234.5` and today's date with the locale the option would give, in the
  user's language: "١٬٢٣٤٫٥ · ١٩/٠٦/٢٠٢٦" for `SA`. It needs no request.
- **Direction.** Codes and zone ids render in LTR isolates inside RTL text.
- **Validation** is the server's (`Users.LanguageNotOffered`, `Users.CalendarUnknown`,
  `Users.TimeZoneUnknown`, `Users.RegionNotOffered`, `Settings.*`). A zone the server cannot map
  comes back as a field error. An unknown digits value fails spec 0016's enum binding.

### 4.3 Inviting users

`users/invite` (spec 0018 §3.3) takes ids and answers one `InviteResult` per id. Its dialog opens
from the promoted button (one user) or the search page's bulk menu (a selection).

1. **Confirm.** The dialog asks "Invite {name}?" or "Invite {n} users?", then explains:
   - normally, "Each person gets an email from the identity server to set up their sign-in, and a
     welcome email with a link to {tenant}.";
   - on a sandbox tenant (`TmLaunchCache.docs.tenant().category`), "This is a sandbox. Only people
     who already have an account are added, and no one gets an invitation email.";
   - when the action is sensitive, it adds "You'll be asked to confirm it's you."
2. **Run** `run({}, { keepOpen: true })` (spec 0031 §10.4) under the busy overlay.
3. **Outcome**, by response:

| Response | The dialog |
|---|---|
| 200, one user whose row has a `Status` | closes; the re-read page shows the new state |
| 200, otherwise | shows the results list |
| 422 | shows the rows' errors in its alert; nothing was sent, since one invalid row fails the whole action |
| 502 `partial-failure` | shows the results list for `errorDetails.results`, then the retry line |

- **Results list.** One line per user: the name, then "Invitation sent" (`Invited`), "Invitation
  sent again" (`Reinvited`), "Added: already had an account" (`Active`) or the row's `Error`. The
  error text comes from `TmServerText` for `Users.*` codes, and from the identity server otherwise.
- **422 codes:** `Users.NotIndividual`, `Users.Inactive`, `Users.AlreadyJoined`.
- **Retry line.** "{n} weren't attempted because the identity server stopped responding." and a
  "Try again for these" button. It runs `run({}, { keepOpen: true, ids:
  errorDetails.failed.map(Number) })`, since `failed` carries the ids as strings (spec 0018 §3.3).
- **After close** the host re-reads the record and marks the list stale (spec 0031 §10.4). Any
  other failure goes to `TmErrorPresenter` and the dialog stays.
- **Step-up.** A confirmation redirect returns to the page; the user presses Invite again.

**Example —** Farah selects 30 users and presses Invite. The identity server stops responding
after 12 of them. The dialog lists those 12 results, then "18 weren't attempted because the
identity server stopped responding." Try again for these re-runs the action for the 18 ids only.

### 4.4 Service accounts and credentials

`users/issue-credentials` (spec 0018 §3.8) creates or rotates a service account's client and
returns its secret once. The dialog shows it once and keeps it nowhere else.

1. **Confirm.** "Issue credentials for {name}?" When the user already has a client id: "The current
   secret stops working at once. Update every system that uses it."
2. **Run** with `keepOpen`. The dialog becomes "Credentials for {name}" and shows:
   - Client id and Client secret as read-only mono LTR fields, each with a `tm-copy-button` (spec
     0023 §3.3), and a "Copy both" button;
   - the warning "This secret is shown only once. Copy it now and store it somewhere safe.";
   - Done. Closing before any copy asks "Close without copying the secret?".
3. **Never kept.** The secret lives in the dialog's memory only. It never reaches a page context,
   draft, page state, telemetry, the state dump or the record. The dialog's overlay context
   publishes no values, and an `assistant` origin never receives the result.

### 4.5 The Sign-in aside (`tm-user-sign-in`)

The aside (spec 0031 §6.7) says how the user signs in and when they were last active.

| User | Shows |
|---|---|
| Individual, `New` | "Not invited yet." |
| Individual, `Invited` | "Invited {relative}", the invite status, `LastInviteError` through `TmServerText`, and Check delivery |
| Individual, `Joined` | "Joined {relative}" |
| Service | the client id (`Subject`, mono, with Copy) or "No credentials yet.", then the deactivation note |
| every user | "Last active {relative}" or "Never", from the `activity` extra (spec 0018 §3.1) |

- **Deactivation note.** "Deactivating this account deletes its credentials. Issue new ones after
  activating it." (spec 0018 §3.6)
- **Check delivery** calls `users/invitation-status { ids: [id] }` (spec 0018 §3.4) on press. The
  call aborts when the user leaves. The answer replaces the button's line, at the same height:

| `Delivery.Status` | Line |
|---|---|
| `Pending` | "Waiting to send" |
| `Sent` | "Sent", plus "Delivery isn't tracked" when `ExpectsDeliveryEvents` is false |
| `Delivered` | "Delivered {relative}" |
| `Bounced`, `Rejected`, `Complained`, `Abandoned` | "{state}: {reason}" |
| `NotFound` | "Sent by another application" |

- **Failure** shows "Couldn't check delivery" with Retry.

### 4.6 The Access tab (`tm-user-access`)

The tab answers "what can this user do, and why". It checks every securable through
`access/check` and shows one row per resource and action.

- **Who sees it.** An administrator with `core.Role × Read`, which `access/check` requires for
  another user (spec 0018 §3.7). Without that grant the tab is absent.
- **Requests.** The tab sends every non-wildcard securable of the metadata (§12.4), in registry
  order. It sends chunks of 200, the endpoint's cap, with two in flight, under the host's signal.
  Acme's ~150 securables take one call.
- **Rows.** Rows group by resource label, one row per action. Each row:
  - is named by `TmPermissions.label(resource, action)` (spec 0029 §7), the "{resource}: {action}"
    words a `forbidden` message shows (spec 0024 §11);
  - has the raw pair, in mono, as its description;
  - shows an icon and "Allowed" (`Unrestricted`), "Allowed for some records" (`Filtered`) or "Not
    allowed" (`Denied`).
- **Reasons.** Enter expands a row's reasons:
  - role grants, with a link to the role and the filter in mono LTR;
  - bespoke criteria (`Access_Reason_<reason>`), such as "Their own records" for `self`;
  - "System user";
  - drifted permissions, marked "Not in effect" (§5.5).
- **Toolbar.** A search, a `tm-segmented` All / Allowed / Not allowed, and Refresh sit above the
  rows. The search matches labels and raw names alike, so either line a user reads out from an
  error finds its row.
- **Language switch.** The rows re-render with no request.

**Example —** Omar's save fails with "It needs “Sales invoices: Create and edit”." and the raw pair
`acme.SalesInvoice · Save`. Farah opens Omar's Access tab and types "sales invoices: create", or
pastes the raw pair. Either way one row remains, "Sales invoices: Create and edit", and it shows
"Not allowed".

### 4.7 Image, signature, activation

Image and Signature are uploaded images. Activation, deactivation and deletion are the frameworks'
actions, bounded by the server's user rules.

- **Image.** `tm-field path="Image"` renders spec 0031 §6.3's image editor with the `user-image`
  avatar policy.
- **Signature** (policy `user-signature`, PNG ≤ 1 MiB, spec 0014 §3.1). `tm-signature-field`
  wraps `tm-field`:
  - **View.** A `tmFieldView` template draws the image on a fixed light "paper" box: `--tm-paper`
    in both themes, 240 × 80 px, 1 px border. Dark ink stays visible in the dark theme. An empty
    signature shows "—".
  - **Edit.** The kit's image editor.
- **Activate and Deactivate** are spec 0031's actions. Deactivating signs the user out of the
  tenant (spec 0018 §3.6).
- **Delete** succeeds only for `New` users (`Users.OnlyNewUsersDeletable`).
- **The system user** (id 1, `Kind = System`) lists like any user. The server refuses every write
  to it (`Users.SystemUserImmutable`), and §3's `visible` predicates hide both actions.

## 5. Roles

The role page edits a role's permissions in a grid with one filter panel, and checks each filter
as the user types. It lists the role's members read-only and marks permissions that grant nothing.

### 5.1 The layout

The role page shows the role's fields, then a Permissions tab and a Members tab.

```ts
// @tellma/core/admin — roles/role-details.ts
@Component({ selector: 'tm-role-details', imports: [TmDetailsKit, TmAlert, TmRolePermissions, TmRoleMembers], template: `
  @if (d.id() === 1) { <tm-alert kind="info">{{ t('tm.admin.roles.administratorNotice') }}</tm-alert> }
  <tm-details-card>
    <tm-field path="Name" /> <tm-field path="Code" />
    <tm-field path="IsPublic" hintKey="tm.admin.roles.publicHint" [readonly]="d.id() === 1" />
  </tm-details-card>
  <tm-details-card flush><tm-details-tabs>
    <tm-details-tab id="permissions" collection="Permissions">
      <ng-template tmDetailsTabContent><tm-role-permissions /></ng-template></tm-details-tab>
    <tm-details-tab id="members" labelKey="tm.admin.roles.members">
      <ng-template tmDetailsTabContent><tm-role-members /></ng-template></tm-details-tab>
  </tm-details-tabs></tm-details-card>
  <tm-details-audit />` })
export class TmRoleDetails { protected readonly d = injectTmDetails<TmEntityType<'core.Role'>>(); }
```

- **Administrator** (id 1, spec 0014 §3.5): "This role always grants everything. You can rename it
  and add permissions." Its `IsPublic` and its `*` / `*` row are locked.
- **Public hint:** "A public role applies to every active user and has no members."
- **Step-up.** `core.Role × Save`, `Delete` and `Activate` are sensitive.

### 5.2 Securables on the client

The metadata's `securables` section (§12.4) is fixed per deployment. The editor's pickers
therefore render at once, even offline.

- **Resources** are grouped by feature in `tm-select` option groups (spec 0010 §B.4), labelled
  "Centers", "Settings: General" or "Credit exposure report", with the resource name as the
  description. "All resources" (`*`) comes first.
- **Actions** of the row's resource follow registry order ("Read", "Create and edit", "Activate and
  deactivate", "Invite"), plus "All actions" (`*`). Resource `*` offers every action name.
- **Filter root** is the pair's `filterRoot`. For action `*`, it is the root that the resource's
  filterable securables share (spec 0014 §3.7 step 2). Resource `*` has none.
- **Sensitive.** A row whose pair is sensitive, or whose wildcard covers a sensitive pair, shows a
  `shield-check` glyph with "Needs identity confirmation". The action's option shows the same.

### 5.3 The permissions editor (`tm-role-permissions`)

Permissions edit in a grid. The active row's filter edits in a panel under the grid, and the
panel checks it as the user types.

```text
┌ Permissions ────────────────────────────────────────────────────────────────┐
│ [!] 1 permission isn't in effect. Fix or remove it.                          │
│    │ Resource        │ Action                    │ Filter          │ Notes    │
│    │ Sales invoices  │ Read                      │ CenterId = 1012 │ Jeddah   │
│ [!]│ Customers       │ Create and edit [shield]  │ Mangr.Id = me() │          │
├────┴─────────────────┴───────────────────────────┴─────────────────┴──────────┤
│ Filter · Customers · Create and edit                                          │
│ [ Mangr.Id = me()                                                           ] │
│ Line 1: Unknown property 'Mangr' on Customer                                  │
│ Records of Customers this permission covers. Leave empty for all records.     │
└───────────────────────────────────────────────────────────────────────────────┘
```

- **Grid.** Spec 0004's editable binding over `injectTmDetails().form.permissions`, with spec 0031
  §6.4's height rule. New rows take negative ids. The columns:
  - a 28 px status column: the drift or local-error marker, with its reason as text;
  - Resource: a custom editor, §5.2's grouped select;
  - Action: the resource's actions; changing the resource clears an action it lacks;
  - Filter: mono, LTR, ellipsized and read-only in the grid; Enter or F2 moves to the panel;
  - Notes: ≤ 1,024 characters.
- **Registration.** The editor registers the `Permissions` collection with
  `registerCollection` (spec 0031 §4.1), so the error summary, the tab's error dot and the page
  context see its rows.
- **The panel** follows the grid's active row, headed "Filter · {resource} · {action}".
  - **Editor.** `tm-expression-editor clause="filter" [rows]="4"`, bound to the row's `filter`,
    over `injectTmQueryexDraft({ root: filterRoot, profile: 'permission' })` (spec 0029 §10).
    Every diagnostic a role save would return shows 250 ms after typing stops.
  - **Hint.** "Records of {root plural} this permission covers. Leave empty for all records. me()
    is the signed-in user, as in CreatedById = me()."
  - **No filter root.** The panel shows "This permission can't be limited to some records." and no
    editor.
  - **View mode** shows the filter text read-only.
- **Public roles** disable "All resources" (`Permissions.WildcardResourceOnPublicRole`).
- **Save errors** at `entities[0].permissions[j]…` (spec 0016 §7.3, codes per spec 0014 §3.7) land
  on row `j`:
  - `FilterInvalid` and `FilterVersionUnsupported` land on its Filter cell. Their summary item
    selects the row and focuses the panel. `FilterVersionUnsupported` reads "Re-enter this filter
    to confirm it under the current rules."
  - `EscalationBeyondSelf` and `UnknownSecurable` land on the row itself.
- **Keyboard.** The grid's model (spec 0004); Tab reaches the panel; F8 walks the editor's problems.

### 5.4 Members (`tm-role-members`)

The tab renders the eager `members` extra (spec 0018 §4.3) through `*tmDetailsExtra`, so it needs
no request of its own.

- **Columns:** avatar and name (a link to the user), Email, State (a badge), Active, Notes. The
  avatar's `colorKey` is the record key `core.User:{id}` (spec 0023 §4.2).
- **Limits.** The server sends at most 500 members, ordered by name. A 500-row list adds "Showing
  the first 500 members." Over 20 rows, a search field filters the rows on the client.
- **Read-only.** "Add a role to a user from the user's page." links to `/{t}/users`.
- **No access.** Without `core.User × Read` the extra is absent: "You can't see this role's
  members."

### 5.5 Drift

The eager `problems` extra (spec 0018 §4.3) lists the role's drifted permissions. The server
computes it in C# from rows the read already loads (a computed extra, spec 0015 §5.3).

- **Banner** at the top of the tab: "{n, plural, one {# permission isn't} other {# permissions
  aren't}} in effect. Fix or remove {n, plural, one {it} other {them}}."
- **Row markers** carry the reason (`Access_Problem_<Code>`):

| Code | Text |
|---|---|
| `UnknownResource` | "This resource no longer exists." |
| `UnknownAction` | "This action no longer exists for this resource." |
| `FilterUnsupported` | "This permission can't have a filter." |
| `VersionUnsupported` | "Re-enter the filter to confirm it." |
| `FilterInvalid` | "The filter isn't valid: {first diagnostic}" |

- **Edit mode** checks edited filters live (§5.3). A row's stored problem leaves the banner once
  the row's filter text changes.
- **Save** re-reads the extra with the record.

## 6. Centers (`@tellma/module-gl`)

The GL pack declares `gl.Center` in its eager primary. It ships the layout and its field group in
`@tellma/module-gl/centers`, so a distribution's layout can reuse the stock fields.

```ts
// @tellma/module-gl (primary, eager)
export const tmGlCenters = tmEntity('gl.Center', {
  section: 'administration', order: 30, icon: 'network',
  columns: ['Code', 'Name', 'CenterType', 'IsActive'], orderBy: 'Code',
  details: () => import('@tellma/module-gl/centers').then(m => m.TmGlCenterDetails),
  picker: { description: ['Code', 'CenterType'] },
});
export function tmGlFeature(): TmFeature {
  return tmFeature({
    key: 'gl',                                                   // = GlModule.FeatureName
    sections: [{ id: 'financials', labelKey: 'Gl_Nav_Financials', icon: 'scale', order: 10 }],
    entities: [tmGlCenters],
  });
}

// @tellma/module-gl/centers (lazy)
@Component({ selector: 'tm-gl-center-fields', hostDirectives: [TmFieldGroup], imports: [TmDetailsKit], template: `
  <tm-field path="Name" /> <tm-field path="Code" hintKey="Gl_Center_CodeHint" /> <tm-field path="CenterType" />
  <tm-field path="Parent" filter="CenterType in ('Abstract', 'BusinessUnit')" />` })
export class TmGlCenterFields {}
@Component({ selector: 'tm-gl-center-details', imports: [TmDetailsKit, TmGlCenterFields], template: `
  <tm-details-card><tm-gl-center-fields /></tm-details-card><tm-details-audit />` })
export class TmGlCenterDetails {}
```

- **`financials`** stays empty until a distribution fills it. An empty section is dropped (spec
  0027 §3.2).
- **Code hint:** "Used by import, export and the API. Must be unique."
- **From the frameworks.** The search page opens as a tree (spec 0030 §16), with Delete and Delete
  with descendants.
- **Parent** picks grouping centers only (`Abstract` or `BusinessUnit`). `Tree.Cycle` and
  `Centers.ParentMustBeGrouping` land on the field. `Centers.ParentMustBeGrouping` also lands on
  `CenterType` when a grouping center with children changes type.
- **Data.** Sample centers exist in Development only (spec 0018 §5.6).

**Example —** Acme's override from §2's first row, adding `MyCenter.ManagerId` (spec 0018 §1.4):

```ts
export const centersOverride = tmEntity('gl.Center', { columns: ['Code', 'Name', 'CenterType', 'Manager', 'IsActive'],
  details: () => import('./my-center-details') });

@Component({ selector: 'acme-my-center-details', imports: [TmDetailsKit, TmGlCenterFields], template: `
  <tm-details-card><tm-gl-center-fields /><tm-field path="Manager" /></tm-details-card><tm-details-audit />` })
export default class MyCenterDetails {}
```

## 7. Settings

One page shows every settings category the user may read, one tab each. It runs on spec 0031's
host through a settings source. Tabs, labels and controls come from the metadata; only the values
wait for `settings/details`.

### 7.1 Route, tabs and permissions

The page has one tab per category the user may read, and the URL names the selected tab. The
whole page shares one edit mode.

- **Routes.** `/{t}/settings` and `/{t}/settings/{category}` (spec 0024 §9.2). The route data is
  `{ kind: 'settings', skeleton: 'details', titleKey: 'tm.admin.settings.title', load }`, where
  `load` imports `TmSettingsPage` from `/admin`. Links use `TmUrls.page('settings/invoicing')`.
- **Launcher.** The entry `core.settings` shows while any category is readable (spec 0027 §3.2).
- **Tabs** are the metadata's categories (§7.3) that pass `TmPermissions.can(category.resource,
  'Read')`. A `Save` grant satisfies it (spec 0014 §5.2). The check uses the cached access
  summary, so the tab strip renders before any request.
- **Selection.** `/{t}/settings` selects the first tab without rewriting the URL. A tab click
  navigates to `/{t}/settings/{category}` with `replaceUrl`, through `tm-details-tabs`'
  `selected` model (spec 0031 §6.6).
- **Unknown category.** An unknown or unreadable category shows "This settings category doesn't
  exist or you can't see it." with a link to the first tab. A user who can read no category gets
  the route's `forbidden` state.
- **One edit mode for the page.** Edit is enabled when any readable category is savable. In edit
  mode, categories without `Save` render their fields read-only, with the lock (spec 0031 §6.2).
- **Overflow.** The tab strip scrolls when it overflows (spec 0005).

### 7.2 The page and its source

`TmSettingsPage` is spec 0031's `tm-details-page` over a settings source and the settings layout
(§7.5). The source maps the settings endpoints onto spec 0031 §3.1's source members.

```ts
// @tellma/core/admin — settings/
@Component({ selector: 'tm-settings-page', imports: [TmDetailsPage],
  template: `<tm-details-page [source]="source" [layout]="layout" />` })
export class TmSettingsPage {
  readonly category = input<string>();                            // the route parameter
  protected readonly source = tmSettingsSource(this.category);
  protected readonly layout = TmSettingsLayout;                   // §7.5
}
type TmSettingsModel = TmEntityType<'core.Settings'> & {          // the typed row: name, name2, primaryLanguage, …, modifiedAt
  // key → value. A secret's entry is absent (untouched), a string (replace) or null (clear).
  readonly entries: Readonly<Record<string, TmJson>>;
};
declare function tmSettingsSource(category: Signal<string | undefined>): TmDetailsSource<TmSettingsModel>;
```

The source's members:

| Member | Value |
|---|---|
| `kind` | `'settings'` |
| `entity` | `'core.Settings'`: the read-only root the kit resolves `Name` and the language fields against |
| `id` | `1` |
| `titleKey` | `tm.admin.settings.title` ("Settings") |
| `draftKey` | `TmDrafts.key(t, 'core.Settings', 1)` |
| `read` | `settings/details` (spec 0013 §6.2) → `{ entity: { ...general, entries: values }, related, extras: { setKeys, entryStamps } }` |
| `write` | §7.6's patch to `settings/save`; the result's `details` maps back the same way, and its `client` is adopted (§7.7) |
| `can` | `edit`: any readable category is savable; `create`, `delete`, `activate`: false |
| `validate` | §7.6 step 2 |
| `saveSecurable` | `['core.Settings.General', 'Save']` while the General tab is selected, else `null` |
| `category` | the selected tab (§7.1) |
| `errorSegments` | `entries.<key>` → `['entries', key]` (keys contain dots); a typed member `PrimaryLanguage` → `['primaryLanguage']` |
| `changes` | the stored `settings` document (spec 0025 §3.1), except the copy this page's own save adopted |

### 7.3 Keys, categories and groups in the metadata

The metadata's Core chunk carries two sections, `settingKeys` and `settingCategories` (spec 0013
§1.2). They are the camelCase JSON of spec 0013 §5.3's `SettingKeyDescriptor` and
`SettingCategoryDescriptor`. The primary exports them as `TmSettingKeyMetadata` and
`TmSettingCategoryMetadata`, member for member.

**Example —** one of Acme's keys and its category:

```json
{ "name": "acme.sales.allowedCustomerTypes", "category": "sales", "group": null, "visibility": "Server",
  "valueType": "Enum", "isNullable": false, "isArray": true, "enumType": "acme.CustomerType",
  "enumValues": ["Retail", "Corporate", "Government"], "references": null,
  "defaultJson": ["Retail", "Corporate", "Government"], "labelKey": "Setting_acme_sales_allowedCustomerTypes" }
{ "name": "sales", "resource": "core.Settings.sales", "order": 3, "labelKey": "SettingCategory_sales" }
```

- **Order.** The categories array is the tab order (`general` first); keys keep registry order.
- **Groups.** A category's groups are its keys' distinct `group` values, in first-appearance order.
  Keys with no group fill the category's first, untitled card. A group's title is
  `SettingGroup_<category>_<group with '.' → '_'>`.
- **Labels.** `labelKey`, `descriptionKey` and the category's `labelKey` are looked up in the
  string pack. A missing key shows the humanized last name segment, as spec 0013 §5.4 does.
- **Enum values** take spec 0013 §10.1's keys from the qualified `enumType`:
  `Acme_CustomerType_Retail`, else the humanized value.
- **Size.** Under 1 KB gzip for Acme.

### 7.4 Rendering a key: `tm-setting-field`

Every settings key renders through `tm-setting-field`, which picks its view and editor from the
key's type. The generic tabs use it, and so can a `settingsTabs` component (§7.5).

```ts
@Component({ selector: 'tm-setting-field' })                     // exported for settingsTabs components
declare class TmSettingField {
  readonly key: InputSignal<string>;
  readonly labelKey: InputSignal<string | undefined>;            // default: the key's label
  readonly hintKey: InputSignal<string | undefined>;             // default: its description
  readonly span: InputSignal<1 | 2>;                             // default 1; 2 for json and scalar arrays
}
```

`tm-setting-field` joins the card's field grid like `tm-field`, and registers `entries.<key>` with
`registerField` (spec 0031 §4.1). It renders spec 0029's `tm-value` and `tm-value-editor`, fed a
`TmValueDescriptor` built from the key (spec 0029 §12.1):

| Key | View | Edit |
|---|---|---|
| `Int32`, `Int64`, `Decimal`, `String`, `Date`, `Time`, `Guid`, `Enum` | spec 0029 §12.2's text for `integer`, `decimal`, `string`, `date`, `time`, `guid`, `enum` | spec 0029 §12.2's control |
| `Boolean` | "Yes" / "No" | `tm-switch` (spec 0023 §6.1) |
| `references`, as a scalar or an array | the display name from `related`, else `TmDisplayNames`, as a link | `tm-entity-field`; an array: `tm-entity-multi-field` |
| an `Enum` array | the labels joined by `Intl.ListFormat` | `tm-multi-select` (spec 0010 §B.4) |
| a `String`, `Int32` or `Int64` array | read-only `tm-chip` items in a `tm-chip-set` | a mono `textarea`, one value per line, parsed per type |
| `Json` | formatted JSON, mono, LTR, collapsed beyond 6 lines | a mono `textarea`, 6 rows |
| visibility `Secret` | "Set" or "Not set" (from `setKeys`) | see **Secrets** below |

- **Large numbers.** `Int64` and `Decimal` values are `TmDecimal` strings (spec 0029 §12.1).
  - An `Int64` key's descriptor is `integer` with `long: true`.
  - A `Decimal` key's descriptor is `decimal`.
  - An `Int64` array's lines parse through `TmDecimals.parse`.
- **Secrets.** When "Not set", the editor is an empty password input. When "Set", it offers
  Replace and Clear. Replace reveals an empty input with `autocomplete="new-password"`; Clear
  writes `null`.
- **Text editors** (JSON and scalar arrays) write the parsed value while their text parses. Text
  that does not parse writes an invalid marker `{ $invalid: text }`, which §7.6 refuses.
- **Default chip.** A key with no stored row shows a muted "Default" chip after its label.
- **Reset.** In edit mode, a value that differs from the default shows "Reset to default", with
  the accessible name "Reset {label} to default". It writes the default.
- **Stable boxes.** The chip's and the reset button's boxes are always reserved, so nothing shifts.

### 7.5 The layout, the General tab and the `settingsTabs` hatch

The layout puts each category in a tab. General has its own body, and every other category renders
generically from its keys. A distribution replaces any tab's body through `settingsTabs`.

```ts
// @tellma/core/admin — settings/settings-layout.ts
@Component({ selector: 'tm-settings-layout', imports: [TmDetailsKit, TmSettingsGeneral, TmSettingsCategory, NgComponentOutlet], template: `
  <tm-details-card flush><tm-details-tabs [(selected)]="s.category">
    @for (c of s.categories(); track c.name) {
      <tm-details-tab [id]="c.name" [labelKey]="c.labelKey"><ng-template tmDetailsTabContent>
        @if (s.tab(c.name)?.(); as custom) { <ng-container *ngComponentOutlet="custom" /> }
        @else if (c.name === 'general') { <tm-settings-general /> } @else { <tm-settings-category [category]="c.name" /> }
      </ng-template></tm-details-tab> }
  </tm-details-tabs></tm-details-card>` })
export class TmSettingsLayout { protected readonly s = injectTmSettings(); }
```

- **`tm-settings-category`** renders one `tm-details-card` per group, the untitled one first, and
  a `tm-setting-field` per key.
- **`tm-settings-general`** renders five cards, then the `general` keys by group:
  - *Organization*: `tm-field path="Name"`, one field per content language;
  - *Languages*: `tm-language-field scope="offered"` for Primary, Secondary and Tertiary; the last
    two are nullable ("None");
  - *Calendars*: `tm-calendar-field scope="registered"` for Primary and Secondary;
  - *Time zone*;
  - *Formats*: `tm-region-field scope="offered"` for Region ("Language default") and
    `tm-digits-field scope="offered"` for Digits ("Region default"), the tenant defaults of spec
    0024 §7.7.
- **Language confirmation.** A language change asks for confirmation as soon as the user makes it:
  - another language for a filled position: "Change the second language from Arabic to French?" /
    "Names already entered in the second language stay as they are and will be shown as French.";
  - clearing the secondary or tertiary language: "Remove Arabic?" / "Names in Arabic are hidden,
    and erased from a record the next time it's saved.";
  - the choices are Cancel, which restores the previous value, and Change languages, styled as
    danger for a removal.
- **A new language's name field** appears after the save lands, since descriptors follow the
  tenant's shape (spec 0029 §4.2). The Languages card's hint says "After saving, enter the
  organization's name in the new language."
- **Step-up notice.** Spec 0031 §5.3's notice, driven by the source's `saveSecurable` (§7.2).
  Only the General tab's securable is sensitive (spec 0013 §5.5).

```ts
// @tellma/core primary
interface TmSettingsTabDeclaration {
  readonly category: string;                                     // a declared category; else unknown-setting-category (spec 0024 Appendix A)
  readonly component: TmLazyComponent;                           // replaces the tab's generic body
}
// @tellma/core/admin
declare function injectTmSettings(): TmSettingsContext;
interface TmSettingsContext {
  readonly details: TmDetailsContext<TmSettingsModel>;           // spec 0031 §4.1, over the source
  readonly categories: Signal<readonly TmSettingCategoryMetadata[]>; // readable, in tab order
  readonly category: WritableSignal<string>;                     // the selected tab
  key(name: string): TmSettingKeyMetadata;                       // the key's metadata (§7.3)
  entry<T = TmJson>(name: string): FieldTree<T>;                 // one key's form node
  isSet(name: string): Signal<boolean>;                          // from setKeys (§7.2)
  editable(category: string): Signal<boolean>;                   // edit mode and a Save grant
  tab(category: string): Signal<Type<unknown> | null> | null;    // the loaded settingsTabs component
}
```

**Example —** Acme's invoicing tab, one feature member and a 13-line component:

```ts
settingsTabs: [{ category: 'invoicing', component: () => import('./settings/invoicing-tab') }],

@Component({ selector: 'acme-invoicing-tab', imports: [TmDetailsKit, TmSettingField], template: `
  <tm-details-card titleKey="Acme_Settings_Numbering">
    <tm-setting-field key="acme.invoicing.prefix" /> <tm-setting-field key="acme.invoicing.padding" />
    <p>{{ t('Acme_Settings_NextNumber', { number: preview() }) }}</p>
  </tm-details-card>` })
export default class InvoicingTab {
  private readonly s = injectTmSettings();
  protected readonly preview = computed(() => this.s.entry<string>('acme.invoicing.prefix')().value()
    + '42'.padStart(this.s.entry<number>('acme.invoicing.padding')().value(), '0'));
}
```

### 7.6 Saving

The source turns the user's changes into one patch, with an expected stamp per entry. Spec 0031
§8 runs the save, and spec 0013 §6.3 owns the server side.

1. **Diff.**
   - Changed typed members go to `fields` (`Name2`, `PrimaryLanguage`).
   - An entry whose canonical JSON changed is listed in `fields` as `entries.<key>`, with its
     value in `entries`.
   - The value is `null` when it equals the default, so later default changes reach the tenant.
   - A secret is listed only when replaced or cleared.
2. **Client checks.** The source's `validate` (spec 0031 §3.1) reports two errors at
   `entries.<key>`: a non-nullable key whose value is `null` (`Required`), and an invalid marker
   (`Settings.EntryTypeMismatch`). The host shows them before any request (spec 0031 §8.1).
3. **Stamps.** `expectedModifiedAt` is the base row's raw stamp, sent only with a typed member.
   `expectedEntryStamps[key]` is the entry's stamp, or `null` when no row existed.
4. **Write** `settings/save`. On success the host takes `details` as the new server copy and
   returns to view mode with the new stamps; the source adopts `client` (§7.7).
5. **Errors.** A 422 lands by `errorSegments`. A 409 opens spec 0031 §9.1's dialog in its
   no-author form; Overwrite resends with `concurrency: 'Override'`.

**Example —** Farah edits two Acme `invoicing` keys and returns the padding to its default:

```json
POST /17/api/web/settings/save
{ "fields": ["entries.acme.invoicing.prefix", "entries.acme.invoicing.padding"],
  "entries": { "acme.invoicing.prefix": "SI-", "acme.invoicing.padding": null },
  "expectedEntryStamps": { "acme.invoicing.prefix": "2026-10-18T09:00:01.5551234+00:00",
                           "acme.invoicing.padding": "2026-10-02T13:40:12.0012345+00:00" },
  "concurrency": "Check" }
200 { "details": { "general": { … }, "values": { "acme.invoicing.prefix": "SI-", "acme.invoicing.padding": 5, … },
                   "setKeys": [ … ], "entryStamps": { "acme.invoicing.prefix": "2026-10-20T10:12:44.7770000+00:00", … } },
      "client": { "tenantId": 17, "names": [ … ], "calendars": ["gc", "uq"], "entries": { "acme.invoicing.prefix": "SI-", … } } }
```

A colleague who changed `acme.sales.defaultCurrencyId` meanwhile causes no conflict.

### 7.7 Effects on the warm cache and other tabs

A save changes the tenant's settings in every cached copy. This section says how this tab, other
tabs and other browsers catch up.

1. **This tab** passes the result to `TmCacheRefresher.adopt(t, { settings: { data: client,
   tag } })` (spec 0025 §5.3). The `tag` comes from the response's header, so that header never
   triggers a sync. The app bar's tenant name, the session menu's calendars and the `Client` keys
   re-render. A Region or Digits change re-formats every value in place (spec 0024 §7.7).
2. **Other tabs** of the browser re-render from `store.changed`; other browsers follow
   `cache.changed { tag: 'settings', value }` (spec 0025 §4.4).
3. **A language change** also bumps `permissions` (spec 0013 §6.1): one `client/sync` brings the
   new `me`, descriptors follow the new shape (spec 0029 §4.2) and spec 0025 §10 forgets the
   tenant's page state.
4. **A Settings page in view mode** in any tab re-reads `details` when the stored document moves
   (the source's `changes`). In edit mode it does not re-read; its save's stamps catch any
   conflict.
5. **The tenant picker** may show the old name until the catalog mirror runs (spec 0013 §6.3,
   step 6).
6. **Refresh caches** is a menu action, `settings.refresh-caches`, enabled with
   `can('core.Settings.General', 'Save')`:
   - it confirms "Refresh caches?" / "Every user's next request reloads settings, permissions and
     lists. Use this after changing data outside {displayName}.";
   - it calls `settings/refresh-caches`, which is sensitive (spec 0013 §6.4);
   - it toasts "Caches refreshed."

## 8. My profile (`/{t}/me`)

Every member edits their own profile through `users/me/save` (spec 0018 §3.5). The page is a
source page that renders from the stored `me` document and sends nothing until Save.

```ts
// @tellma/core/admin — me/
declare function tmMeSource(): TmDetailsSource<UserProfileView>;
@Component({ selector: 'tm-me-layout', imports: [TmDetailsKit, TmUserLocaleFields, TmSignatureField, RouterLink], template: `
  <tm-details-card>
    <tm-field path="Image" /> <tm-field path="Name" /> <tm-field path="Email" readonly hintKey="tm.admin.me.emailHint" />
    <tm-field path="ContactEmail" /> <tm-field path="ContactMobile" /> <tm-field path="Gender" />
  </tm-details-card>
  <tm-details-card titleKey="tm.admin.me.regional"><tm-user-locale-fields scope="language" autoTimeZone /></tm-details-card>
  <tm-details-card titleKey="tm.admin.me.signature"><tm-signature-field path="Signature" /></tm-details-card>
  <tm-details-card titleKey="tm.admin.me.more">
    <a tmButton variant="link" [routerLink]="urls.page('me/notifications')">{{ t('tm.admin.me.notifications') }}</a>
    @if (accountUrl(); as url) { <a tmButton variant="link" [href]="url" target="_blank">{{ t('tm.admin.me.account') }}</a> }
  </tm-details-card>` })
export class TmMeLayout {}                                       // reads TmUrls and the account URL
```

The source's members, as in §7.2:

| Member | Value |
|---|---|
| `kind` | `'settings'` |
| `entity` | `'core.User'`, for labels, types and blob policies |
| `id` | `me.user.id` |
| `titleKey` | `tm.admin.me.title` ("My profile") |
| `draftKey` | `TmDrafts.key(t, 'core.User/me', me.user.id)`: apart from the draft of the user's own `/users/{id}` page; spec 0034 §12.1 keys the studio the same way |
| `read` | resolves at once to `{ entity: TmLaunchCache.docs.me().user }`, with no request |
| `write` | `users/me/save` → `MeResult`; see **Save** below |
| `can` | `edit`: true; the rest false |
| `category` | `'profile'` (spec 0026 §9.3) |
| `errorSegments` | `preferredLanguage` → `['preferredLanguage']`: `users/me/save` paths carry no row index |
| `changes` | the stored `me` document |

- **Save.** The source sends the thirteen self-editable members, `modifiedAt` from the base and
  `concurrency` to `users/me/save`. It adopts the returned `MeResult` with `adopt(t, { me })` (spec
  0025 §5.3).
- **Route.** `/{t}/me` (spec 0024 §9.2) loads `TmMePage`: `tm-details-page` over `tmMeSource()`
  and `TmMeLayout`, as in §7.2. Links use `TmUrls.page('me')`.
- **Header.** "My profile" with Edit, Save and Cancel; no crumb, pager or Actions menu.
- **No step-up.** `users/me/save` is self-service (spec 0018 §3.5), so the source declares no
  `saveSecurable` and no step-up notice shows.
- **Email** is read-only, with the hint "Your sign-in email. Change it in My account." The account
  link is spec 0027 §10.1's row 3 URL.
- **Language and calendar** offer the tenant's sets. A saved language re-renders the app through
  the adopted `me` (spec 0024 §7.1). A saved region or digits value re-formats the app in place
  (spec 0024 §7.7).
- **Automatic time zone.** `tm-auto-time-zone` is a `tm-checkbox`:
  - its label is "Change my time zone automatically when this device's time zone changes", and its
    hint "Saved on this device only.";
  - it reads `TmDeviceZone.auto` and writes `setAuto(on, 'profile')` (spec 0024 §7.8);
  - it applies at once in view and edit mode, and stays out of Save, the draft and the dirty state;
  - the Users page never shows it.
- **Conflicts.** Three writers call `users/me/locale` and move `ModifiedAt`: a session-menu switch
  (spec 0027 §12.1), the time-zone prompt and the automatic time-zone update (spec 0024 §7.8). In
  view mode the page re-renders from the adopted `me`. In edit mode Save meets spec 0031 §9.1's
  self-case dialog, and Overwrite resends with `Override` (spec 0018 §3.5).

## 9. Schedules

The schedule page is a layout over spec 0020 §7's stack, plus one rule for built-in schedules. A
built-in schedule locks every field except its names, `CronExpression` and `TimeZoneId`. It also
hides Deactivate, Take over and Delete.

```ts
// @tellma/core/admin — schedules/schedule-details.ts
@Component({ selector: 'tm-schedule-details', imports: [TmDetailsKit, TmTimeZoneField, TmJobHandlerField, TmJsonField, TmScheduleStatus], template: `
  @if (d.entity()?.pausedReason; as reason) { <tm-alert kind="warning">{{ t('tm.admin.schedules.paused.' + reason, { name: owner() }) }}</tm-alert> }
  <tm-details-card>
    <tm-field path="Name" /> <tm-field path="Code" [readonly]="builtIn()" />
    <tm-job-handler-field path="HandlerKey" [readonly]="builtIn()" />
    <tm-field path="CronExpression" hintKey="tm.admin.schedules.cronHint" /> <tm-time-zone-field path="TimeZoneId" />
    <tm-field path="MissedPolicy" [readonly]="builtIn()" /> <tm-field path="OverlapPolicy" [readonly]="builtIn()" />
    <tm-field path="CatchUpWindowMinutes" [readonly]="builtIn()" /> <tm-field path="RunAsUser" />
    <tm-json-field path="ArgumentsJson" [readonly]="builtIn()" [span]="2" />
  </tm-details-card>
  <tm-details-aside titleKey="tm.admin.schedules.status"><tm-schedule-status /></tm-details-aside>
  <tm-details-audit />` })
export class TmScheduleDetails {
  private readonly d = injectTmDetails<TmEntityType<'core.Schedule'>>();
  protected readonly builtIn = computed(() => (this.d.id() ?? 1000) <= 999);   // spec 0020 §7.1's reserved band
}
```

- **Built-in** (`Id ≤ 999`). The page shows a "Built-in" title badge. A built-in's name is its
  handler key until renamed (spec 0020 §7.9).
- **How the actions hide.** §3's `visible` hides Deactivate and Take over. Delete hides because the
  stack's `permissions` extra (spec 0020 §7.8) answers `delete: false` for a built-in, and spec
  0031 §4.1's `can` reads it.
- **Job** (`tm-job-handler-field`) is write-once. Create mode offers the metadata's `jobHandlers`
  marked `schedulable` (spec 0020 §3.7), labelled as spec 0028 §11.2 says. View mode shows the
  label and the key in mono. The search column's `TmJobHandlerCell` renders the same label.
- **`CronExpression`** is mono LTR, with the hint "Five fields: minute, hour, day of month, month,
  day of week. Example: 0 6 * * 1-5 runs at 06:00 on weekdays."
- **`TimeZoneId`** is nullable ("Tenant default (Asia/Riyadh)").
- **Validation** is the server's (`Schedules.*`, spec 0020 §7.8).
- **`tm-json-field`** wraps `tm-field` for a JSON text column. View mode shows the JSON formatted,
  mono, LTR and collapsed beyond 6 lines. Edit mode is a 6-row mono `textarea`.
- **JSON check.** The declaration's `validate: tmJsonTextSchema('argumentsJson')` (spec 0031 §7.2)
  rejects text that `JSON.parse` refuses: "This isn't valid JSON." `tmJsonTextSchema` is a schema
  helper in the primary, beside the declarations.
- **Paused notice.** A warning alert explains the `PausedReason`:

| `PausedReason` | Notice |
|---|---|
| `OwnerInactive` | "Paused because its owner, {name}, is inactive. Take it over, then activate it." |
| `Exhausted` | "Paused because its expression never occurs again. Edit the expression, then activate it." |
| `HandlerRetired` | "Paused because its job no longer exists in this version." |

- **Status aside** reads the eager `state` extra (spec 0020 §7.8). It shows Next run (in the
  display zone, plus relative time), Last run, Last occurrence, Last skipped, and Last job as a
  link to `/{t}/jobs/{id}`. It shows "Not scheduled" when `NextDueAt` is null.
- **Run now** answers `202 JobAccepted`. The page shows spec 0031 §10.4's "Started in the
  background." toast with Track, then re-reads. `Schedules.RunInProgress` shows in the 400 dialog.

## 10. Outgoing emails

The email page shows what was sent, to whom, its delivery and its exact body, read-only. Retry and
Cancel are the stack's entity actions (spec 0022 §7.1).

### 10.1 The layout (`TmOutgoingEmailDetails`)

The page has four cards:

| Card | Content |
|---|---|
| Message | `From`, `ReplyTo`, `To`, `Cc`, `Bcc`, `Subject` (span 2), `SendAfter`, `Audience`, `RecipientUser`, Regarding |
| Delivery | `State`, `AttemptCount`, `SentAt`, `DeliveryStatus`, `DeliveryAt`, `DeliveryRecipient`, `DeliveryReason`, `ProviderMessageId`, `Job`, `ErrorCode` (`Email_<code>` through `TmServerText`), `ErrorDetail` (verbatim) |
| Body, `flush` | `<tm-email-body [html] [text] [attachments]>` (§10.2) |
| Attachments | `<tm-child-files collection="OutgoingEmailAttachments" file="File" />`, view only |

- **Title** is the subject (`[DisplayNameSource]`, spec 0022 §2.1).
- **Badges.** The state, then the delivery status when set. The state's badge is success for Sent,
  neutral for Sandboxed and Cancelled, accent for Pending and Sending, and error for Rejected and
  Failed.
- **Regarding** reads "{entity label} {display name}", a link to
  `TmUrls.target(RegardingEntity, RegardingId)` when known and readable, else the raw values.
- **Bodies** arrive with the details read, which returns every mapped property (spec 0015 §5.3).
- **Attention.** The search page's `attention` filter (§3) is the target of
  `/{t}/outgoing-emails?x.attention=1`.

### 10.2 The body viewer (`tm-email-body`)

`tm-email-body` shows an email's HTML in a sandboxed frame, or its plain text. Nothing remote ever
loads.

```ts
@Component({ selector: 'tm-email-body' })
declare class TmEmailBody {
  readonly html: InputSignal<string | null>;
  readonly text: InputSignal<string>;
  readonly attachments: InputSignal<readonly { contentId: string | null; fileId: number | null }[]>;  // resolves cid: images
  readonly mode: ModelSignal<'html' | 'text'>;                   // default 'html' when html is set
}
```

1. **Inline images.** The viewer fetches each `cid:<id>` that has a matching attachment through
   `TM_BLOB_FETCHER`, and replaces it with a `data:` URL. The fetch aborts on destroy. An image
   over 2 MiB stays unresolved.
2. **Document.** The viewer prefixes the HTML string with two tags. It never parses the string,
   because `DOMParser` is a Trusted Types sink.
   - `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:;
     style-src 'unsafe-inline'; font-src data:">`. The frame loads nothing remote, whatever policy
     a `blob:` document inherits in a given engine.
   - `<base target="_blank">`.
3. **Frame.** The document becomes a `text/html` `Blob`. Its object URL is the `src` of
   `<iframe sandbox="allow-popups allow-popups-to-escape-sandbox" referrerpolicy="no-referrer"
   title="Email body">`.
   - The sandbox means no scripts, an opaque origin, and links that open in a new tab.
   - `src` is not a Trusted Types sink; `srcdoc` is.
   - The URL is revoked on destroy.
4. **Geometry.** The frame and the text view share one fixed height, 480 px (320 px below `sm`),
   and scroll inside.
   - A `tm-segmented` HTML / Text sits above.
   - "Remote images and links to them aren't loaded." always sits below.
   - Text renders in a wrapping `pre` with `dir="auto"`.

## 11. Exports and Imports

Members find their own files and import results through the stacks' self-scope. An administrator
with a stored grant sees all (spec 0019 §12.1). Neither stack saves, so both are search pages
with view-only details.

- **`TmEntityNameCell`** renders `Resource` as the entity's plural label, such as "Centers". An
  unknown name shows as is. The column's text filter matches the stored name, so "center" finds
  `gl.Center`.
- **`TmDownloadCell`** renders the export's file as a download link,
  `TmUrls.blob('export-file', fileId, { download: true })`.
- **`Job.Status`** shows beside `ErrorCount` on Imports. It is readable through the job's related
  projection (spec 0020 §2.3).
- **Empty states:** "Exports you run in the background appear here for 7 days." and "Imports you
  start from a list appear here."

The details pages:

- **`TmExportDetails`** shows a primary Download link button, then `Resource` (the plural label,
  linking to the entity's search page), `Kind`, `File`, `RowCount`, `ExpiresAt`, `Job` and
  `CreatedBy`. A collapsed "Request" card holds `tm-json-field path="RequestJson"`.
- **`TmImportDetails`** shows `Resource`, `Mode`, `File` (the uploaded workbook), `RowCount`,
  `ErrorCount`, `Job`, `ExpiresAt` and `CreatedBy`. A "Result" card holds
  `<tm-import-result-view [result] [running]>` (spec 0030 §13.3) over `ResultJson`. The same
  collapsed "Request" card follows.
- **Expiry** shows "In 6 days" with the date. An expired row is gone (spec 0019 §12.1).
- **A running import** shows "Importing… {percent}%" from the job's progress.
- **Live refresh.** The import page calls `injectTmDetails().refresh()` when
  `TmLeader.events('job.changed')` (spec 0025 §7.1) names this import's job. The event reaches the
  requester only.
- **No push elsewhere.** The other admin pages get no push; Refresh re-reads them.

## 12. Server and metadata changes

The server members these pages read are specified in their owner specs; this section lists them.
It owns one thing itself: the label chains of the `securables` section (§12.4).

### 12.1 Settings (spec 0013)

| What the pages read | Where |
|---|---|
| `settingKeys` and `settingCategories` in the Core chunk, written by `SettingsClientAssetContributor` | spec 0013 §1.2, §5.3 |
| `TenantSettingsDetails` with `EntryStamps` and no key descriptors | spec 0013 §6.1, §6.2 |
| `TenantSettingsPatch` with per-entry stamps and `Concurrency`; `SettingsSaveResult(Details, Client)` | spec 0013 §6.1, §6.3 |
| `Region` and `Digits` on `TenantSettings`, `TenantSettingsForClient` and `TenantSettingsPatch`; `Settings.RegionNotOffered` | spec 0013 §5.6, §6.1, §6.5 |
| each `languages` entry's `region`, `regions` and `nativeDigits`, which the region and digits fields list | spec 0013 §1.2, §7.2 |
| the startup gate "a nullable key defaults to `null`", which makes §7.6's reset rule sound | spec 0013 §5.3 |
| `SettingCategory_`, `SettingGroup_`, `Setting_<key>_Description`, `Securable_`, `Access_Problem_`, `Access_Reason_` keys | spec 0013 §10.1 |

### 12.2 Users and roles (specs 0014, 0016, 0018)

- **`UserProfileView.ModifiedAt`** (spec 0016 §3.4), filled by the connect prologue's profile set
  (spec 0014 §7.1). `MeResult` carries no securables fingerprint.
- **`MeSaveRequest.Concurrency`.** `Override` skips the stamp comparison (spec 0018 §3.1, §3.5).
- **Regional members.** `PreferredRegion` and `PreferredDigits` on `User` (spec 0014 §3.1) and
  `UserProfileView` (spec 0016 §3.4); `MeSaveRequest`'s thirteen members and
  `Users.RegionNotOffered` (spec 0018 §3.1, §3.2).
- **The `activity` extra** on the users stack (spec 0018 §3.1).
- **The `members` and `problems` extras** on the roles stack (spec 0018 §4.3). `members` is
  filtered by the caller's `core.User × Read` and capped at 500. `problems` follows spec 0014 §9's
  five conditions.

### 12.3 Jobs, schedules and emails (specs 0018, 0020, 0022)

- **The `state` extra** on the schedules stack (spec 0020 §7.8).
- **The `permissions` extra** on the schedules stack (spec 0020 §7.8): `delete` is false for a
  built-in.
- **`jobHandlers`** in the Core chunk (spec 0020 §3.7), mirrored as `TmJobHandlerMetadata`
  `{ key, schedulable, labelKey, labelFallback? }`.
- **`Job`'s `[RelatedSelect]`** (spec 0020 §2.3), so `Job.Status` and the label pass the traversal
  rule for any reader of an export, import or email.
- **`[DisplayNameSource(nameof(Subject))]`** on `OutgoingEmail` (spec 0022 §2.1).
- **GL strings** `Gl_Nav_Financials` ("Financials" / "المالية") and `Gl_Center_CodeHint` (spec
  0018 §5.1).

### 12.4 The securables section

`SecurablesClientAssetContributor` writes one `ClientSecurable` per registered securable (spec 0014
§10). The primary mirrors it in camelCase as `TmSecurableMetadata`.

The emit picks each label key from a chain: the first key in the chain that a neutral `Strings`
resource declares wins. Spec 0029 §2.4 resolves entity labels the same way.

| Label | Chain |
|---|---|
| an entity resource | its plural key (`Gl_Center_Plural`) |
| `core.Settings.General`, `core.Settings.<c>` | `SettingCategory_general`, `SettingCategory_<c>`; the editor shows "Settings: {category}" |
| any other resource | `Securable_<resource with '.' → '_'>` (`Securable_acme_CreditExposureReport`), else the humanized last segment |
| an action | `<Schema>_<Entity>_Action_<Name>`, then Core's `Action_<Name>` (spec 0013 §10.1), else the humanized name; `*` is "All actions" |

Acme's section has about 150 items, under 3 KB gzip. It has four readers:
`TmPermissions.sensitive` and `TmPermissions.label` (spec 0029 §7), the permissions editor (§5.2)
and the Access tab (§4.6).

## 13. Testing

Five suites pin this design, and all of them run on PR.

- **Unit (vitest).** The settings diff (typed members, entries, default → `null`, secrets, stamps,
  client checks); `errorSegments`; groups and tab order from fixture metadata; filter roots for
  `*`; every `visible`; the email document prefix and `cid:` rewriting.
- **Harness (fake `TmApi`).**
  - Settings: tabs from access; read-only categories; §7.6's body; 409 → Overwrite → `Override`;
    language confirmation; the Formats card's defaults; adoption; a view-mode re-read on a moved
    document.
  - Roles: grid ↔ panel; live diagnostics; drift markers; members states and the cap line.
  - Users: invite outcomes (all sent, mixed, 422, 502 with retry); credentials shown once and the
    close guard; access chunks of 200; access labels equal to `TmPermissions.label`, and a search
    by label or raw pair.
  - Locale fields: `tm-region-field` options per scope, with samples; `tm-digits-field` hidden
    without native digits, and its native option's system under each scope; `tm-auto-time-zone`
    applies at once and stays out of Save, the draft and the dirty state.
- **e2e on Acme** (Playwright; Chromium; Firefox and WebKit `@cross-engine`).
  - Settings: two administrators edit different `invoicing` keys (both save) and the same key
    (conflict, Overwrite); add a language, then its name field appears; replace and clear the
    secret; the invoicing tab's preview.
  - Users: create, invite, Check delivery; credentials and Copy; a restricted role (no Access tab).
  - Roles: a filtered permission checked and saved; drift.
  - Centers: the tree and Acme's override.
  - My profile: warm with requests held, a save, a region change re-formatting the page, a
    session-menu switch while editing.
  - Schedules: run-now, built-in locks and the hidden Delete.
  - Outgoing emails: an email with HTML, a `cid:` image and a blocked remote image.
  - Imports: an import's page refreshing on `job.changed`.
- **axe.** Every page, tab, dialog, notice and empty state; light and dark, LTR and RTL.
- **Budgets.** §1's sizes: the `/admin` chunk, the dialogs, the primary's declarations and
  `@tellma/module-gl`'s primary.

## 14. Definition of done

- **Behaviour.** §3–§11 behave as stated and every §13 suite is green. The server members of §12
  exist in their owner specs.
- **UI rules.** Each rule says how the design meets it, then which test pins it.
  - **Instant warm launch.** Stock chrome is spec 0030's and 0031's. Settings' tabs, labels and
    controls come from the metadata and cached access (§7.1, §7.3). My profile renders entirely
    from the stored `me` (§8). *Test:* spec 0024's `warm-launch.spec` on `/17/settings/invoicing`,
    `/17/me` (values too) and `/17/roles/4`, with every request held: chrome within 1 s.
  - **Instant feedback.** Edit, tab switches and the permissions panel change in the click's
    frame. Securables, zones, regions and their samples are local (§5.2, §4.2). *Test:*
    `admin-feedback.spec`, a rAF probe after Edit, a tab click and a grid row change.
  - **Non-blocking.** `settings/details`, `invitation-status`, `access/check`, `queryex/discover`
    and `cid:` fetches abort on leave or supersession; writes never abort. *Test:* a harness case
    where leaving the Access tab aborts its chunks.
  - **Stable layout.** Fixed heights for the panel, the frame, the status aside and the delivery
    line (§5.3, §10.2, §4.5). Reserved "Default" and reset boxes (§7.4). The step-up notice sits
    in spec 0031's overlaying strip (§7.5). *Test:* `settings-geometry.spec` and
    `role-editor-geometry.spec`, boxes identical across load, Edit, typing, a 422 and a reset.
  - **RTL.** Logical properties. Filters, cron, JSON, zone ids, client ids and secrets sit in LTR
    isolates. The email frame keeps the email's direction. *Test:* every §13 e2e in `ar` at 390,
    768, 992 and 1440 px, and `email-body-rtl.spec`.
  - **WCAG AA.** Settings tabs are spec 0005's `tablist`. The permissions grid and panel work from
    the keyboard (Tab, F2, F8), and Access rows expand with Enter. Dialogs trap and return focus,
    and Copy announces "Copied". Outcome, state and drift markers pair an icon with text. *Test:*
    `admin-keyboard.spec` (invite, issue and copy credentials, add a filtered permission and fix it
    from the summary, change and reset a setting), and axe on every page, tab, dialog and state.
- **Page context.** The entity pages publish spec 0030's and 0031's contexts. Settings and My
  profile publish kind `settings` through spec 0031 §16, with the category from their sources
  (§7.2, §8) and the host's `form` (`Name`, `entries.acme.invoicing.prefix`).
  - A secret's field carries no value. The credentials dialog publishes no values (§4.4).
- **Page actions** beyond the frameworks': `settings.refresh-caches` (menu, confirms) and the entity
  actions `core.user.invite`, `core.user.issue-credentials`, `core.schedule.run-now`,
  `core.schedule.take-over`, `core.outgoing-email.retry` and `core.outgoing-email.cancel`.
- **Telemetry** under `tm.admin`, with closed properties (spec 0026 §3.2). Events:
  - `tm.admin.settings.saved { categories: 1 | 2-3 | 4+, typed, languages, outcome }`
  - `tm.admin.users.invite { count: 1 | 2-10 | 11+, outcome: ok | mixed | invalid | partial | error }`
  - `tm.admin.users.credentials { rotation, copied, outcome }`
  - `tm.admin.roles.problems { count: 0 | 1 | 2+ }`
  - `tm.admin.email.body { mode: html | text, inlineImages }`
  - `tm.admin.profile.saved { outcome, language }`
  - Timings: `tm.admin.users.delivery { outcome }` and `tm.admin.users.access { chunks }`.
- **What Acme adds.**
  - The centers override and `my-center-details` (§6), and the `invoicing` tab (§7.5).
  - Spec 0018 §1.4's keys: a string, an int, a nullable reference, an enum array and a secret,
    across three categories.
  - E2e fixtures that create the users (New, Invited, Joined, a service account) and the roles
    they exercise.
  - Drift: a role whose `Name3` filter was saved while the tenant had three languages, before the
    tertiary language is removed.

## Decisions record

1. **Every stock page is a declaration on specs 0030 and 0031** — layouts are markup and a
   distribution writes nothing; a component per page would re-implement both frameworks (§3).
2. **A pack's primary holds declarations; its layouts ship in a secondary entry point** — eager code
   may not import `/details` (§1, §6).
3. **Settings and My profile are source pages on spec 0031's host** — edit mode, save, drafts,
   conflicts and the error summary come free; bespoke pages would duplicate them (§7.2, §8).
4. **One Settings page with one edit mode and a tab per readable category, selected by the URL** —
   one Save sends every dirty field; per-tab edit modes would multiply drafts and guards (§7.1).
5. **Setting-key descriptors ship in the client metadata** — tabs, labels and controls render with
   no request; `details` carries values and stamps only (§7.3).
6. **Groups and tab order are derived** — from key names and registry order, so a pack declares
   nothing extra (§7.3).
7. **One stamp per stored entry; the typed row's stamp only with a typed member** — different keys
   never conflict, and an entries-only save needs no General grant. Rejected: a stamp per category,
   in a new table or as a digest (a reset deletes its row; keys in one category conflict falsely);
   the typed row's stamp on every save (§7.6).
8. **A value equal to its default saves as a reset** — later default changes reach the tenant
   (§7.6).
9. **Secrets are write-only** — "Set" or "Not set", Replace and Clear; no value ever reaches the
   client (§7.4).
10. **Setting fields reuse spec 0029's typed controls through a value descriptor** — one display
    and one control per type on every surface (§7.4).
11. **Language, calendar, time-zone, region and digits fields are platform components; zones come
    from `Intl.supportedValuesOf`, regions from the metadata's offered list** — the server
    validates. An emitted zone list would follow the build machine's ICU, not the host's; the
    offered regions are a frozen list both ends share (§4.2).
12. **Permissions edit in a grid with one filter panel, checked live by `queryex/discover`** —
    filters in grid cells would be unreadable; drift comes with the read (§5.3, §5.5).
13. **Role members are a read-only, row-filtered, capped extra** — no hidden user leaks and no
    route is added; memberships stay a property of the user (§5.4).
14. **Invite and Issue credentials are entity actions with declared dialogs** — they confirm, run,
    show per-row results or the one-time secret, and retry a subset; delivery is checked on demand
    (§4.3–§4.5).
15. **Email HTML renders from a `blob:` URL in a sandboxed frame with its own CSP** — no scripts, no
    remote loads, `cid:` images inline; `srcdoc` would be a Trusted Types sink (§10.2).
16. **The Access tab names each pair with `TmPermissions.label` and also matches raw names** — the
    words a `forbidden` message shows the user find the administrator's row at once (§4.6).

## Review flags

1. **Per-entry stamps, not per category** (§7.6). Flips if a category's keys must save as one
   consistent set.
2. **One edit mode for the whole Settings page** (§7.1). Alternative: edit and save per tab. Flips
   if administrators want separate saves per category.
3. **Groups derived from key names** (§7.3). Alternative: a `group` argument on `SettingKey`. Flips
   when a pack's naming cannot express its grouping.
4. **A value equal to its default resets** (§7.6). Flips if tenants must pin today's default.
5. **JSON and scalar-array keys edit as text** (§7.4). Alternatives: hide JSON keys that have no
   `settingsTabs` component; a free-entry chip input. Flips on misuse or a second consumer of
   chip entry.
6. **Field components, not a metadata `format`** (§4.2). Flips when distributions add many
   language, calendar or zone properties.
7. **Role members read-only and capped at 500** (§5.4). Alternatives: add and remove members on the
   role; a paged child-query route. Flips if administrators reject editing from the user's page.
8. **The Access tab checks every securable** (§4.6). Alternative: one securable at a time. Flips on
   measured slowness (`tm.admin.users.access`).
9. **Delivery is checked on demand** (§4.5). Alternative: read it when the aside renders. Flips if
   administrators press Check delivery on most visits.
10. **Signatures are uploaded images, never drawn** (§4.7). Alternative: a drawing pad. Flips if
    users ask to sign on screen.
11. **No administrator Notifications tab on the user page**: notification settings stay
    self-service (spec 0028). Flips if administrators must change others' settings.
12. **My profile meets the conflict dialog after a same-tab locale switch or time-zone update while
    editing** (§8). Alternative: rebase when only locale fields moved. Flips on user reports.
13. **Remote email images are always blocked** (§10.2). Alternative: a "Show remote images" switch.
    Flips if administrators must see emails exactly as sent.
14. **No entity filter on Exports and Imports**; the `Resource` column's text filter stands in
    (§11). Alternative: a data-driven filter, which waits for spec 0030's component hatch (its
    review flag 14). Flips on that hatch.
15. **Region lists differ by page** (§4.1, §4.2): My profile lists the regions of the language the
    user reads; the user page and the Formats card list every offered region. Alternative: one
    list everywhere. Flips if users miss a region they need, or administrators pick one that no
    language of the tenant uses.
