# Spec: Stock Pages

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every Tellma distribution ships the same administration pages. The platform provides Users, Roles,
Settings, My settings, Schedules, Outgoing emails, Exports and Imports. The GL pack provides
Centers. This spec ships them. A distribution writes nothing to get them, and a few lines to extend
one.

- **Seven entity pages.** Each is a declaration on the search page (spec 0030) and the details
  page (spec 0031), plus a markup-only layout. A few page-specific components fill the gaps: the
  permissions editor, the membership grid, the user's Sign-in and Access tabs, the invite,
  credentials and preferences dialogs, and the email body viewer.
- **One access page.** `/{t}/access` answers "who holds this permission" and "what can this user
  do" (§4.9).
- **Two source pages.** Settings and My settings' Profile section are details pages over a source
  that is not an entity. Editing, saving, conflicts, drafts and the leave guard therefore come from
  spec 0031 too. My settings' other sections save each change themselves.

The server side already exists in specs 0013, 0014, 0016, 0018, 0019, 0020 and 0022, and §12
lists the members these pages read. This spec adds the client view of setting keys and securables.

Other specs that carry parts of this design:

- spec 0024 §2.2, §7.2, §7.7, §7.8, §9.2, Appendix A — the `settingsTabs` feature member; the
  locale write, the regional formats and the device time zone that the locale fields edit; the
  `settings` and `me` routes; the `unknown-setting-category` check.
- spec 0027 §3.2 — the Administration section and the order of its entries.
- spec 0010 §C.14, §C.15 — the row-end column; popup cell editors for a permission's filter and a
  membership's window.
- spec 0013 §5.3, §7.7 — a setting key's `group`; the value sets behind the locale fields.
- spec 0023 §6.4 — `tm-date-time-field`.
- spec 0029 §3, §7, §12.1 — `actions[].visible`, the column `filter`; the tenant's state in
  `TmPermissions`; `TmValueDescriptor`.
- spec 0030 §13.3 — `TmImportResultView`.
- spec 0031 §3.1, §4.1, §5.3, §6.1, §6.6, §10.4 — `TmDetailsSource` and `tm-details-page`; the
  quiet refresh and `refused`; the step-up notice; the card section; the tabs' `selected` model;
  action dialogs with `run({ keepOpen, ids, rethrow })`.

## Goals / Non-goals

**Goals**

- Every stock page works with no distribution code; extending one costs a declaration override and
  a layout that reuses the stock field groups.
- Settings renders its tabs, labels and controls from the metadata, so a warm launch shows the
  page before any request. Two administrators editing different keys never conflict.
- My settings renders entirely from stored documents. Profile saves through `users/me/save`, and
  each other section saves its own changes.
- The role editor checks a permission's filter as the user types and shows drifted permissions.
- An administrator can invite users, issue service credentials, grant a role for a time window,
  change a user's preferences for them, see why a user can or cannot do something and who holds a
  permission, and read exactly what an outgoing email contained.

**Non-goals (explicitly out of scope)**

- **Other platform pages.** Jobs, Notifications and Assignments, and the body of My settings'
  Notifications section, belong to spec 0028. Support requests belong to spec 0026.
- **The frameworks underneath.** Search and details pages (specs 0030, 0031), the entity model and
  typed controls (spec 0029), and generic components (spec 0023).
- **Deferred features.** Editing role memberships from the role page; "Schedule this export"; a
  cron preview; showing remote images in emails; drawing a signature on screen; exporting the
  access page's holders.
- **Self-only settings.** An administrator never changes another user's device settings (theme,
  automatic time zone) or credentials, and never sends them a test message.

## 1. Placement and vocabulary

The declarations are data in the eager primary entry point. Everything they render loads in one
lazy chunk, `@tellma/core/admin`. The Centers page ships in the GL pack's package.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

| Part | Entry point | Folder (`client/projects/`) | Loaded |
|---|---|---|---|
| the six declarations, `tmAccessPage`, `TmSettingsTabDeclaration`, the settings metadata types | `@tellma/core` | `core/tellma-core/src/admin/` | eager, ≤ 2 KB gzip |
| layouts, field groups, the Settings page, the My settings hub, the access page, editors, cells, dialogs | `@tellma/core/admin` | `core/tellma-core/admin/` | lazy, one chunk, 31 KB self-weight |
| `tmGlFeature()`, the `gl.Center` declaration | `@tellma/module-gl` | `module/tellma-module-gl/src/` | eager, < 1 KB |
| `TmGlCenterDetails`, `TmGlCenterFields` | `@tellma/module-gl/centers` | `module/tellma-module-gl/centers/` | lazy |

- **What `/admin` imports.** The primary, `/entity`, `/search` (for the import result view),
  `/details` and `/attention` (for job labels). It reaches `/attention-pages` only through
  `import()`: the hub's Notifications section (spec 0028 §10.3) and a `@defer (on immediate)` block
  in `TmUserPreferencesDialog` for `tm-notification-settings` (§4.8). That chunk counts toward
  neither `/admin`'s budget nor the dialog's.
- **Lazy only.** The declarations reach `/admin` only through `import()` (spec 0024 §1).
- **Packs split the same way.** A pack's primary holds declarations only. Its layouts ship in a
  secondary entry point, because they import `/details`.
- **Dialogs.** The invite, credentials and preferences dialogs load with `import()` on first use,
  ≤ 4 KB each.
- **Testing exports** (`@tellma/core-testing`): `TmSettingsPageHarness`,
  `TmPermissionsEditorHarness`, `TmMembershipsHarness`, `TmUserAccessHarness`,
  `TmAccessPageHarness`, `tmFakeSettingsDetails()`.
- **Strings.** Client scope `tm.admin`; server keys per spec 0013 §10.1.

**Vocabulary**

- *Stock page* — a page this spec ships: the seven entity pages, Settings, My settings and the
  access page.
- *Source page* — a details page on spec 0031's host fed by a `TmDetailsSource` that is not an
  entity route: Settings and My settings' Profile section.
- *Field group* — a component that renders a page's stock fields inside a card, such as
  `TmUserFields` or `TmGlCenterFields`, so a distribution's layout can reuse them.
- *Extra* — a named result the details read returns beside the record, such as a role's `members`
  (spec 0015 §5.3). Every extra a page lists is *eager*: it arrives with every read (spec 0031 §6).
- *Securable* — a resource and action pair that a permission grants (spec 0014 §2). A *sensitive*
  securable makes the user confirm their identity first; that confirmation is the *step-up* (spec
  0014 §4.3).
- *Filter root* — the entity a permission's filter is written over (spec 0014 §3.7).
- *Category* — a settings tab: `general` (the typed row plus `general` keys) or a key category.
- *Group* — a titled card inside a category, declared by a key or derived from its name (§7.3).
- *Window* — a role membership's `[StartsAt, EndsAt)`, null bounds unbounded; a membership is
  *current* while its window holds the server time (spec 0014 §3.6).
- *Holder* — a user a securable reaches through their roles (spec 0014 §5.1).
- *Entry stamp* — the `ModifiedAt` of a key's `core.SettingEntries` row, or `null` when none exists.
- *Drift* — a stored permission that grants nothing (spec 0014 §9).
- *Built-in schedule* — a schedule whose id is in the reserved band, `Id ≤ 999` (spec 0020 §7.1).

## 2. What a distribution writes

A distribution writes nothing for the stock pages. The table lists the uncommon needs and what
each costs in lines.

| Need | What the distribution writes | Lines |
|---|---|---|
| Centers with an extra field | an override and a layout reusing `TmGlCenterFields` (§6, spec 0024 §2.3) | 4 + 6 |
| Users with extra columns (`MyUser : User`) | an override and a layout reusing `TmUserFields`, `TmUserTabs` | 4 + 6 |
| Roles with extra columns (`MyRole : Role`, spec 0018 §1.2) | an override and a layout reusing `TmRoleFields`, `TmRoleTabs` | 4 + 6 |
| Its own settings | `SettingKey<T>` fields in a C# holder (spec 0013 §5.3) and resx labels | 0 client |
| A settings tab with its own editor | `settingsTabs: [{ category, component }]` and a component (§7.5) | 1 + ~14 |
| Hide a stock launcher entry; the page stays reachable (spec 0024 §2.3) | `hide: ['entry:core.Schedule']` | 1 |
| Drop an earlier feature's settings-tab editor; the category's generic tab returns (spec 0024 §2.3) | `hide: ['settingsTab:invoicing']` | 1 |

**Example —** a distribution whose `MyUser` adds `EmployeeNumber` overrides `details` only:

```ts
export const usersOverride = tmEntity('core.User', { details: () => import('./my-user-details') });

@Component({ selector: 'acme-user-details', imports: [TmDetailsKit, TmUserFields, TmUserTabs],
  template: `<tm-details-card><tm-user-fields /><tm-field path="EmployeeNumber" />
               <tm-user-tabs tmDetailsCardSection /></tm-details-card><tm-details-audit />` })
export default class MyUserDetails {}
```

The override replaces only `details`, so the declaration's actions stay (§3), and `tm-user-fields`
brings the State badge with it (§4.1).

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
    { name: 'notification-preferences/save', icon: 'sliders-horizontal', button: true,            // "Preferences" (§4.8)
      dialog: () => admin().then(m => m.TmUserPreferencesDialog), visible: (u) => u.kind === 'Individual' },
    { name: 'preferences/set', visible: () => false }, { name: 'preferences/delete', visible: () => false }, // the dialog's
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
      visible: (e) => e.state === 'Pending' || e.state === 'Sending' || e.state === 'Failed' }, // a live job: Email.NotRetryable (spec 0022 §7.1)
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
export const tmAccessPage = tmPage('access', {                  // §4.9
  component: () => admin().then(m => m.TmAccessPage), titleKey: 'tm.admin.access.title', kind: 'access',
  section: 'administration', order: 25, icon: 'shield-check', securable: ['core.Role', 'Read'],
});
```

- **`visible`** (spec 0029 §3) hides a promoted button or menu item for one record, built-in
  actions included. It never replaces the server's check. Search-page bulk actions ignore it, and
  the server refuses row by row instead (spec 0030 §12.2).
- **Launcher.** The `order` values place the entries in spec 0027 §3.2's Administration section.
  Settings is the core entry `core.settings` (§7.1), and the access page's entry is `access`, at
  order 25 (§4.9). My settings is `core.me`, a search-only entry, with one for each section (spec
  0027 §3.2).
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

The user page is two cards: one holding the user's fields and, below them, tabs for role
memberships, sign-in and effective access; then the collapsed audit card. Invite, Issue
credentials and Preferences are entity actions with their own dialogs.

### 4.1 The layout and field groups

The user layout is built from two reusable parts, the fields and the tabs, neither with a card of
its own. A distribution's own layout reuses them (§2).

```ts
// @tellma/core/admin — users/
@Component({ selector: 'tm-user-details', imports: [TmDetailsKit, TmUserFields, TmUserTabs], template: `
  <tm-details-card><tm-user-fields /><tm-user-tabs tmDetailsCardSection /></tm-details-card> <tm-details-audit />` })
export class TmUserDetails {}

@Component({ selector: 'tm-user-tabs', imports: [TmDetailsKit, TmUserMemberships, TmUserSignIn, TmUserAccess], template: `
  <tm-details-tabs>
    <tm-details-tab id="roles" collection="RoleMemberships">
      <ng-template tmDetailsTabContent><tm-user-memberships /></ng-template></tm-details-tab>
    <tm-details-tab id="sign-in" labelKey="tm.admin.users.signIn">
      <ng-template tmDetailsTabContent><tm-user-sign-in /></ng-template></tm-details-tab>
    @if (canSeeAccess()) {
      <tm-details-tab id="access" labelKey="tm.admin.users.access">
        <ng-template tmDetailsTabContent><tm-user-access [user]="d.id()" /></ng-template></tm-details-tab> }
  </tm-details-tabs>` })
export class TmUserTabs {
  protected readonly d = injectTmDetails<TmEntityType<'core.User'>>();
  protected readonly canSeeAccess = inject(TmPermissions).can('core.Role', 'Read');
}

@Component({ selector: 'tm-user-fields', hostDirectives: [TmFieldGroup], imports: [TmDetailsKit, TmUserLocaleFields, TmSignatureField, TmUserStateBadge], template: `
  <ng-template tmDetailsTitleExtra><tm-user-state-badge /></ng-template>
  <tm-field path="Image" /> <tm-field path="Name" /> <tm-field path="Kind" />
  @if (d.model()?.kind === 'Individual') {
    <tm-field path="Email" [readonly]="d.mode() !== 'create' && d.entity()?.state !== 'New'" hintKey="tm.admin.users.emailHint" />
    <tm-field path="ContactEmail" /> <tm-field path="ContactMobile" /> <tm-field path="Gender" />
    <tm-user-locale-fields /> <tm-signature-field path="Signature" />
  }` })
export class TmUserFields { protected readonly d = injectTmDetails<TmEntityType<'core.User'>>(); }
```

- **State badge.** An Individual user's title row shows "Not invited", "Invited" or "Joined" (spec
  0031 §5.1), so the invitation state shows without opening a tab; the Sign-in tab holds the
  detail (§4.5). A service account shows none.
- **Kind** is write-once (spec 0014 §3.1). A service account, like the system user (`Kind =
  System`), shows Image, Name and Kind only (`Users.EmailNotAllowed`).
- **Email** locks once the user leaves `New` (`Users.EmailLockedAfterInvite`). Its hint: "Sign-in
  email. It can't change after the invitation is sent."
- **Memberships** (`tm-user-memberships`) bind spec 0004's editable grid over
  `injectTmDetails().form.roleMemberships`, with spec 0031 §6.4's height rule, and register the
  collection (spec 0031 §4.1), as the permissions editor does (§5.3). New rows take negative ids.
  The columns are Role, Period and Notes:
  - **Role** offers active roles that are not public (`IsPublic = false`): public roles take no
    members (spec 0014 §3.5). A hint under the grid says "Public roles apply to everyone and don't
    need members."
  - **Period** (`tm.admin.memberships.period`) shows the window as read-only text, in the display
    zone with its offset: "Permanent"; "Scheduled · 20 Nov, 14:00–15:00 (GMT+3)", or "Scheduled
    from 20 Nov, 14:00 (GMT+3)" with no end; "Active until 15:00", adding the date when it is not
    today; "Active since 3 Nov, 09:00" with no end; "Expired" in muted text. The text re-renders at
    the next boundary among the rows.
  - **Notes:** ≤ 1,024 characters.
- **The window editor.** Period is a popup column (`[editorPopup]="{ minWidth: 360 }"`, spec 0010
  §C.15) whose pane is a `role="group"` sub-form:
  - a `tm-segmented` Permanent / Time-limited;
  - Time-limited adds Starts, "Now" or a `tm-date-time-field` (spec 0023 §6.4), and Ends, a
    duration (1 hour, 4 hours, 8 hours, 1 day, 1 week) or "Custom", which shows a second
    `tm-date-time-field`; both fields read the display zone and always show its label;
  - a commit writes `startsAt` and `endsAt` only: Permanent writes both `null`, "Now" the commit
    instant cut to the minute, and a duration the start plus that duration. A stored window
    reopens as Custom;
  - the column is keyed `endsAt`. The pane commits both members as one `grid.applyTransaction` of
    two `TmCellEdit`s, one undo step (spec 0004 §11), and cancels its own cell commit. Paste and
    Delete on the cell write `endsAt`, as on any cell.

  In view mode F2 opens the pane read-only. A new row is Permanent.
- **Errors** (spec 0031 §8.5): lockout, escalation (also on a widened window) and
  `RoleMemberships.Overlap` land on the row; `RoleMemberships.EndsBeforeStart` and
  `RoleMemberships.EndsInPast` on its Period cell, since both are reported at `EndsAt` (spec 0014
  §8.4). An ended row stays until an administrator removes it (spec 0014 §3.6).
- **`tm-user-locale-fields`** renders §4.2's five fields for `PreferredLanguage`,
  `PreferredCalendar`, `PreferredTimeZone`, `PreferredRegion` and `PreferredDigits`, each nullable.
  The region and digits fields take scope `offered`, since an administrator's language says
  nothing about the user's. A member edits their own in My settings (§8.3).
- **Step-up.** Saving a user is sensitive (`core.User × Save`, spec 0014 §4.3). Spec 0031 §8.6
  keeps the draft across the identity confirmation.

### 4.2 Language, calendar, time-zone, region and digits fields

Five platform field components offer only valid codes in edit mode, each with its scope and
samples. Language, calendar, time zone and region are value sets (spec 0013 §7.7), so their view
mode is the kit's `tm-value` over the set's label, the same name a report, a filter or an export
shows. The server stays the authority.

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
// each also takes value: ModelSignal<string | null>, bound instead of path when no details host is above (§8.3)
```

Each wraps `tm-field` with a `tmFieldEdit` template, and the digits field also with a
`tmFieldView` template (spec 0031 §6.2). Options carry their set's labels:

| Field | View | Edit options (`tm-select`) |
|---|---|---|
| language | "العربية", the `core.Language` label | `tenant`: the tenant's content languages; `offered`: the metadata's `languages` |
| calendar | "Umm al-Qura", the `core.Calendar` label | `tenant`: the tenant's calendars; `registered`: the metadata's `calendars` |
| time zone | "Asia/Riyadh", the id | searchable; the `core.TimeZone` set's values, the current ids and never its `aliases` (spec 0029 §2.2), grouped by their first segment, each with its `longOffset`; an id the browser's ICU does not know shows without an offset |
| region | "Saudi Arabia", the `core.Region` label | searchable; `language`: the regions offered with the active language; `offered`: every region offered with any offered language (spec 0024 §7.7). Sorted by `Intl.Collator`, each with its sample |
| digits | "Arabic-Indic (١٢٣)" | "Latin (123)" and the native system, each named by `tm.digits.<system>`. The native system is the active language's. If it has none, under `offered` it is the first offered language's that has one (`arab` in Acme). Hidden when no language in scope has native digits |

- **Nullable** fields list "Tenant default ({effective value})" first. On the Settings page,
  Region and Digits label that option differently (§7.5). A stored value outside the options stays
  as an option, so it is never lost.
- **The sample** formats `1234.5` and today's date with the locale the option would give, in the
  user's language: "١٬٢٣٤٫٥ · ١٩/٠٦/٢٠٢٦" for `SA`. It needs no request.
- **Standalone.** With no details host above it, a field renders its edit control alone, in a
  `tm-form-field` (spec 0005) bound through `value`. Language & region uses this (§8.3).
- **Direction.** Codes and zone ids render in `dir="ltr"` isolates inside RTL text (spec 0024
  §7.9); an option is a plain string with `dir="auto"`.
- **Validation** is the server's (`Users.LanguageNotOffered`, `Users.CalendarUnknown`,
  `Users.TimeZoneUnknown`, `Users.RegionNotOffered`, `Settings.*`). A value outside its set comes
  back as the field's error (spec 0013 §7.7). An unknown digits value fails spec 0016's enum
  binding.

### 4.3 Inviting users

`users/invite` (spec 0018 §3.3) takes ids and answers one `InviteResult` per id. Its dialog opens
from the promoted button (one user) or the search page's bulk menu (a selection).

1. **Confirm.** The dialog asks "Invite {name}?" or "Invite {n} users?", then explains:
   - normally, "Each person gets an email from the identity server to set up their sign-in, and a
     welcome email with a link to {tenant}.";
   - on a sandbox tenant (`TmLaunchCache.docs.tenant().category`), "This is a sandbox. Only people
     who already have an account are added, and no one gets an invitation email.";
   - when the action is sensitive, it adds "You'll be asked to confirm it's you."
2. **Run** `run({}, { keepOpen: true, rethrow: ['partial-failure'] })` (spec 0031 §10.4) under the
   busy overlay. A 502 therefore rejects with its `TmApiError`, whose `problem` carries
   `errorDetails`.
3. **Outcome**, by response:

| Response | The dialog |
|---|---|
| 200, one user whose row has a `Status` | closes; the re-read page shows the new state |
| 200, otherwise | shows the results list |
| 422 | shows the rows' errors in its alert; nothing was sent, since one invalid row fails the whole action |
| 502 `partial-failure` | `run()` rejects; shows the results list for the problem's `errorDetails.results`, then the retry line |

- **Results list.** One line per user: the name, then "Invitation sent" (`Invited`), "Invitation
  sent again" (`Reinvited`), "Added: already had an account" (`Active`) or the row's `Error`. The
  error text comes from `TmServerText` for `Users.*` codes, and from the identity server otherwise.
- **422 codes:** `Users.NotIndividual`, `Users.Inactive`, `Users.AlreadyJoined`.
- **Retry line.** "{n} weren't attempted because the identity server stopped responding." and a
  "Try again for these" button. It runs `run({}, { keepOpen: true, rethrow: ['partial-failure'],
  ids: errorDetails.failed.map(Number) })`, since `failed` carries the ids as strings (spec 0018
  §3.3).
- **After close** the host re-reads the record and marks the list stale (spec 0031 §10.4). Any
  other failure lists in the dialog's alert (spec 0031 §10.4), and the dialog stays.
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

### 4.5 The Sign-in tab (`tm-user-sign-in`)

The Sign-in tab says how the user signs in and when they were last active.

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
`access/check` and shows one row per resource and action. `tm-user-access` takes the user as
`[user]`, so the access page's By user tab renders the same component (§4.9).

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
  - role grants, with a link to the role, the filter in mono LTR, and "until {time}" for a
    time-limited membership (the grant's `endsAt`);
  - bespoke criteria (`Access_Reason_<reason>`), such as "Their own records" for `self`;
  - "System user";
  - drifted permissions, marked "Not in effect" (§5.5);
  - a "Who else has this" link to the access page on that pair (§4.9).
- **At a boundary.** A time-limited grant leaves or joins the answer when its window does; the
  tab's Refresh re-checks.
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
  to it (`Users.SystemUserImmutable`), and its `permissions` extra refuses every ability by that
  rule (spec 0018 §3.1). Edit, Activate or Deactivate and Delete therefore show disabled with that
  reason (spec 0031 §10.1); §3's `visible` predicates hide its entity actions.

### 4.8 Another user's preferences (`TmUserPreferencesDialog`)

An administrator changes, for an Individual user, the preferences the user's own hub keeps outside
the user record (§8.4, §8.5): the pinned bar and the notification settings. Profile and locale are
the record's fields (§4.1); device settings and credentials are the user's alone. The Preferences
button is the users stack's `notification-preferences/save` entity action (§3), so it needs
`core.User × Preferences` (filtered, not sensitive) and shows in view mode only.

- **The dialog** is a `TmModal` (`md`, full screen below `sm`) titled "Preferences for {name}",
  with the hint "These apply to {name} on all their devices." It holds:
  - a Display card with "Show the pinned bar" (`nav.pinnedBarVisible`), read through
    `users/preferences/get { userId }` (spec 0018 §3.5);
  - `<tm-notification-settings [user]="id" />` (spec 0028 §10.3), in a `@defer (on immediate)` block
    (§1): the pop-up switch and the type × channel grid, read through
    `users/notification-preferences/get` and `users/preferences/get`, with no test delivery.
- **Saving.** Every change saves itself, as in the user's own hub (§8.4, spec 0028 §10.3), outside
  the page's Edit and Save. The pinned-bar switch writes `users/preferences/set` at once,
  and `tm-notification-settings` saves its switches as its section does. A failed write shows the
  hub's "Couldn't save your changes" with Try again under the control's card, and the controls
  keep the administrator's values. The footer holds Close only; a write in flight still lands.
- **Tenant state.** The routes are `AllowWhenReadOnly`, so the button works on a `ReadOnly` tenant
  and is disabled with spec 0029 §7's reason on a `Frozen` one.

### 4.9 The access page (`/{t}/access`)

The page answers "who holds this permission, and why" and, beside it, "what can this user do". It
is an audit page with no record of its own.

- **Route and entry.** `/{t}/access` is §3's `tmAccessPage`, whose component is `TmAccessPage` from
  `/admin`. Its launcher entry `access` sits in Administration at order 25 and shows with
  `core.Role × Read`, which `access/holders` requires (spec 0018 §3.7). Without it the route shows
  its `forbidden` state (spec 0024 §2.4).
- **Tabs and URL.** Two tabs, By permission (the default) and By user. The URL holds the state:
  `?resource=acme.JournalEntry&action=Post` on By permission, `?tab=user&user=9` on By user. A tab
  click or a pick replaces the URL.
- **By permission.**
  - A searchable `tm-select` over every non-wildcard securable of the metadata (§12.4), each named
    by `TmPermissions.label` and matching raw names too, as the Access tab's search does (§4.6).
  - A pick reads `access/holders { resource, action }` (spec 0014 §10) under the page's signal; a
    new pick aborts it.
  - An "Everyone" banner when `everyone` is not `Denied`: the public roles and the bespoke
    criteria that grant it to every user, each with its reason (`Access_Reason_<reason>`).
  - A grid of holders, by display name: avatar and name (a link to the By user tab for that
    user), "Allowed" or "Allowed for some records" with "until {time}" when the access ends
    (`endsAt`), the roles behind it as links, and their filters in mono LTR. A capped answer adds
    "Showing the first {n}." Holders the caller cannot see are counted only: "{n} more you can't
    see."
  - A "Not in effect" disclosure lists the answer's problems (§5.5's texts).
- **By user.** A `tm-entity-field entity="core.User"` above `<tm-user-access [user]>` (§4.6), with
  a link to the user's page.
- **Cross-links.** Each Access tab reason list (§4.6) and each permission row of a role (§5.3)
  link here on their pair.
- **Page context** is kind `access` (spec 0026 §9.3), with `state: { tab, resource, action, user }`.

## 5. Roles

The role page edits a role's permissions in a grid whose filter cell opens a popup editor, and
checks each filter as the user types. It lists the role's members read-only and marks permissions
that grant nothing.

### 5.1 The layout

The role page is two cards, as the user page is (§4.1): the role's fields with a Permissions tab
and a Members tab below them, then the audit card. Its two parts are reusable (§2).

```ts
// @tellma/core/admin — roles/
@Component({ selector: 'tm-role-details', imports: [TmDetailsKit, TmAlert, TmRoleFields, TmRoleTabs], template: `
  @if (d.id() === 1) { <tm-alert kind="info">{{ t('tm.admin.roles.administratorNotice') }}</tm-alert> }
  <tm-details-card><tm-role-fields /><tm-role-tabs tmDetailsCardSection /></tm-details-card>
  <tm-details-audit />` })
export class TmRoleDetails { protected readonly d = injectTmDetails<TmEntityType<'core.Role'>>(); }

@Component({ selector: 'tm-role-fields', hostDirectives: [TmFieldGroup], imports: [TmDetailsKit], template: `
  <tm-field path="Name" /> <tm-field path="Code" />
  <tm-field path="IsPublic" hintKey="tm.admin.roles.publicHint" [readonly]="d.id() === 1" />` })
export class TmRoleFields { protected readonly d = injectTmDetails<TmEntityType<'core.Role'>>(); }

@Component({ selector: 'tm-role-tabs', imports: [TmDetailsKit, TmRolePermissions, TmRoleMembers], template: `
  <tm-details-tabs>
    <tm-details-tab id="permissions" collection="Permissions">
      <ng-template tmDetailsTabContent><tm-role-permissions /></ng-template></tm-details-tab>
    <tm-details-tab id="members" labelKey="tm.admin.roles.members">
      <ng-template tmDetailsTabContent><tm-role-members /></ng-template></tm-details-tab>
  </tm-details-tabs>` })
export class TmRoleTabs {}
```

- **Administrator** (id 1, spec 0014 §3.5): "This role always grants everything. You can rename it
  and add permissions." Its `IsPublic` and its `*` / `*` row are locked.
- **Public hint:** "A public role applies to every active user and has no members."
- **Step-up.** `core.Role × Save`, `Delete` and `Activate` are sensitive.

### 5.2 Securables on the client

The metadata's `securables` section (§12.4) is fixed per deployment. The editor's pickers
therefore render at once, even offline.

- **Resources** are grouped by their securable's `feature` in `tm-select` option groups (spec 0010
  §B.4), each group labelled by §12.4's feature chain. A resource is labelled "Centers", "Settings:
  General" or "Credit exposure report", with the resource name as the description. "All resources"
  (`*`) comes first.
- **Actions** of the row's resource follow registry order ("Read", "Create and edit", "Activate and
  deactivate", "Invite"), plus "All actions" (`*`). Resource `*` offers every action name.
- **Filter root** is the pair's `filterRoot`. For action `*`, it is the root that the resource's
  filterable securables share (spec 0014 §3.7 step 2). Resource `*` has none.
- **Sensitive.** A row whose pair is sensitive, or whose wildcard covers a sensitive pair, shows a
  `shield-check` glyph with "Needs identity confirmation". The action's option shows the same.

### 5.3 The permissions editor (`tm-role-permissions`)

Permissions edit in a grid. A row's filter edits in a popup editor that opens over its cell and
spills below it (spec 0010 §C.15), and checks the filter as the user types.

```text
┌ Permissions ──────────────────────────────────────────────────────────────┐
│ [!] 1 permission isn't in effect. Fix or remove it.                       │
│    │ Resource        │ Action                    │ Filter          │ Notes │
│    │ Sales invoices  │ Read                      │ CenterId = 1012 │ Jeddah│
│ [!]│ Customers       │ Create and edit [shield]  ┌─────────────────────────────┐
│    │ Centers         │ Read                      │ Mangr.Id = me()             │
└────┴─────────────────┴───────────────────────────│                             │
                                                   │ Line 1: Unknown property    │
                                                   │ 'Mangr' on Customer         │
                                                   │ Records of Customers this   │
                                                   │ permission covers. …        │
                                                   └─────────────────────────────┘
```

- **Grid.** Spec 0004's editable binding over `injectTmDetails().form.permissions`, with spec 0031
  §6.4's height rule. New rows take negative ids. The columns:
  - a 28 px status column: the drift or local-error marker, with its reason as text;
  - Resource: a custom editor, §5.2's grouped select;
  - Action: the resource's actions; changing the resource clears an action it lacks;
  - Filter: mono, LTR and ellipsized in the cell, edited in the popup below;
  - Notes: ≤ 1,024 characters;
  - a row-end column (spec 0010 §C.14) with a "Who has this" link to the access page on the row's
    pair (§4.9), for a pair without wildcards.
- **Registration.** The editor registers the `Permissions` collection with
  `registerCollection` (spec 0031 §4.1), so the error summary, the tab's error dot and the page
  context see its rows.
- **The filter editor.** The Filter column is `[editorPopup]="{ minWidth: 480 }"`.
  - **Editor.** Its template holds `tm-expression-editor clause="filter" [rows]="4"`, a cell editor
    (spec 0029 §10.1) bound to the row's `filter`, over `injectTmQueryexDraft({ root: filterRoot,
    profile: 'permission' })` (spec 0029 §10). Every diagnostic a role save would return shows
    250 ms after typing stops. Enter inserts a line, and Mod+Enter commits.
  - **Hint,** under the editor: "Records of {root plural} this permission covers. Leave empty for
    all records. me() is the signed-in user, as in CreatedById = me()."
  - **No filter root.** The cell is read-only (spec 0004 §6.1's per-row `readonly`) and shows "All
    records" in muted text, described as "This permission can't be limited to some records."
  - **View mode.** F2 opens the popup read-only, so a long filter reads in full.
- **Public roles** disable "All resources" (`Permissions.WildcardResourceOnPublicRole`).
- **Save errors** at `entities[0].permissions[j]…` (spec 0016 §7.3, codes per spec 0014 §3.7) land
  on row `j`:
  - `FilterInvalid` and `FilterVersionUnsupported` land on its Filter cell. Their summary item
    selects the row and opens the cell's popup. `FilterVersionUnsupported` reads "Re-enter this
    filter to confirm it under the current rules."
  - `EscalationBeyondSelf` and `UnknownSecurable` land on the row itself.
- **Keyboard.** The grid's model (spec 0004) and spec 0010 §C.15's pane keys; F8 walks the editor's
  problems.

### 5.4 Members (`tm-role-members`)

The tab renders the eager `members` extra (spec 0018 §4.3) through `*tmDetailsExtra`, so it needs
no request of its own.

- **Columns:** avatar and name (a link to the user), Email, State (a badge), Active, Period, Notes.
  The avatar's `colorKey` is the record key `core.User:{id}` (spec 0023 §4.2). Period is §4.1's
  window text from the row's `startsAt` and `endsAt`.
- **Windows.** The extra omits memberships that have ended (spec 0018 §4.3), so a row is current
  or scheduled. A user with a current and a scheduled window has a row for each.
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
  `Gl.Centers.ParentMustBeGrouping` land on the field. `Gl.Centers.ParentMustBeGrouping` also lands
  on `CenterType` when a grouping center with children changes type.
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

- **Routes.** `/{t}/settings` and `/{t}/settings/{category}` are one route, whose matcher makes
  `category` optional (spec 0024 §9.2). A tab click therefore keeps the route and Angular reuses the
  host, in edit mode too. The route data is `{ kind: 'settings', skeleton: 'details', titleKey:
  'tm.admin.settings.title', load }`, where `load` imports `TmSettingsPage` from `/admin`. Links use
  `TmUrls.page('settings/invoicing')`.
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
- **One edit mode for the page.** Edit is enabled when any readable category is savable and the
  tenant is neither `ReadOnly` nor `Frozen` (spec 0031 §3.1). In edit mode, categories without
  `Save` render their fields read-only, with the lock (spec 0031 §6.2).
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
| `write` | §7.6's patch to `settings/save`; the result's `details` maps back the same way, and its `client` is adopted with its `settingsTag` (§7.7) |
| `writeRoute` | `'settings/save'` |
| `can` | `edit`: any readable category is savable, on a tenant that admits writes (§7.1); `create`, `delete`, `activate`: false |
| `validate` | §7.6 step 2 |
| `saveSecurable` | `['core.Settings.General', 'Save']` while the working copy has a dirty typed member or a dirty `general` entry, else `null` |
| `editSecurable` | `['core.Settings.General', 'Save']` |
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
- **Groups.** A key's group is the one it declares, else the one its name implies (spec 0013
  §5.3). A category's groups are its keys' distinct `group` values, in first-appearance order.
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
  Only the General category's securable is sensitive (spec 0013 §5.5).

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
   returns to view mode with the new stamps; the source adopts `client` under `settingsTag` (§7.7).
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
      "client": { "tenantId": 17, "names": [ … ], "calendars": ["gc", "uq"], "entries": { "acme.invoicing.prefix": "SI-", … } },
      "settingsTag": "1.5d3e…" }
```

A colleague who changed `acme.sales.defaultCurrencyId` meanwhile causes no conflict.

### 7.7 Effects on the warm cache and other tabs

A save changes the tenant's settings in every cached copy. This section says how this tab, other
tabs and other browsers catch up.

1. **This tab** passes the result to `TmCacheRefresher.adopt(t, { settings: { data: client, tag:
   settingsTag } })` (spec 0025 §5.3). `settingsTag` is the tag the response header also carries, so
   that header never triggers a sync. The app bar's tenant name, the session menu's calendars and
   the `Client` keys re-render. A Region or Digits change re-formats every value in place (spec 0024
   §7.7).
2. **Other tabs** of the browser re-render from `store.changed`; other browsers follow
   `cache.changed { tag: 'settings', value }` (spec 0025 §4.4).
3. **Page state.** When the save changed the tenant's languages, every tab forgets the tenant's
   page state once it holds the new `settings` document (spec 0025 §10).
4. **A language change** also bumps `permissions` (spec 0013 §6.1): one `client/sync` brings the
   new `me`, and descriptors follow the new shape (spec 0029 §4.2).
5. **A Settings page in view mode** in any tab re-reads `details` when the stored document moves
   (the source's `changes`). In edit mode it does not re-read; its save's stamps catch any
   conflict.
6. **The tenant picker** may show the old name until the catalog mirror runs (spec 0013 §6.3,
   step 6).
7. **Refresh caches** is a menu action, `settings.refresh-caches`, enabled with
   `can('core.Settings.General', 'Save')` on a tenant that admits writes:
   - it confirms "Refresh caches?" / "Every user's next request reloads settings, permissions and
     lists. Use this after changing data outside {displayName}.";
   - it calls `settings/refresh-caches`, which is sensitive (spec 0013 §6.4);
   - it toasts "Caches refreshed."

## 8. My settings (`/{t}/me`)

Every member keeps their personal settings in one hub of sections, each a route with its own link.
Profile has Edit, Save and Cancel; every other section saves each change itself. The hub renders
from stored documents, so it sends nothing when it opens.

### 8.1 The hub

```ts
// @tellma/core/admin — me/
@Component({ selector: 'tm-me-hub' })
declare class TmMeHub {}                                          // the 'me' route: the nav and the sections' outlet
declare function tmMeSource(): TmDetailsSource<UserProfileView>; // Profile (§8.2)
```

| Section | Route | Icon | Saves | Applies in |
|---|---|---|---|---|
| Profile | `/{t}/me`, `/{t}/me/profile` | `id-card` | on Save, through `users/me/save` (§8.2) | this tenant |
| Language & region | `/{t}/me/region` | `globe` | each change, through spec 0024 §7.2's locale write (§8.3) | this tenant; the automatic time zone, this device |
| Display | `/{t}/me/display` | `sun-moon` | each change, at once (§8.4) | the theme, this device; the pinned bar, this tenant |
| Notifications | `/{t}/me/notifications` | `bell` | each change (spec 0028 §10.3) | this tenant |
| Account | the account URL (spec 0027 §10.1), in a new tab | `user-round` | at the identity server | every tenant |

- **Layout.** From `md` up, the nav fills a 220 px column at the inline start, beside the section.
  Below `md`, a full-width menu button above the section shows the current section's icon and
  name with a chevron, and opens a `tm-menu` (spec 0010 §C.10) of the same links. The route
  skeleton `me` (spec 0024 §4.4) draws this geometry.
- **The nav** is a `<nav>` labelled by its caption "My settings", holding a list of links. It is
  not a tablist, because each section is a page with its own URL. The current link carries
  `aria-current="page"`. Account carries a mirrored `external-link` icon, `target="_blank"`,
  `rel="noopener"` and "(opens in a new tab)" in its accessible name; it shows while the
  distribution document is cached.
- **Routes.** `/{t}/me` shows Profile without rewriting the URL, as `/{t}/settings` selects its
  first tab (§7.1). The sections are child routes of `me` (spec 0024 §9.2), so the nav stays while
  they change. Moving between sections is a navigation: Profile's leave guard applies (spec 0031
  §11.3), and focus moves to the new section's heading. Links use `TmUrls.page('me/region')`.
- **Titles.** Each section's heading is its name, and `document.title` is "{section} · My
  settings · {displayName}".
- **Scope.** A member of more than one tenant sees "Applies in {tenant} only." under the heading
  of Profile, Language & region and Notifications, since each tenant keeps its own user row.
  Device settings say so in their hints.
- **Tenant state** (spec 0029 §7).
  - On a `ReadOnly` tenant, Profile's Edit is disabled with the reason "{tenant} is read-only.",
    since `users/me/save` is refused; Language & region, Display and Notifications stay live,
    because their writes are `AllowWhenReadOnly` (spec 0018 §3.1, spec 0021 §4.3).
  - On a `Frozen` tenant, every control that writes to the tenant is disabled, under an info
    `tm-alert`: "{tenant} is under maintenance, so these settings can't change now."
  - The theme and the automatic time-zone option stay live in both, being device settings.
- **Page context.** Each section publishes kind `settings` (spec 0026 §9.3): Profile through its
  source (§8.2); Language & region and Display with `{ category: 'region' | 'display', mode:
  'edit', dirty: false }`; Notifications as spec 0028 §10.3 says.

### 8.2 Profile

Profile is a source page on spec 0031's host. It renders from the stored `me` document and sends
nothing until Save.

```ts
// @tellma/core/admin — me/
@Component({ selector: 'tm-me-profile-layout', imports: [TmDetailsKit, TmSignatureField], template: `
  <tm-details-card>
    <tm-field path="Image" /> <tm-field path="Name" /> <tm-field path="Email" readonly hintKey="tm.admin.me.emailHint" />
    <tm-field path="ContactEmail" /> <tm-field path="ContactMobile" /> <tm-field path="Gender" />
  </tm-details-card>
  <tm-details-card titleKey="tm.admin.me.signature"><tm-signature-field path="Signature" /></tm-details-card>` })
export class TmMeProfileLayout {}
```

The source's members, as in §7.2:

| Member | Value |
|---|---|
| `kind` | `'settings'` |
| `entity` | `'core.User'`, for labels, types and blob policies |
| `id` | `me.user.id` |
| `titleKey` | `tm.admin.me.profile` ("Profile") |
| `draftKey` | `TmDrafts.key(t, 'core.User/me', me.user.id)`: apart from the draft of the user's own `/users/{id}` page; spec 0034 §12.1 keys the studio the same way |
| `read` | resolves at once to `{ entity: TmLaunchCache.docs.me().user }`, with no request |
| `write` | `users/me/save` → `MeResult`; see **Save** below |
| `writeRoute` | `'users/me/save'` |
| `can` | `edit`: true, except on a `ReadOnly` or `Frozen` tenant (§8.1); the rest false |
| `category` | `'profile'` (spec 0026 §9.3) |
| `errorSegments` | `contactEmail` → `['contactEmail']`: `users/me/save` paths carry no row index |
| `changes` | the stored `me` document |
| `rebase` | the moved `me.user` when its eight profile members equal the base's, else `null`; see **Rebase** below |

- **Save.** The source sends `MeSaveRequest`'s eight profile members, `modifiedAt` from the base
  and `concurrency` to `users/me/save` (spec 0018 §3.5). It adopts the returned `MeResult` with
  `adopt(t, { me })` (spec 0025 §5.3).
- **Rebase.** Through `rebase` (spec 0031 §3.1), a moved `me` whose eight profile members equal the
  base's becomes the base, stamp included, with no notice. So a locale change (§8.3, the session
  menu, the time-zone update), in this tab or another, never makes Save conflict. A move that changed a profile member meets spec 0031 §9.1's
  self-case dialog, and Overwrite resends with `Override`.
- **Header.** "Profile" with Edit, Save and Cancel; no crumb, pager or Actions menu.
- **No step-up.** `users/me/save` is self-service (spec 0018 §3.5), so the source declares no
  `saveSecurable` and no step-up notice shows.
- **Email** is read-only, with the hint "Your sign-in email. Change it under Account."

### 8.3 Language & region

The section is one card of always-editable fields: no Edit, no Save, no draft.

- **Fields**, each standalone (§4.2), seeded from `me` and listing "Tenant default ({effective
  value})" first:
  - Language, `tm-language-field scope="tenant"`, whose default names the tenant's primary
    language, the one a reset applies (spec 0024 §7.2);
  - Calendar, `tm-calendar-field scope="tenant"`;
  - Time zone, then `tm-auto-time-zone` on its own row;
  - Region, `tm-region-field scope="language"`;
  - Digits, `tm-digits-field scope="language"`.
- **Saving.** A change calls `TmLocalization.setLocale` (spec 0024 §7.2). It applies at once: a
  language re-renders the app, and a region or digits value re-formats it in place (spec 0024
  §7.7). One `users/me/locale` follows 600 ms after the last change, and "Tenant default" sends the
  member in `reset`.
- **Status.** A line under the card, at a reserved height, reads `localeSave`: "Saving…", "All
  changes saved", or "Couldn't save your changes" with Try again. A 422 marks its field invalid,
  with the message in the field's error popover.
- **Automatic time zone.** `tm-auto-time-zone` is a `tm-checkbox`:
  - its label is "Change my time zone automatically when this device's time zone changes", and its
    hint "Saved on this device only.";
  - it reads `TmDeviceZone.auto` and writes `setAuto(on, 'settings')` (spec 0024 §7.8);
  - the Users page never shows it.

### 8.4 Display

- **Theme.** A `tm-segmented` System / Light / Dark over `TmTheme` (spec 0010 §B.2), with the hint
  "Saved on this device only." (spec 0024 §7.6).
- **Pinned bar.** A `tm-switch` "Show the pinned bar" over `TmPreferences.get` and `set` of
  `tmPinnedBarVisibleKey` (spec 0027 §8.2, §8.11; spec 0025 §9.1), with the hint "Applies in
  {tenant} on all your devices." The preference is curated, so it applies and flushes at once (spec
  0025 §9.4).
- **No status.** Both apply in the click's frame, and nothing waits on the server.

### 8.5 Notifications

The section's body is spec 0028 §10.3's: the pop-up switch, the type × channel grid and test
delivery. The hub supplies its frame, heading and scope line.

## 9. Schedules

The schedule page is a layout over spec 0020 §7's stack, plus one rule for built-in schedules. A
built-in schedule locks every field except its names, `CronExpression` and `TimeZoneId`. It also
hides Deactivate and Take over, and disables Delete.

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
- **How the actions hide.** §3's `visible` hides Deactivate and Take over. Delete shows disabled
  with its reason: the `permissions` extra refuses `delete` on a built-in with
  `Schedules.BuiltInImmutable` (spec 0020 §7.8), and spec 0031 §4.1's `can` reads it.
- **Job** (`tm-job-handler-field`) is write-once. Create mode offers the metadata's `jobHandlers`
  marked `schedulable` (spec 0020 §3.7), labelled as spec 0028 §11.2 says. View mode shows the
  label and the key in mono. The search column's `TmJobHandlerCell` renders the same label. When
  the metadata lists no `schedulable` handler (no Core handler is, spec 0020 §7.8), the field reads
  "No job can be scheduled yet."
- **`CronExpression`** is mono LTR, with the hint "Five fields: minute, hour, day of month, month,
  day of week. Example: 0 6 * * 1-5 runs at 06:00 on weekdays."
- **`TimeZoneId`** is nullable ("Tenant default (Asia/Riyadh)"), with §4.2's `core.TimeZone`
  options.
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
- **A stopped import** (`ResultJson`'s `stopped` is true, spec 0019 §3, §12.6; its job, while it
  exists, shows "Stopped", spec 0028 §10.5) shows a warning `tm-alert` above the Result card:
  "This import was stopped. The rows listed as saved stay saved, and the rest of the file wasn't
  imported." The Result card shows the outcome the handler recorded, with its
  `CommittedRowRanges` (spec 0019 §12.6), so the user knows which rows to fix or remove.
- **A job that ended without a result** (`Job.Status` is `Cancelled` or `Failed` and `ResultJson`
  is null, spec 0019 §12.6) shows a `tm-alert` in place of the Result card. Cancelled reads "This
  import was stopped before any rows were imported." and Failed "This import ended with an error.
  Rows saved before the error stay saved." Both add a "View job" link to the job's details page,
  which shows the error.
- **An import pending confirmation** (`Job.Status` is `PendingConfirmation` through the job's
  related projection, spec 0020 §3.8) shows a warning `tm-alert` above the Result card: "This
  import is paused until its warnings are confirmed." Anyone but the initiator also reads "Waiting
  for {name}." (`CreatedBy`) and sees nothing more.
  - **The warnings.** For the initiator (`CreatedById` is the user, as is the job's
    `RequestedById`), the page reads `jobs/get` on `JobId` selecting `PendingConfirmationsJson`
    (`{ count, items }`). The Result card renders the items through `tm-import-result-view`
    as the import dialog renders confirmable errors (spec 0030 §13.3): each item is an error whose
    `code` is its `key`, whose `sheet` and `row` come from its `arguments`, and whose message is
    the key's with those `arguments`; `totalErrors` is `count`, so "and {n} more" follows.
  - **Confirm** (primary) writes `jobs/confirm { ids: [jobId], arguments: { keys } }` with the
    items' distinct keys, then re-reads and follows the job again. `Jobs.NotPendingConfirmation`
    means the list moved: the page re-reads and shows the message. On a `ReadOnly` tenant Confirm
    is disabled with spec 0029 §7's reason.
  - **Stop** sends `jobs/cancel` after spec 0028 §6.2's import confirmation. The job stops at its
    next claim, and the row shows a stopped import's outcome (above, spec 0019 §12.6).
- **Expiry** shows "In 6 days" with the date. An expired row is gone (spec 0019 §12.1).
- **Delete while the job runs.** While `Job.Status` is `Pending`, `Running`, `Held` or
  `PendingConfirmation`, the server refuses the delete with `Imports.JobActive` (spec 0019 §12.1).
  The page shows that refusal as it shows any failed delete (spec 0031 §10.2).
- **A running import** shows "Importing… {percent}%" from the job's progress.
- **Following its job.** While the import's job is `Pending` or `Running`, the page follows it by
  spec 0028 §10.5's rule, re-reading with `injectTmDetails().refresh({ quiet: true })` (spec 0031
  §4.1), which shows no progress bar. A status change is announced politely ("Import finished.",
  "Import failed: {message}", "Import stopped.", "Import waiting for confirmation."); percent
  changes are not.
- **No push elsewhere.** The other admin pages get no push; Refresh re-reads them.

## 12. Server and metadata changes

The server members these pages read are specified in their owner specs; this section lists them.
It owns one thing itself: the label chains of the `securables` section (§12.4).

### 12.1 Settings (spec 0013)

| What the pages read | Where |
|---|---|
| `settingKeys` and `settingCategories` in the Core chunk, written by `SettingsClientAssetContributor` | spec 0013 §1.2, §5.3 |
| `TenantSettingsDetails` with `EntryStamps` and no key descriptors | spec 0013 §6.1, §6.2 |
| `TenantSettingsPatch` with per-entry stamps and `Concurrency`; `SettingsSaveResult(Details, Client, SettingsTag)` | spec 0013 §6.1, §6.3 |
| `Region` and `Digits` on `TenantSettings`, `TenantSettingsForClient` and `TenantSettingsPatch`; `Settings.RegionNotOffered` | spec 0013 §5.6, §6.1, §6.5 |
| each `languages` entry's `region`, `regions` and `nativeDigits`, which the region and digits fields list | spec 0013 §1.2, §7.2 |
| the startup gate "a nullable key defaults to `null`", which makes §7.6's reset rule sound | spec 0013 §5.3 |
| a key's optional `group`, which §7.3's groups read | spec 0013 §5.3 |
| the `core.Language`, `core.Calendar`, `core.TimeZone` and `core.Region` value sets on the locale columns, emitted in the Entities chunk's `enums` | spec 0013 §7.7, spec 0029 §2.2 |
| `SettingCategory_`, `SettingGroup_`, `Setting_<key>_Description`, `Securable_`, `Access_Problem_`, `Access_Reason_` keys | spec 0013 §10.1 |

### 12.2 Users and roles (specs 0014, 0016, 0018)

- **`UserProfileView.ModifiedAt`** (spec 0016 §3.4), filled by the connect prologue's profile set
  (spec 0014 §7.1). `MeResult` carries no securables fingerprint.
- **`MeSaveRequest.Concurrency`.** `Override` skips the stamp comparison (spec 0018 §3.1, §3.5).
- **Regional members.** `PreferredRegion` and `PreferredDigits` on `User` (spec 0014 §3.1) and
  `UserProfileView` (spec 0016 §3.4); `Users.RegionNotOffered` (spec 0018 §3.2).
- **The self-service split.** `MeSaveRequest`'s eight profile members; `MeLocaleRequest` with
  `Region`, `Digits` and `Reset`, and `MeLocaleResult` with the two formats (spec 0018 §3.1, §3.5).
- **The `activity` extra** on the users stack (spec 0018 §3.1).
- **The `members` and `problems` extras** on the roles stack (spec 0018 §4.3). `members` is
  filtered by the caller's `core.User × Read`, capped at 500, carries `StartsAt` and `EndsAt`, and
  omits ended memberships. `problems` follows spec 0014 §9's five conditions.
- **Membership windows.** `RoleMembership.StartsAt` and `EndsAt`, and the validators
  `RoleMemberships.Overlap`, `EndsBeforeStart` and `EndsInPast` (spec 0014 §3.6, §8.4); a role
  grant's `endsAt` in `AccessDecision` (spec 0014 §5.1, spec 0016 §3.3).
- **Another user's preferences.** `users/preferences/get` and `set`, and
  `users/notification-preferences/get` and `save`, under `core.User × Preferences`, Individual
  users only (spec 0018 §3.1, §3.5).
- **Holders.** `access/holders` with `AccessHoldersRequest(Resource, Action)` answering
  `AccessHolders` (spec 0014 §5.1, §10; spec 0018 §3.7).

### 12.3 Jobs, schedules and emails (specs 0018, 0020, 0022)

- **The `state` extra** on the schedules stack (spec 0020 §7.8).
- **Built-ins in the `permissions` extra** (spec 0015 §5.3, spec 0020 §7.8): a built-in refuses
  `delete` and `action:take-over` with `Schedules.BuiltInImmutable`.
- **`jobHandlers`** in the Core chunk (spec 0020 §3.7), mirrored as `TmJobHandlerMetadata`
  `{ key, schedulable, labelKey, labelFallback?, result? }`, `result` being `{ entity, via }` from
  `ClientJobResult` (spec 0028 §10.5 reads it).
- **`Job`'s `[RelatedSelect]`** (spec 0020 §2.3), so `Job.Status` and the label pass the traversal
  rule for any reader of an export, import or email.
- **A `PendingConfirmation` job's warnings and its `confirm` action:** `PendingConfirmationsJson`,
  read through `jobs/get` under the self-scope, and `jobs/confirm` (spec 0020 §3.8, §11.1).
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
| `core.Settings.General`, `core.Settings.<c>` | `SettingCategory_general`, `SettingCategory_<c>`; every reader, `TmPermissions.label` included, shows it through Core's `Securable_Settings`, "Settings: {category}" |
| any other resource | `Securable_<resource with '.' → '_'>` (`Securable_acme_CreditExposureReport`), else the humanized last segment |
| an action | `<Schema>_<Entity>_Action_<Name>`, then Core's `Action_<Name>` (spec 0013 §10.1), else the humanized name; `*` is "All actions" |
| a feature, the editor's group | `Feature_<name with '.' → '_'>` (`Feature_gl`), else the humanized name |

Acme's section has about 150 items, under 3 KB gzip. It has five readers:
`TmPermissions.sensitive` and `TmPermissions.label` (spec 0029 §7), the permissions editor (§5.2),
the Access tab (§4.6) and the access page (§4.9).

## 13. Testing

Five suites pin this design, and all of them run on PR.

- **Unit (vitest).** The settings diff (typed members, entries, default → `null`, secrets, stamps,
  client checks); `errorSegments`; groups, declared and derived, and tab order from fixture
  metadata; filter roots for `*`; every `visible`; the window text for each state and its next
  boundary; the window editor's writes (Permanent, "Now", each duration, Custom); the access
  page's URL state; the email document prefix and `cid:` rewriting.
- **Harness (fake `TmApi`).**
  - Settings: tabs from access; read-only categories; a first tab click from `/{t}/settings` in edit
    mode keeping the host and the working copy; §7.6's body; 409 → Overwrite → `Override`; language
    confirmation; the Formats card's defaults; adoption under the result's `settingsTag`, with no
    sync from the header; a view-mode re-read on a moved document.
  - Roles: cell ↔ popup, including F2 read-only in view mode and the summary opening a cell's
    popup; live diagnostics; drift markers; members states, windows and the cap line.
  - Users: the tabs inside the card; the State badge; memberships with a Period popup, its commit
    undone in one step restoring both bounds, and its three errors landing; invite outcomes (all
    sent, mixed, 422, a 502 read through `rethrow` with retry, a 500 in the alert); credentials
    shown once and the close guard; access chunks of 200; access labels equal to
    `TmPermissions.label`, and a search by label or raw pair; the preferences dialog saving each
    change with the target id (two grid changes within 600 ms sending one write), a failed write's
    status and Try again, Close only, hidden for a service account and without the grant, with no
    test delivery, and disabled on a `Frozen` tenant.
  - Access page: holders for a pair, the Everyone banner, the capped and hidden counts, problems;
    By user over `tm-user-access`; the cross-links from the Access tab and the permissions grid.
  - Locale fields: views equal to the value sets' labels; zone options from `core.TimeZone`,
    grouped, an id without an ICU offset; `tm-region-field` options per scope, with samples;
    `tm-digits-field` hidden without native digits, and its native option's system under each
    scope; each field standalone.
  - Imports: quiet re-reads on `job.changed`, the 5 s poll for a non-requester and with the hub
    down, coalescing, the stop at a terminal status, the announcements; a job cancelled before its
    claim and one failed with `Jobs.AttemptsExhausted`, both with a null `ResultJson`, showing their
    alerts; a pending confirmation's alert, its warnings with sheet, row and "and {n} more", Confirm
    sending the distinct keys for the initiator only, a stale list re-reading, Confirm disabled on a
    `ReadOnly` tenant, Stop confirming, and the state alone for an administrator.
  - My settings: the nav's `aria-current` and the below-`md` menu; `/{t}/me` showing Profile; a
    Profile Save sending eight members; a locale change while Profile is in edit mode rebasing, so
    Save sends the new stamp and meets no 409; two Language & region changes within 600 ms sending
    one write, and "Tenant default" sending `reset`; a failed write's status and Try again;
    `tm-auto-time-zone` applying at once; a `ReadOnly` tenant disabling Profile only, and a
    `Frozen` one disabling every tenant write but not the theme.
- **e2e on Acme** (Playwright; Chromium; Firefox and WebKit `@cross-engine`).
  - Settings: two administrators edit different `invoicing` keys (both save) and the same key
    (conflict, Overwrite); add a language, then its name field appears; replace and clear the
    secret; the invoicing tab's preview.
  - Users: create, invite, Check delivery; credentials and Copy; a restricted role (no Access tab);
    a one-hour Auditor membership saved, then its buttons disabling for that user at its end;
    another user's notification settings changed through Preferences.
  - Roles: a filtered permission checked and saved in its popup, which spills past the grid's
    bottom edge; drift.
  - Access page: who holds `acme.SalesInvoice · Save`, then the By user tab for one holder.
  - Centers: the tree and Acme's override.
  - My settings: warm with requests held on each section; a Profile save; a region change
    re-formatting the page; a session-menu switch while Profile is in edit mode, then a clean Save;
    the theme and the pinned bar from Display; Account opening the identity page in a new tab.
  - Schedules: run-now, built-in locks and the disabled Delete with its reason.
  - Outgoing emails: an email with HTML, a `cid:` image and a blocked remote image.
  - Imports: an import's page following its job, for the requester and for an administrator; a
    stopped import showing its saved ranges, one pending confirmation stopped on its page included;
    a background import whose second chunk raises a warning, whose notice opens the Import page,
    where Confirm finishes it with no second confirmation.
- **axe.** Every page, tab, dialog, notice and empty state; light and dark, LTR and RTL.
- **Budgets.** §1's sizes: the `/admin` chunk, the dialogs, the primary's declarations and
  `@tellma/module-gl`'s primary.

## 14. Definition of done

- **Behaviour.** §3–§11 behave as stated and every §13 suite is green. The server members of §12
  exist in their owner specs.
- **UI rules.** Each rule says how the design meets it, then which test pins it.
  - **Instant warm launch.** Stock chrome is spec 0030's and 0031's. Settings' tabs, labels and
    controls come from the metadata and cached access (§7.1, §7.3). My settings renders entirely
    from stored documents (§8). *Test:* spec 0024's `warm-launch.spec` on `/17/settings/invoicing`,
    `/17/me`, `/17/me/region` (values too) and `/17/roles/4`, with every request held: chrome
    within 1 s.
  - **Instant feedback.** Edit, tab switches and the popup editors open in the click's frame.
    Securables, zones, regions and their samples are local (§5.2, §4.2). *Test:*
    `admin-feedback.spec`, a rAF probe after Edit, a tab click and F2 on a Filter cell.
  - **Non-blocking.** `settings/details`, `invitation-status`, `access/check`, `access/holders`,
    `queryex/discover` and `cid:` fetches abort on leave or supersession; writes never abort.
    *Test:* a harness case where leaving the Access tab aborts its chunks.
  - **Stable layout.** Popup editors take no layout space, so rows keep their height (§4.1, §5.3).
    Fixed heights for the frame, the status aside and the delivery line (§10.2, §9, §4.5). Reserved
    "Default" and reset boxes (§7.4). The step-up notice sits in spec 0031's overlaying strip
    (§7.5). *Test:* `settings-geometry.spec` and `role-editor-geometry.spec`, boxes identical
    across load, Edit, typing in a popup, a 422 and a reset.
  - **RTL.** Logical properties. Filters, cron, JSON, zone ids, client ids and secrets sit in
    `dir="ltr"` isolates, with no bidi control character in the text (spec 0024 §7.9). The email
    frame keeps the email's direction. The My settings nav sits at the inline start. *Test:* every
    §13 e2e in `ar` at 390, 768, 992 and 1440 px, and `email-body-rtl.spec`.
  - **WCAG AA.** Settings tabs are spec 0005's `tablist`. The permissions and membership grids and
    their popups work from the keyboard (F2 opens a popup, Tab moves inside it, Mod+Enter commits,
    Escape cancels; F8 walks a filter's problems), and Access rows expand with Enter. Dialogs trap
    and return focus, and Copy announces "Copied". Outcome, state and drift markers pair an icon
    with text. *Test:*
    `admin-keyboard.spec` (invite, issue and copy credentials, add a filtered permission and fix it
    from the summary, give a membership a window, change and reset a setting), and axe on every
    page, tab, dialog, open popup and state.
- **Page context.** The entity pages publish spec 0030's and 0031's contexts. Settings and the
  My settings sections publish kind `settings`, Settings and Profile through spec 0031 §16 with the
  category from their sources and the host's `form` (`Name`, `entries.acme.invoicing.prefix`), the
  other sections as §8.1 says. The access page publishes kind `access` (§4.9).
  - A secret's field carries no value. The credentials dialog publishes no values (§4.4).
- **Page actions** beyond the frameworks': `settings.refresh-caches` (menu, confirms) and the entity
  actions `core.user.invite`, `core.user.issue-credentials`, the Preferences button
  `core.user.notification-preferences.save` (§4.8), `core.schedule.run-now`,
  `core.schedule.take-over`, `core.outgoing-email.retry` and `core.outgoing-email.cancel`.
- **Telemetry** under `tm.admin`, with closed properties (spec 0026 §3.2). Events:
  - `tm.admin.settings.saved { categories: 1 | 2-3 | 4+, typed, languages, outcome }`
  - `tm.admin.users.invite { count: 1 | 2-10 | 11+, outcome: ok | mixed | invalid | partial | error }`
  - `tm.admin.users.credentials { rotation, copied, outcome }`
  - `tm.admin.roles.problems { count: 0 | 1 | 2+ }`
  - `tm.admin.email.body { mode: html | text, inlineImages }`
  - `tm.admin.profile.saved { outcome }`
  - `tm.admin.users.preferences { part: display | notifications, outcome }`
  - Timings: `tm.admin.users.delivery { outcome }`, `tm.admin.users.access { chunks }` and
    `tm.admin.access.holders { outcome, capped }`.
- **What Acme adds.**
  - The centers override and `my-center-details` (§6), and the `invoicing` tab (§7.5).
  - Spec 0018 §1.4's keys: a string, an int, a nullable reference, an enum array and a secret,
    across three categories.
  - E2e fixtures that create the users (New, Invited, Joined, a service account), the roles they
    exercise, and a scheduled and an expired membership.
  - Drift: a role whose `Name3` filter was saved while the tenant had three languages, before the
    tertiary language is removed.

## Decisions record

1. **Every stock page is a declaration on specs 0030 and 0031** — layouts are markup and a
   distribution writes nothing; a component per page would re-implement both frameworks (§3).
2. **A pack's primary holds declarations; its layouts ship in a secondary entry point** — eager code
   may not import `/details` (§1, §6).
3. **Settings and My settings' Profile are source pages on spec 0031's host** — edit mode, save,
   drafts, conflicts and the error summary come free; bespoke pages would duplicate them (§7.2,
   §8.2).
4. **One Settings page with one edit mode and a tab per readable category, selected by the URL** —
   one Save sends every dirty field; per-tab edit modes would multiply drafts and guards (§7.1).
5. **Setting-key descriptors ship in the client metadata** — tabs, labels and controls render with
   no request; `details` carries values and stamps only (§7.3).
6. **Groups are declared or derived; tab order is registry order** — a pack declares a group only
   when its key names cannot express it, since renaming a stored key orphans its rows (§7.3).
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
11. **The locale codes are value sets; the five fields keep only their edit scopes and samples** —
    a code reads as the same name in a details page, a report, a filter and an export, and the
    zone options are Queryex's own current ids, so every zone a user picks compiles (§4.2).
12. **Permissions edit in a grid; a filter edits in a popup editor that spills below its cell,
    checked live by `queryex/discover`** — a multi-line filter needs more than a row's height,
    and the edited text sits where the value was; drift comes with the read (§5.3, §5.5).
13. **Role members are a read-only, row-filtered, capped extra** — no hidden user leaks and no
    route is added; memberships stay a property of the user (§5.4).
14. **Invite and Issue credentials are entity actions with declared dialogs** — they confirm, run,
    show per-row results or the one-time secret, and retry a subset; delivery is checked on demand
    (§4.3–§4.5).
15. **Email HTML renders from a `blob:` URL in a sandboxed frame with its own CSP** — no scripts, no
    remote loads, `cid:` images inline; `srcdoc` would be a Trusted Types sink (§10.2).
16. **The Access tab names each pair with `TmPermissions.label` and also matches raw names** — the
    words a `forbidden` message shows the user find the administrator's row at once (§4.6).
17. **My settings is one hub of routed sections behind a link nav; only Profile has Edit and Save,
    and the locale members leave `me/save`** — personal settings live in one place and each section
    keeps a deep link; a profile change wants review before it saves, while a language, a theme or a
    switch is seen at once; with the locale members in `me/locale` alone, a section's write never
    turns a Profile Save into a conflict (§8).
18. **The user and role pages are two cards: fields with tabs below, then audit** — no aside, and
    the field groups and tabs carry no card, so a distribution adds a header field without
    copying the tabs (§4.1, §5.1).
19. **An administrator edits another user's preferences in a dialog that saves each change** — the
    controls behave as they do in the user's own hub; a self-saving tab inside a read-mode card
    would surprise, and joining the record's Save would make two non-atomic writes behind one
    button (§4.8).
20. **A membership window is two nullable instants edited in a popup; durations are UI only** —
    the server stores and decides only `[StartsAt, EndsAt)` (§4.1).
21. **"Who holds this" is its own page beside "what can this user do"** — no securable has a
    details page, and the question comes from audits; the user page keeps its Access tab as the
    entry point from a user (§4.9).
22. **Pending import warnings are confirmed on the Import page, shown as the import dialog's** —
    the initiator reads them by sheet and row, in the view they already know (§11).

## Review flags

1. **Per-entry stamps, not per category** (§7.6). Flips if a category's keys must save as one
   consistent set.
2. **One edit mode for the whole Settings page** (§7.1). Alternative: edit and save per tab. Flips
   if administrators want separate saves per category.
3. **A value equal to its default resets** (§7.6). Flips if tenants must pin today's default.
4. **JSON and scalar-array keys edit as text** (§7.4). Alternatives: hide JSON keys that have no
   `settingsTabs` component; a free-entry chip input. Flips on misuse or a second consumer of
   chip entry.
5. **Role members read-only and capped at 500** (§5.4). Alternatives: add and remove members on the
   role; a paged child-query route. Flips if administrators reject editing from the user's page.
6. **The Access tab checks every securable** (§4.6). Alternative: one securable at a time. Flips on
   measured slowness (`tm.admin.users.access`).
7. **Delivery is checked on demand** (§4.5). Alternative: read it when the Sign-in tab first
   opens. Flips if administrators press Check delivery on most visits.
8. **Signatures are uploaded images, never drawn** (§4.7). Alternative: a drawing pad. Flips if
   users ask to sign on screen.
9. **Remote email images are always blocked** (§10.2). Alternative: a "Show remote images" switch.
   Flips if administrators must see emails exactly as sent.
10. **No entity filter on Exports and Imports**; the `Resource` column's text filter stands in
    (§11). Alternative: a data-driven filter, which waits for spec 0030's component hatch (its
    review flag 12). Flips on that hatch.
11. **Region lists differ by page** (§4.1, §4.2, §8.3): Language & region lists the regions of the
    language the user reads; the user page and the Formats card list every offered region.
    Alternative: one list everywhere. Flips if users miss a region they need, or administrators pick
    one that no language of the tenant uses.
12. **My settings' nav is a list of links that becomes a menu below `md`** (§8.1). Alternative: a
    tablist, as on Settings. Flips if users expect the sections to share one Save.
