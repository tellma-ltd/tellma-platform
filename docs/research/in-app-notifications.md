# In-app Notifications — Market Analysis and Recommendation

**Purpose:** Survey how widely used enterprise products tell their users things inside the app —
assigned work, background jobs, rule-based alerts, operator announcements and everyday activity —
both in their architecture and in their UX. Settle, from that evidence, how Tellma should bucket
notifications, which surface each bucket gets, and what that changes in
[spec 0021](../specs/0021-notifications-inbox-and-hub.md).

**Status:** Research and recommendation, 13 October 2026. Not a spec: the recommendations are inputs
to a revision of spec 0021 and to the specs that follow it; where they conflict with a later spec,
the spec wins.

**Scope.** In-app surfaces only: shell icons and badges, drawers and pages, toasts and banners.
Email, SMS and push appear only where they shape the in-app design. Operational telemetry for
Tellma staff (OpenTelemetry, Azure Monitor) is out of scope.

**Method.** Vendor documentation, design-system guidelines, API references and changelogs were
fetched and read on 13 October 2026. Claims resting only on community or partner material are
marked *(2°)*; claims that could not be confirmed are marked *(unverified)*; deductions from verified
facts are marked *(inferred)*. Several SAP design-system pages and Salesforce help pages refused
direct fetches, so some of their content comes from search extracts of the official pages.
Confidence markers on recommendations:

| Marker | Meaning |
|---|---|
| **Firm** | Well supported by evidence and by platform conventions already in force. Changing it should require new information, not a preference. |
| **Leaning** | The better of two defensible options. Worth revisiting if implementation or usage data pushes back. |
| **Open** | A genuine coin-flip, or something that needs a usability test. Listed in [Part 4](#part-4--open-questions). |

**Products.** Seven ERP and finance suites — SAP S/4HANA with the Fiori launchpad, Oracle Fusion
Cloud, Dynamics 365 Finance and Operations, Dynamics 365 Business Central, Workday, Oracle NetSuite,
Odoo — with short notes on Sage Intacct, Xero and QuickBooks Online. Eleven CRM, service and
work-management products — Salesforce Lightning, ServiceNow, Jira and Confluence, GitHub, Linear,
Asana, Microsoft Teams with the Graph activity feed, the Microsoft 365 admin Message center and
Service health, HubSpot, Zendesk, Slack. Eight design systems — SAP Fiori, Microsoft Fluent 2 and
the Power Apps in-app notification guidance, IBM Carbon, Atlassian, PatternFly, the Salesforce
Lightning Design System, Material 3, GOV.UK. Five notification platforms whose data models are
public — Knock, Novu, Courier, MagicBell, Stream. Long-running-task UX in the Azure portal, Google
Drive, Notion and Confluence exports, and GitHub and GitLab pipelines.

---

## The questions, answered in brief

| # | Question | What the field does | Recommendation for Tellma |
|---|---|---|---|
| 1 | Is the list of categories comprehensive? | Every product has a category the list lacks: **activity about things you are involved in** — your document was approved, returned or posted; someone mentioned you; a report was shared with you. It is the bulk of every product's bell. | **Add "Activity".** Also place **tenant-admin system alerts** (failed system jobs, email delivery failing, scheduler gaps — spec 0021 already raises them) under alerts, not telemetry, and add **system state**: tenant markers in the app bar for what the tenant *is* (sandbox, read-only) and banners for what is *happening* (connection lost, new version available). [D1](#21-bucket-by-lifecycle--d1), [D12](#28-announcements-banners-and-tenant-markers--d11-d12) |
| 2 | Is it structured correctly? | Products that bucket by **what makes an item go away** have clean UX: work leaves when it is done (Workday, Linear, GitHub's 2026 pull-request inbox), broadcasts leave when their window ends (ServiceNow, GitHub, Workday). Products that bucket by source and put everything in one bell drift and draw complaints (Oracle Fusion, QuickBooks). | **Bucket by lifecycle, not by source.** Five buckets — work, operations, activity, conditions, broadcasts — map onto four surfaces: the tray, the bell, the banner and the toast. [D1](#21-bucket-by-lifecycle--d1), [D2](#22-four-surfaces--d2) |
| 3 | How is a user told that a document was assigned to them? | **Assigned work is a live query with its own surface, never a stored notification**: SAP My Inbox, Oracle Worklist, Workday My Tasks, Business Central "Requests to Approve", NetSuite reminders, Linear My Issues, Asana My Tasks, Jira "Assigned to me", Zendesk views, GitHub's pull-request dashboard. Products that also copy tasks into the bell need sync jobs and still show stale items (Oracle, SAP). | **The tray is the signal.** Its count is the number of open assignments; it turns to an accent colour while any assignment is new since the tray was last opened; new rows carry a "new" dot; a toast announces an assignment that arrives while the app is open. **No bell row.** [D3](#23-assignments--d3-d4), [D4](#23-assignments--d3-d4) |
| 4 | For jobs: unseen or unread? Terminal or intermediate? A shell button? | **The job page is the truth; one notice points to it when the job ends** (Oracle, Workday, Dynamics, Business Central, GitHub Actions). Progress lives on a job list or a session tray (Azure portal, Google Drive), never in the notification feed. Results that live only on a page get lost (Confluence exports). | **Terminal states only**, as ordinary bell rows with the bell's unseen and unread semantics — no counter of their own. Running jobs appear in an **"In progress" section at the top of the bell's dropdown** and a progress ring on the bell icon; **no separate shell button**. [D5](#24-background-jobs--d5) |
| 5 | For alerts: an unseen dot, an unseen count, or per-item read? | Seen and read are stored separately almost everywhere (SAP, Salesforce, Atlassian, ServiceNow, Knock, Novu, MagicBell, Stream). **Only SAP Situation Handling gives an alert an identity**: one instance per rule and record, updated on re-trigger, closed automatically when the condition clears, closed for everyone. The others fire afresh on every evaluation. | **An unseen count on the badge and per-item read**, as for every bell row. What alerts need is **identity and withdrawal**: one row per condition while it holds, withdrawn for everyone when it clears. [D6](#25-the-bell--d6-d7-d8), [D9](#26-alerts--d9) |
| 6 | Are operator notices a banner or a notification? | **A banner, and a separate object, in every product that has them**: ServiceNow banner announcements, GitHub announcement banners, the Jira announcement banner, Oracle and Workday announcements, NetSuite administrative notifications, Knock Guides. None writes a row per user into the bell. SAP and Dynamics have none, and their customers build workarounds. | **A banner**, from an `Announcement` record with an audience, a required end, a severity and a dismissible flag — never bell rows. [D11](#28-announcements-banners-and-tenant-markers--d11-d12) |
| 7 | What changes in spec 0021? | — | The bell keeps its machinery and gains severity, toasts, condition semantics (`Ongoing` and `Withdraw`), a running-jobs section and a clear verb; assignments and announcements get their own small models. [Part 3](#part-3--changes-to-spec-0021-and-its-siblings) |

---

## Part 1 — Market analysis

### 1.1 The field at a glance

| Product | Assigned work | Background jobs | Rule-based alerts | Operator broadcast | Bell badge |
|---|---|---|---|---|---|
| **SAP S/4HANA** | My Inbox / Task Center, a live query; the workflow *also* posts a bell item | Application Jobs monitor; per-job notify options | **Situation Handling**: instance per anchor object, auto-close, close-for-everyone | None in the launchpad; customers build plugins | **Unseen** count, reset on open; read per item |
| **Oracle Fusion** | BPM Worklist + "Things to Finish"; tasks *also* copied into the bell | Scheduled Processes; "notify me" at submit | Alerts Composer (HCM), Watchlist counts | **Announcements**: role-scoped, start/end dates | Unread count |
| **Dynamics 365 F&O** | Work items list | Batch jobs page; per-job batch alerts | User-created alert rules, run by batch jobs | None found *(unverified)* | Action center *(badge unverified)* |
| **Business Central** | "Requests to Approve" cues (live counts) | Job-queue cues (failed / running / queued) + failure notification | Data-driven page notifications, coloured cues | None for tenant users | No global feed |
| **Workday** | **My Tasks**, its own icon and count | Process Monitor; "Notify Me Later" | Admin alerts on report filters | **Announcements** worklet: audience rule, start/end | **Two badges**: tasks and notifications |
| **NetSuite** | Reminders portlet (derived counts with ageing) | Job Status page + email | Saved-search alerts (email) | Administrative notifications, **must acknowledge** | None in-app |
| **Odoo** | Activities (live), systray count | No user-facing notice | Automation rules create activities | None found *(unverified)* | Inbox unread |
| **Salesforce** | "Items to Approve" component (live) | Job monitor pages + email | Flows send custom notification types | In-app guidance prompts | **Unseen and unread** both exposed |
| **ServiceNow** | My Work / My Approvals lists | No unified notice found | Provider notifications on conditions | **Banner announcements**: window, audience, non-dismissible flag | **Unseen** by default, admin can switch to unread |
| **Jira / Confluence** | "Your work › Assigned to me" | Progress page *(unverified)* | Automation rules | One site banner, six colours, optional permanent dismiss | **New since opened**; Watching excluded from the count |
| **GitHub** | **Pull-requests dashboard** (2026): a query-driven work inbox | `ci_activity` notice to whoever triggered the run | Dependabot, deployment approvals | Banners that **must expire or be dismissible** | Unread; Done and Saved |
| **Linear** | **My Issues** (a view) | Agent-session notices | SLA ordering; view subscriptions | Product updates only | Inbox; Priority tab (2026) |
| **Zendesk** | Views ("Your unsolved tickets") | — | — | — | Unread **tickets**, not messages; **auto-removed when the ticket is solved** |
| **Microsoft 365** | Planner / Approvals apps | — | — | **Message center** (planned change) and **Service health** (incidents), each a long-lived, updatable record | Teams feed: 30-day expiry |

Per-product detail and links are in [§1.7](#17-evidence-by-product).

### 1.2 What the field has converged on

**C1 — Assigned work is a query, with its own surface.** Every product with real workflow shows
"what I must do" as a live query over the work's own tables: SAP My Inbox, Oracle Worklist, D365
work items, Business Central approval cues, Workday My Tasks, NetSuite reminders, Odoo activities,
Salesforce Items to Approve, ServiceNow My Approvals, Jira "Assigned to me", Linear My Issues, Asana
My Tasks, Zendesk views. An item leaves because the work changed, not because someone dismissed a
message. Where a notification about the assignment also exists, it is a pointer. GitHub's 2026
answer to notification overload was not a better notification inbox but a separate, query-driven
pull-request inbox ("Review requests / Needs fixing / Ready to merge"). Asana states the split in
one line: My Tasks is "work you need to do", the Inbox is "updates you need to know about" *(2°)*.

**C2 — Copying work into the bell causes drift.** Oracle Fusion copies BPM tasks into the bell and
ships a batch job, "Synchronize Notifications in Global Header", to remove items "acted on
elsewhere, for example, by email or by other approvers"; Oracle recommends running it every two
hours, and its support articles are titled "Approvers See Notification under the Bell Icon even after
Request Approval". SAP knowledge-base articles describe My Inbox and the bell disagreeing. Workday
users report that the approval notice stays after the task is gone *(2°)*. Zendesk is the one
product that removes notifications automatically when the ticket is solved.

**C3 — Seen and read are separate states.** "Seen" is set for all items at once when the panel
opens and drives the badge; "read" is set per item by a click and drives bold styling. SAP's shell
API has `getUnseenNotificationsCount`, `notificationsSeen` and `markRead`; Salesforce exposes
`unseenCount` and `unreadCount`; Atlassian's count "clears, but notifications remain listed";
ServiceNow badges unseen by default; Knock, MagicBell and Stream count unseen by default. PatternFly
warns that "if unread notifications are almost always present, the alert loses its effect".

**C4 — Background jobs: a job page plus one notice at the end.** The job's list or page carries
status, progress and logs; a single notice goes to the person who started it when it ends (Oracle
"notify me" at submit, Workday "Notify Me Later", Dynamics batch alerts, Business Central's 2024
job-queue cues and failure notification, GitHub's "a workflow run you triggered completed", with a
failures-only option). Session trays (Azure portal, Google Drive) show progress for jobs the user is
watching; anything that may finish after the user leaves needs a durable notice and a durable result
page. The counter-example is Confluence's space export: the link lives only on the progress page and
is lost on navigation, and users have open feature requests about it *(2°)*.

**C5 — Broadcasts are their own object, shown as a banner.** ServiceNow banner announcements have
start and end times, colour and icon for urgency, an audience by role or group, a non-dismissible
flag and an ordering number. GitHub requires every banner to "have an expiration date, be user
dismissible, or both". Workday and Oracle announcements carry audience rules and start and end dates;
NetSuite's administrative notices must be acknowledged, with an audit of who confirmed. Knock models
announcements as **Guides**, a product separate from the feed, with "tenant-wide outage banners" as
an example. The design systems agree: Carbon, Atlassian and GOV.UK reserve the banner for
system-wide conditions, one at a time, under the header.

**C6 — Toasts announce only what deserves interruption.** SAP pops a banner only for high-priority
notifications (users can switch it off); Dynamics lets the sender choose timed or hidden;
ServiceNow's toasts "tuck away into the bell menu after a few seconds" *(2°)*. The design systems
converge on 4–8 seconds (Dynamics 4, Fluent 7, PatternFly 8), pause on hover, at most three or four
visible with an overflow link, never the only path to an action, never auto-dismissing a critical
message (Carbon cites WCAG 2.2.4), and a polite live region present from page load.

**C7 — Every mature product grows a priority tier.** Atlassian keeps Watching out of the count;
Slack shows a number for "needs you" and a dot for the rest; GitHub's mobile push covers only
mentions, assignments, review requests and deployment approvals; Linear added a Priority tab in
September 2026. Fatigue is the most common complaint in notification usability testing (Smashing,
2025).

**C8 — Retention is bounded, and limits evict rather than block.** Atlassian, Teams and
Freshservice keep 30 days, ServiceNow 60 days unread, GitHub three months unless saved; Dynamics
requires an expiry on every system notification. Salesforce is the cautionary tale: a user tray
that reaches 10,000 notifications "can't receive new notifications", and sends beyond 10,000 per
hour are dropped, not queued.

**C9 — Types are a declared, localisable catalogue with per-type preferences.** Teams' activity
types are declared in the app manifest with a localisable `templateText` and parameters; SAP lists
every received type in a settings dialog with on/off, banner, email and push switches; Workday sets
preferences per type, channel and frequency within limits an administrator sets. Spec 0021's type
registry, rendered from resources at read time, already matches this.

### 1.3 Where products disagree

- **Identity for alerts.** Only SAP Situation Handling gives an alert instance an identity (an
  "anchor object") with update, close, reopen and automatic close when the condition clears; a
  situation "closed by anyone disappears for everyone". Dynamics alert rules, Workday report alerts
  and NetSuite saved-search alerts fire per event or per run, so a scheduled check notifies again on
  every run while the condition holds *(inferred for the last three)*.
- **Done and archive.** GitHub has Done and Saved, Asana Archive and Bookmark, ServiceNow Clear and
  "Remind me", Linear delete and snooze; Salesforce and Atlassian have read only, and draw
  complaints, as do Workday (notifications users cannot delete) and Oracle (no bulk dismiss).
  Asana auto-archived on open until 2015 and reversed it because messages became hard to find.
- **Who defines alerts.** End users in Dynamics (an alert rule from any page) and NetSuite
  (subscribe to a public search); key users or administrators, targeting roles or teams, in SAP,
  Workday and Oracle.
- **Banner dismissal.** For the session only (ServiceNow), permanently per user (Jira, GitHub),
  never (Atlassian's design guidance for critical banners), or acknowledged with an audit
  (NetSuite).
- **Badge style.** A count (SAP, Salesforce, ServiceNow, Atlassian, Zendesk) or a dot (Asana,
  Slack threads). Zendesk counts tickets, not messages.

### 1.4 Failures worth designing against

| Failure | Product | Design consequence |
|---|---|---|
| Tasks copied into the bell go stale after someone else acts | Oracle (sync job every 2 h), SAP (KBAs) | Work never becomes a bell row |
| Counts don't update after an action until reload | Workday 2021 R1 *(2°)*, Oracle "still shows as unread" | Counts are queried, and pushed on change |
| Full tray blocks new notifications; excess sends dropped | Salesforce | Time-based retention; no per-user cap |
| Export link lost on navigation | Confluence *(2°)* | Every job result has a durable notice and page |
| Jobs started by a script run as "-System-", so nobody is told | NetSuite *(2°)* | A job's requester is a person, or administrators are told |
| No job-failure notice at all until 2024 | Business Central | Failure notices are not optional |
| Delivery polled only on navigation | Dynamics Power Apps | Push (spec 0021's hub) |
| Bell with no defined purpose | QuickBooks Online *(2°)* | Each surface has one stated job |
| Notification overload ends in a separate work inbox | GitHub | Start with the split |

### 1.5 What the design systems say

The design systems split surfaces by **scope** and **persistence**, and agree on the split:

| Surface | Scope | Persistence | Used for |
|---|---|---|---|
| Inline / message bar | One region or page | Until resolved | State of what the user is looking at (Fluent MessageBar, Carbon inline, Atlassian section message) |
| Toast / flag / snackbar | Overlay | Seconds | Feedback on the user's own action; arrival of something urgent |
| Banner | Whole app | Until the condition ends | System-wide conditions: maintenance, outage, read-only mode. One at a time, under the header |
| Notification centre / drawer | One user, over time | Until cleared or expired | History of what was addressed to the user |
| Badge | An icon | Until viewed | Dot = something new; count = how many; attention colour = critical (PatternFly) |
| Modal | Blocking | Until answered | Must act now |

Two refinements recur in work tools: **actionable versus informational** (GitLab's To-Do list,
which marks a to-do done automatically when the user takes the relevant action; SAP's warning that
dismissing a notification "does not affect processes behind the notifications"), and **job tracker
versus notification** (Azure, Drive, GitHub runs).

### 1.6 The data models of notification platforms

| | Seen | Read | Archive / done | Replace or group | Broadcast | Expiry |
|---|---|---|---|---|---|---|
| Knock | `seen_at` on open | `read_at` | `archived_at` (excluded from counts) | archive and retrigger; batch key + window | fan-out, one message per user–tenant pair; announcements are Guides | — |
| Novu | automatic after ~1 s visible, one-way | toggle | archived; snoozed | — | topics fan out per subscriber | — |
| MagicBell | `seen_at` | `read_at` | archived | — | — | — |
| Stream | `is_seen` | `is_read` | — | aggregation groups | follow fan-out | — |
| Teams / Graph | owned by Teams | owned by Teams | — | `chainId` overrides an earlier notification | team, channel or chat members, ≤ 100 per call | 30 days |
| Dynamics `appnotification` | implicit (count cleared on open) | none | dismiss = delete | — | one row per user; teams unsupported | TTL, default 14 days |

Two facts matter for Tellma. First, **every platform fans a notification out to one row per
recipient** — per-user state (seen, read, archived) needs it — and **none models a tenant-wide
announcement as feed rows**. Second, **Novu's severity colours the bell by the highest-severity
unread item**, and PatternFly's badge has read, unread and attention states: severity changes the
badge's colour, not its count.

### 1.7 Evidence by product

- **SAP**: [notification center usage](https://www.sap.com/design-system/fiori-design-web/v1-84/ui-elements/notification-center/usage);
  [UI5 notifications service](https://ui5.sap.com/1.38.67/docs/api/symbols/sap.ushell.services.Notifications.html)
  (unseen count, `notificationsSeen`, `markRead`; WebSocket push with polling fallback);
  [Situation Handling](https://www.sap.com/design-system/fiori-design-web/v1-96/discover/sap-products/sap-s4hana-only/situation-handling);
  [closing behaviour](https://blogs.sap.com/2022/08/30/fine-tuning-custom-situations-closing-behavior-of-situation-instances/)
  and [trigger accumulation](https://blogs.sap.com/2022/03/16/custom-situation-cases-configure-an-escalation-case-5-6/) *(2°)*;
  [Task Center](https://pages.community.sap.com/topics/task-center/faq);
  [SM02 not in the launchpad](https://community.sap.com:443/t5/technology-q-a/sm02-system-messages-in-fiori-launchpad/qaq-p/596188) *(2°)*.
- **Oracle Fusion**: [notifications and Things to Finish](https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26a/fauim/notifications-0.html);
  [bell sync job](https://docs.oracle.com/en/cloud/saas/sales/oasal/how-do-i-sync-in-app-notifications-for-all-users-in-fusion-applications.html);
  [scheduled process statuses](https://docs.oracle.com/en/cloud/saas/procurement/24c/faspp/statuses-of-scheduled-processes.html);
  [announcements](https://docs.oracle.com/en/cloud/saas/applications-common/26a/facia/how-do-i-create-edit-or-delete-announcements-in-fusion-applications.html).
- **Dynamics 365 F&O**: [messaging APIs](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/user-interface/messaging-api-center-bar-details)
  (context routing, required expiry and rule id); [batch job alerts](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/sysadmin/alerts);
  [alert rules](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/fin-ops/get-started/create-alerts).
- **Business Central**: [job-queue notifications, 2024 wave 2](https://learn.microsoft.com/en-us/dynamics365/release-plan/2024wave2/smb/dynamics365-business-central/get-notified-about-issues-job-queue-background-processing);
  [page notifications](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/developer/devenv-notifications-developing);
  [cue colours](https://learn.microsoft.com/en-us/dynamics365/business-central/admin-how-set-up-colored-indicator-on-cues).
- **Power Apps**: [in-app notifications](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/send-in-app-notifications)
  (per-user rows, 14-day default expiry, polling on navigation).
- **Workday**: [notifications](https://doc.workday.com/admin-guide/en-us/manage-workday/tenant-configuration/notifications-and-alerts/wil1453160661174.html)
  (purge never removes actionable items); [alerts](https://doc.workday.com/admin-guide/en-us/manage-workday/tenant-configuration/notifications-and-alerts/dan1370796451292.html);
  [announcements](https://doc.workday.com/admin-guide/en-us/manage-workday/tenant-configuration/navigation/tar1438106772604.html);
  [My Tasks vs notifications](https://gvsu.edu/workday/workday-my-tasks-inbox-and-notifications-69.htm) *(2°)*.
- **NetSuite**: [reminders](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N581945.html);
  [administrative notifications](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N258657.html);
  [saved-search alerts](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N680983.html).
- **Odoo**: [activities](https://www.odoo.com/documentation/18.0/applications/essentials/activities.html);
  [Discuss](https://www.odoo.com/documentation/16.0/applications/productivity/discuss.html).
- **Salesforce**: [custom notification action](https://developer.salesforce.com/docs/atlas.en-us.api_action.meta/api_action/actions_obj_custom_notification.htm);
  [notification limits](https://help.salesforce.com/s/articleView?language=en_US&id=platform.notif_builder_considerations.htm&type=5)
  (from search extracts).
- **ServiceNow**: [banner announcements](https://www.servicenow.com/docs/r/zurich/platform-user-interface/next-experience-banner-announcements.html);
  [Employee Center notifications](https://www.servicenow.com/community/employee-center-articles/notifications-for-employee-center/ta-p/3301753) *(2°)*.
- **Atlassian**: [in-app notifications](https://support.atlassian.com/atlassian-cloud/kb/overview-of-in-app-notifications-in-atlassian-cloud-products/);
  [announcement banner](https://support.atlassian.com/jira/kb/configure-announcement-banner-in-jira-cloud/);
  [design system: banner](https://atlassian.design/components/banner/usage), [flag](https://atlassian.design/components/flag/usage).
- **GitHub**: [notifications REST](https://docs.github.com/en/rest/activity/notifications);
  [managing the inbox](https://docs.github.com/en/subscriptions-and-notifications/how-tos/viewing-and-triaging-notifications/managing-notifications-from-your-inbox);
  [pull-requests dashboard GA](https://github.blog/changelog/2026-07-09-new-pull-requests-dashboard-is-now-generally-available);
  [announcement banners](https://docs.github.com/en/enterprise-server@3.17/organizations/managing-organization-settings/creating-an-announcement-banner-for-your-organization);
  [workflow-run notifications](https://docs.github.com/en/actions/concepts/workflows-and-actions/notifications-for-workflow-runs).
- **GitLab**: [To-Do List](https://docs.gitlab.com/user/todos/).
- **Linear**: [inbox](https://linear.app/docs/inbox); [My Issues](https://linear.app/docs/my-issues); [changelog](https://linear.app/changelog).
- **Asana**: [the better inbox](https://asana.com/inside-asana/better-inbox).
- **Zendesk**: [notifications list](https://support.zendesk.com/hc/en-us/articles/4408829025690-Using-the-notifications-list-to-manage-conversations).
- **Microsoft Teams and 365**: [activity feed notifications](https://learn.microsoft.com/en-us/graph/teams-send-activityfeednotifications);
  [best practices](https://learn.microsoft.com/en-us/graph/teams-activity-feed-notifications-best-practices);
  [Message center](https://learn.microsoft.com/en-us/microsoft-365/admin/manage/message-center);
  [Service health](https://learn.microsoft.com/en-us/microsoft-365/enterprise/view-service-health).
- **Design systems**: [Fluent MessageBar](https://fluent2.microsoft.design/components/web/react/core/messagebar/usage),
  [Fluent Toast](https://fluent2.microsoft.design/components/web/react/core/toast/usage),
  [Carbon notification pattern](https://carbondesignsystem.com/patterns/notification-pattern/),
  [PatternFly notification drawer](https://www.patternfly.org/components/notification-drawer/design-guidelines),
  [PatternFly notification badge](https://www.patternfly.org/components/notification-badge/design-guidelines),
  [GOV.UK notification banner](https://design-system.service.gov.uk/components/notification-banner/),
  [NN/g: indicators, validations, notifications](https://www.nngroup.com/articles/indicators-validations-notifications/),
  [Smashing: notification UX](https://www.smashingmagazine.com/2025/07/design-guidelines-better-notifications-ux/).
- **Platforms**: [Knock message statuses](https://docs.knock.app/send-notifications/message-statuses),
  [Knock Guides](https://docs.knock.app/in-app-ui/guides/overview),
  [Novu inbox](https://docs.novu.co/platform/inbox/overview),
  [Stream notification feeds](https://getstream.io/activity-feeds/docs/javascript/notification-feeds/).
- **Long-running tasks**: [Azure portal notifications](https://learn.microsoft.com/en-us/azure/azure-portal/set-preferences);
  [Notion export](https://www.notion.com/help/export-your-content);
  [Confluence export request](https://jira.atlassian.com/browse/CONFCLOUD-76888) *(2°)*.

---

## Part 2 — Recommendation for Tellma

The recommendation assumes spec 0021 as it stands: one `core.Notifications` row per recipient,
language-neutral and rendered at read time; `INotifier` riding the caller's batch; capped counts
queried, never maintained; `InboxSeenAt` on `core.UserStamps`; the `TellmaHub` with thin events;
the channel registry with a delivery job for channels other than the inbox. It also assumes spec
0020's `core.Jobs` with `RequestedById` and the `job.changed` event, and the shell's app bar, which
already reserves a bell and a "my assignments" tray.

### 2.0 Decision summary

| # | Decision | Choice | Firmness |
|---|---|---|---|
| D1 | Buckets | **By lifecycle — what makes the item go away**: work, operations, activity, conditions, broadcasts | Firm |
| D2 | Surfaces | **Four**: the tray (work), the bell (activity, results, conditions, plus running jobs), the banner (broadcasts and passing system conditions), the toast (arrivals that deserve interruption) | Firm |
| D3 | Assignments | **A derived list** from a platform `core.Assignments` table; the tray count is open work; **never a bell row** | Firm |
| D4 | Assignment arrival | Tray turns accent while anything is new + "new" dots + an arrival toast; email through a channel-only type | Leaning |
| D5 | Background jobs | Running jobs in the bell's **"In progress" section**; **terminal notices only**, as ordinary bell rows | Leaning |
| D6 | Bell badge | **One number: unseen**, reset on open; per-item read kept; colour by the highest unseen severity | Firm |
| D7 | One row per subject | Activity about a record uses `DedupKey = <resource>:<id>` with `Replace` — latest event wins | Leaning |
| D8 | Bell verbs | Read, read all, **clear** (delete own rows); no archive, saved or snooze | Leaning |
| D9 | Alerts | **A condition has one row while it holds** (`DuplicatePolicy.Ongoing`) and is **withdrawn for everyone** when it clears (`INotifier.Withdraw`) | Firm |
| D10 | Severity and toasts | `Severity` on the type (overridable per request) drives icon and badge colour; a type-level `Toast` flag drives arrival toasts | Leaning |
| D11 | Announcements | **A separate record shown as a banner**, never bell rows; audience, required end, severity, dismissible flag | Firm |
| D12 | System state | **What the tenant is** (sandbox, read-only) is a **marker in the app bar**; **what is happening** (connection lost, new version) is a banner. One banner at a time, by a fixed precedence, with a pager | Leaning |

### 2.1 Bucket by lifecycle — D1

The brief's list is organised by **source** (workflow, job runner, rule engine, operator). The
field's evidence says to organise by **lifecycle** — what makes the item go away — because that
decides the surface, the counter and whether a toast fires:

| Bucket | Examples | Goes away when | Surface | Counter |
|---|---|---|---|---|
| **Work** | A document assigned to me; a document returned to me | The work is done or handed on | Tray | Open count (derived) |
| **Operations** | My export running at 40% | It ends — and leaves a result notice | Bell, "In progress" section | None; a ring on the bell |
| **Activity** | My document was approved / posted; a report was shared with me; I was added to a tenant; my export is ready; my import failed | It is read, cleared, or ages out | Bell | Unseen count |
| **Conditions** | Balance below threshold; contract expiring in 3 days; budget at 90%; email delivery failing; scheduler gap | The condition clears | Bell, withdrawn on clear | Unseen count |
| **Broadcasts and passing conditions** | Maintenance Saturday 2–3 pm UTC; connection lost; a new version is available | Its window ends or the condition passes | Banner | None |
| *Tenant state — not a notification* | You are in a sandbox; the tenant is read-only | The tenant's state changes, often after weeks or never | App-bar marker | None |

What the brief's list misses:

1. **Activity** — the largest category in every bell surveyed: approvals, rejections and postings of
   documents the user created or signed; mentions and comments, once documents have them; a report
   shared with the user (already planned in the front-end notes); membership and role changes. Job
   results belong here too: "your export is ready" is news about a finished operation.
2. **Tenant-admin system alerts** — spec 0021 and its siblings already raise them:
   `core.job.failed` for a job without a requester, `core.scheduler.gap`, `core.job.held`,
   `core.schedule.paused` and spec 0022's `core.email.failed`. They target tenant administrators,
   not Tellma staff, so they are conditions, not telemetry.
3. **System state** — derived from state the platform already holds (D12): what the tenant *is*
   (sandbox, read-only) and what is *happening* to the session (connection lost, a new version).
4. **A boundary worth naming**: messages about the page the user is on — validation errors, "this
   document is locked by another user" — are inline message bars, not notifications (Dynamics routes
   by context; Business Central and Intacct keep validation separate).

### 2.2 Four surfaces — D2

| Surface | Where | Holds | Counter |
|---|---|---|---|
| **Tray** | App bar, beside the bell | Work assigned to me | Open assignments; accent while any is new |
| **Bell** | App bar | Activity, job results, conditions; an "In progress" section for my running jobs | Unseen; colour by highest unseen severity; a ring while a job of mine runs |
| **Banner** | One slot directly under the app bar, above the pinned bar | Announcements and passing system conditions | None |
| **Toast** | Top inline-end corner, under the app bar (mirrors in right-to-left) | Arrivals that deserve interruption; feedback on the user's own action | None |

The home page and the launcher reuse the tray's data ("My assignments", "Assigned by me"); the full
Notifications and My jobs pages stay as they are. Two shell icons with two numbers is what Workday
ships, and it is what keeps work from drowning in news. A third icon for jobs is not worth its space:
most users run a background job rarely, and when they do they are waiting for it.

One more mark sits in the app bar without being a notification surface: the **tenant markers**
of D12, beside the tenant name.

### 2.3 Assignments — D3, D4

**D3 — work is derived, never a bell row (Firm).** C1 and C2 decide it: a stored notification about
an assignment either duplicates the tray or goes stale when the document moves on. The invariant in
the brief — a non-terminal document has exactly one assignee — makes the tray a plain query.

Because the tray is a platform shell component that must list work from any module (general-ledger
documents, leave requests, purchase orders), the platform needs one place to read it from. A
`core.Assignments` table, written by modules in their own save batch exactly as `INotifier` is,
serves the tray, the home page widgets and the launcher with one indexed query:

```csharp
// Tellma.Core.Abstractions.Assignments — a sketch
public sealed class Assignment : Entity<int>        // core.Assignments; system-written; Query | Details
{
    public string Resource { get; set; }            // securable resource of the target, e.g. "gl.Document"
    public long TargetId { get; set; }              // unique with Resource: one open assignment per target
    public int AssigneeId { get; set; }             // FK -> core.Users
    public int? AssignedById { get; set; }          // FK -> core.Users; null for the system
    public DateTimeOffset AssignedAt { get; set; }  // refreshed on every reassignment
    public string? Comment { get; set; }            // the assigner's note
    public string? ArgumentsJson { get; set; }      // caption data (number, counterparty), rendered like a notification
}

public interface IAssignments                       // scoped; rides the caller's batch, no extra round trip
{
    void Assign(IDataBatch batch, AssignmentRequest request);                    // upsert on (Resource, TargetId)
    void Release(IDataBatch batch, string resource, IReadOnlyList<long> ids);    // the target reached a terminal state
}
```

Reads follow spec 0021's inbox: `assignments/summary` returns the open count and the new count
(assigned after `UserStamps.AssignmentsSeenAt`), both capped, plus the latest ten;
`assignments/seen` sets the stamp; the full list is the standard search over `core.Assignment`
under a self-scope criterion (`AssigneeId = me() OR AssignedById = me()`), which a stored grant can
widen for a supervisor who needs to see who is sitting on what. After commit, `Assign` and `Release`
publish `assignments.changed` to the old and the new assignee.

**D4 — how the user learns of a new assignment (Leaning):**

1. **The tray count** shows the number of open assignments whenever it is above zero. It is a count
   of work, so opening the tray never resets it; it drops only when an assignment is released or
   handed to someone else.
2. **The tray turns accent** while any assignment is new since the tray was last opened, and
   neutral otherwise (PatternFly's unread and read badge states). Opening the tray calls
   `assignments/seen`.
3. **New rows carry a dot** and sort first; a reassignment back to the user refreshes `AssignedAt`,
   so a returned document is new again, with the returner's comment.
4. **An arrival toast** fires when an assignment arrives while the app is open, unless the user
   assigned it to themselves: "PO-0042 assigned to you by Sara — *Please check the freight line*",
   with an Open action. Several at once collapse into "3 documents assigned to you".
5. **Ageing** is shown as the assignment's age ("3 days"); colouring old items past a tenant
   threshold (NetSuite's ageing, Business Central's cue colours) is a later refinement.
6. **Email or push**, when wanted, rides spec 0021's delivery job through a **channel-only type**
   (`Inbox = false`, [Part 3](#part-3--changes-to-spec-0021-and-its-siblings)), so the user's
   per-type channel preferences still apply and no bell row is written.

Deliberately out: pooled or role-routed assignments (Workday's "first to act removes it for
everyone") — the brief's one-assignee invariant rules them out, and adding them later means a
`RoleId` alternative to `AssigneeId`, not a different model.

### 2.4 Background jobs — D5

**The brief's questions, answered:**

- **Terminal states only.** Pending → Running is not news; the user just asked for it. Progress is
  live state, carried by spec 0020's `job.changed` event, never a notification.
- **No counter of their own.** A terminal notice — `core.export.ready`, `core.import.completed`,
  `core.import.failed`, `core.job.failed` — is an ordinary bell row with the bell's unseen and unread
  semantics. A durable row is what keeps a result findable after the user navigated away (the
  Confluence lesson), and what the user clicks to download.
- **No separate shell button.** The bell's dropdown opens with an **"In progress" section** listing
  the user's own Pending, Running and Held jobs — name, progress bar, progress message, a cancel
  action — read live from `core.Jobs`, the Azure portal's pattern. While any of them exists, the bell
  icon carries a **progress ring**. The section links to My jobs (spec 0020) for history.

**The flow for an export:** the 202 answer raises a feedback toast — "Export started. We'll let you
know when it's ready", with a Track link that opens the bell; the In progress section shows the bar;
on completion, `core.export.ready` lands as an unseen row with a download link and an arrival toast;
on failure, `core.job.failed` does the same with severity Error, and the toast stays until dismissed.

Scheduled runs (`ScheduledFor` set) do not appear in the In progress section — the owner did not
just ask for them — but their failures still notify as today.

### 2.5 The bell — D6, D7, D8

**D6 — one number, unseen (Firm).** The badge shows the **unseen** count (C3) and resets when the
bell opens; read state stays per row (bold, a dot, an "Unread" filter on the page). Spec 0021 already
stores both; the change is to show one number, not two, and to say which. The badge is neutral by
default and turns to the **attention** colour while an unseen row of severity Warning or Error exists
(PatternFly, Novu). The front-end notes' "the number of unread notifications, cleared on click" mixes
the two states; it should read "unseen".

**D7 — one row per subject (Leaning).** GitHub keeps one thread per issue and the latest reason
wins; Zendesk counts tickets, not messages. Spec 0021's `DedupKey` with `OnDuplicate = Replace`
already does this — the convention to adopt is that activity about a record uses `DedupKey =
"<resource>:<id>"`, so "approved" after "commented" on the same document refreshes one unread row
instead of stacking two. Once the row is read, the next event inserts a fresh one.

**D8 — verbs (Leaning).** Read (on click, then navigate to the target), read all, and **clear**:
delete the user's own rows, one or all read ones. Clear answers the Workday and Oracle complaints
for the price of one statement; the bell is not an audit record — the document's history and the job
row are. Archive, saved and snooze are left out: with work in the tray and conditions withdrawn
automatically, the bell carries too little to need triage verbs.

Retention stays as spec 0021 has it (read 90 days, unread 365).

### 2.6 Alerts — D9

**The brief's question:** alerts need what every bell row has — an unseen count on the badge and
per-item read. What sets them apart is **identity**: an alert describes a condition, and without
identity a scheduled check notifies again on every run (Dynamics, Workday, NetSuite), while with it
the user sees one row for as long as the condition holds (SAP Situation Handling).

**The mechanism (Firm):**

- **One row per condition.** The raiser sets `DedupKey` to the condition's identity — rule plus
  anchor record, e.g. `gl.balance-below:account:1042` — with a new `DuplicatePolicy.Ongoing`: **any
  row with the key, read or unread, is a duplicate**. Re-evaluating a condition that still holds
  writes nothing, whether or not the user has read it.
- **Withdraw when it clears.** A new `INotifier.Withdraw(batch, requests)` deletes every row with
  the key — for every recipient, or for the ones named — and publishes `inbox.changed` to them. "Closed
  by anyone disappears for everyone" comes free. If the condition recurs, the next raise inserts a
  fresh row.
- **Updates in place**, when the facts change while the condition holds ("budget at 92%" after
  "90%"), use `Replace` with the same key, which already exists.
- **Recipients** are resolved by the raiser — usually the members of a role, read in the same
  evaluation — as spec 0020 already does for administrators.
- **The open conditions** are simply the user's bell rows of alert types; a filter on the
  Notifications page by category lists them, with no new table.

Who defines business rules, and whether they are evaluated on a schedule (spec 0020 schedules) or
on save (pipeline hooks), is the rule-engine spec's to decide; this mechanism is what it needs from
spec 0021. The platform's own conditions adopt it now: `core.scheduler.gap` and `core.job.held` are
withdrawn when the held jobs are resumed or cancelled, `core.schedule.paused` when the schedule is
taken over or re-activated, and spec 0022's `core.email.failed` when deliveries recover.

### 2.7 Severity and toasts — D10

A type declares a `Severity` — Info, Success, Warning or Error — which a request may override (one
rule can fire at 90% as a warning and at 100% as an error). It sets the row's icon and, through D6,
the badge colour. A type also declares `Toast`: whether a row of the type pops a toast when it
arrives in an open app.

| Arrival | Toast | Auto-dismiss |
|---|---|---|
| Assignment to me (not self-assigned) | Yes | 6 s |
| My job's result: export ready, import completed | Yes | 6 s |
| My job failed; an Error-severity alert | Yes | **No** — stays until dismissed |
| Warning-severity alert | Yes | 6 s |
| Activity (approved, shared, added) | No — the badge suffices | — |
| Feedback on my own action (export started, saved) | Yes | 4 s |

Toast rules, from C6: pause on hover and focus; at most three visible, the rest folded into "N more
— open notifications"; never the only route to an action; announced through a polite live region
present from page load (assertive for errors only), never stealing focus; shown in the visible tab
only, so a user with five tabs open sees one toast; a per-user preference turns arrival toasts off.

### 2.8 Announcements, banners and tenant markers — D11, D12

**D11 — announcements are a separate record, shown as a banner (Firm).** C5 is unanimous, and the
architecture agrees: a fleet-wide maintenance notice written as one bell row per user per tenant is
a fan-out spec 0021 explicitly bounds (`MaxRecipientsPerRequest`, review flag 11), it would linger
unread after the window closed, and it cannot be edited once sent. An announcement is one record:

```csharp
// a sketch; stored in the catalog database, written through the admin surface
public sealed class Announcement
{
    public int Id { get; set; }
    public int? TenantId { get; set; }                  // null: every tenant of the distribution
    public AnnouncementSeverity Severity { get; set; }  // Info | Warning | Critical
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }          // required: nothing nags forever (GitHub's rule)
    public bool Dismissible { get; set; }               // Critical is never dismissible
    public string MessageJson { get; set; }             // text per language code, e.g. { "en": "...", "ar": "..." }
    public string? LinkUrl { get; set; }                // "read more", e.g. a status page
    public DateTimeOffset UpdatedAt { get; set; }       // edits update the same record (Microsoft 365's model)
}
```

- **Authoring.** Tellma operators write announcements from the control plane through new routes on
  the distribution's admin surface (`/api/admin/v1/announcements`), stored in the catalog because
  they cross tenants. Tenant administrators announcing their own news ("post all invoices by the
  28th") is what Oracle and Workday announcements are for; it is the same record in the tenant
  database with an audience of roles — worth adding later, not now.
- **Display.** One banner slot under the app bar. The client fetches the active and upcoming
  announcements (the next 24 hours) at shell load, on hub reconnect and every 15 minutes, and shows
  each from `StartsAt` to `EndsAt` by its own clock, so no push is needed and "maintenance starts in
  10 minutes" still appears on time. Which banner shows is D12's precedence.
- **Dismissal** is permanent per user, kept in a write-behind preference key (spec 0014) holding
  the dismissed ids, pruned as they end. Critical banners — "maintenance in progress, the tenant is
  read-only" — cannot be dismissed.
- **Not in v1:** must-acknowledge notices with an audit of who confirmed (NetSuite) — useful for
  terms changes, a later addition to the same record.

**D12 — what the tenant is gets a marker; what is happening gets the banner (Leaning).** The
platform already knows several states every user of a tenant must see while they hold. They are
derived on the client from state it already receives — nothing is stored — but they last very
differently, and that decides where they go:

- **Tenant markers.** A sandbox is not news; it is what the tenant is, for as long as it exists. A
  `ReadOnly` tenant (spec 0011) often stays so for weeks, as when it is suspended for non-payment.
  A banner that never leaves is one users stop reading, and a permanent banner of top precedence
  would leave no room for anything else on that tenant. Both are therefore **markers beside the
  tenant name in the app bar**, the way SAP marks non-production systems in the launchpad shell
  rather than in its notification centre:
  - **Sandbox:** a "Sandbox" chip so a sandbox is never mistaken for live data while someone tests
    destructive actions.
  - **Read-only:** a "Read-only" chip in the warning colour, opening a short explanation ("You can
    view data but not change it. Contact your administrator."). A tenant that turns read-only during
    a session also raises one toast, and save buttons are disabled with the same reason.

  Both can show at once; on narrow screens they shrink to icons with tooltips.
- **Banners.** The slot holds only what passes in minutes to days, and shows **one banner at a
  time** (Carbon, Atlassian, GOV.UK), chosen in this order:
  1. "Connection lost. Reconnecting…" — the hub has been down for more than a few seconds; critical,
     not dismissible, clears itself.
  2. Critical announcements ("Maintenance in progress") — not dismissible.
  3. "A new version of Tellma is available" (spec 0016's `412 client-outdated`), with a Reload
     action — dismissible for the session.
  4. Warning announcements.
  5. Info announcements.

  Within a level, the one that started first goes first. The others stay reachable through a
  "1 of 3" pager, so nothing is hidden; dismissing a banner shows the next. A banner of lower
  precedence that arrives later never takes the slot — it only updates the pager's count. Because
  every banner in the slot is temporary, none can block the others for long.

### 2.9 What is deliberately left out

| Feature | Seen in | Why not now |
|---|---|---|
| Inline Approve / Reject on a notification | SAP, Oracle, ServiceNow, Salesforce (beta) | Approvals happen on the document; the tray opens it in one click. Revisit with the documents spec. |
| Archive, Saved, Snooze | GitHub, Asana, ServiceNow, Linear, Novu | The bell carries little once work and conditions leave it; Clear and retention suffice. |
| Watching / subscriptions | Jira, GitHub, Odoo | No follow model exists; activity goes to people involved by role in the record. |
| Digests | Workday, Linear, Smashing | An email concern (spec 0022's channel), not in-app. |
| Browser and OS notifications | Odoo, HubSpot | A push channel, which spec 0021 leaves to a later spec. |

---

## Part 3 — Changes to spec 0021 and its siblings

### 3.1 Spec 0021

1. **Context and vocabulary.** Present the bell as one of four in-app surfaces (§2.2 above) and
   remove "a document was assigned to you" from the examples of notifications; §1.2 gains the
   buckets and the surfaces; the §3.5 illustration (`acme.document.assigned` with `Replace`) becomes
   an activity example such as `acme.document.approved` with `DedupKey = "acme.Document:<id>"`.
2. **Type descriptor (§2.3).** `NotificationTypeDescriptor` gains three members:

   ```csharp
   public enum NotificationSeverity : byte { Info, Success, Warning, Error }

   public sealed record NotificationTypeDescriptor(
       string Key, string Category, bool Mutable, string? TargetResource,
       NotificationSeverity Severity = NotificationSeverity.Info,   // icon and badge colour; a request may override it
       bool Toast = false,                                           // pop a toast when a row arrives in an open app
       bool Inbox = true);                                           // false: no bell row; other channels only (assignment email)
   ```

   The Core catalogue sets them: `core.export.ready` and `core.import.completed` Success with
   toast; `core.import.failed` and `core.job.failed` Error with toast; `core.job.held`,
   `core.schedule.paused` and `core.scheduler.gap` Warning with toast; `core.user.added` Info
   without. An `Inbox = false` type skips the insert of §3.3 and runs only the delivery enqueue; the
   preferences page hides its inbox switch.
3. **The row (§2.1).** A `Severity tinyint NOT NULL` column, from the request's override or the
   descriptor; one more parameter in the insert and the replace.
4. **Conditions (§3.1, §3.3).** `DuplicatePolicy` gains `Ongoing`: a duplicate is any row of the
   recipient with the key, read or not. `INotifier` gains `Withdraw(batch, IReadOnlyList<WithdrawRequest>)`
   and `WithdrawAsync`, with `WithdrawRequest(string DedupKey, IReadOnlyList<int>? RecipientIds)`
   (null: everyone). The withdraw statement is one `DELETE … OUTPUT [deleted].[UserId] WHERE
   [DedupKey] = @key [AND UserId IN @recipients]`, feeding the same post-commit `inbox.changed` as
   `Notify` and a `tellma.notifications.withdrawn` counter. The dedup index becomes
   `IX_Notifications_Dedup (DedupKey, UserId) INCLUDE (ReadAt) WHERE DedupKey IS NOT NULL` — no
   `ReadAt` filter, so `Ongoing` and the withdraw-for-everyone seek both use it; `Suppress` and
   `Replace` keep their `ReadAt IS NULL` predicate as a residual.
5. **The summary (§5.2, §5.3).** `InboxSummary` gains `MaxUnseenSeverity` (for the badge colour) and
   `Running`: the caller's own `core.Jobs` rows in `Pending`, `Running` or `Held` with
   `ScheduledFor IS NULL`, newest first, at most `SummaryLatestCount`, with progress percent, key and
   arguments — one more result set in the same `Read` batch. The client refreshes the summary on
   `job.changed` as it does on `inbox.changed`.
6. **A clear verb (§5.2).** `inbox/clear` (`IdsRequest`) and `inbox/clear-read`, deleting the
   caller's own rows, publishing `inbox.changed`.
7. **The client protocol (§5.4).** The badge shows `Unseen` only (`99+` past the cap), in the
   attention colour when `MaxUnseenSeverity` is Warning or Error; the dropdown opens with the In
   progress section; arrival toasts follow §2.7 above, by diffing `Latest` against the previous
   summary for rows whose type has `Toast`. The client therefore needs the type catalogue at load,
   not only on the preferences page — **review flag 10 flips**: serve the descriptors once per
   distribution version (they change only with a deployment), rather than from
   `notification-preferences/get`.
8. **Decisions and review flags.** Decision 16 gains `Ongoing` and withdrawal; review flag 11 closes
   — broadcasts are announcements, not notifications; review flag 2 notes that `Ongoing` shares
   `Replace`'s best-effort race.

### 3.2 Siblings

| Spec | Change |
|---|---|
| **0020** | A filtered index for the running-jobs read: `IX_Jobs_RequestedActive (RequestedById, CreatedAt DESC) WHERE Status IN ('Pending', 'Running', 'Held')` — `IX_Jobs_RequestedBy` would scan a heavy user's whole history to find no running job. `core.job.held` and `core.scheduler.gap` move to `Ongoing` with keys, withdrawn by `resume` and `cancel` of the held rows; `core.schedule.paused` keys by schedule and is withdrawn by `take-over` and re-activation. The descriptors take the severities of §3.1 item 2. |
| **0022** | `core.email.failed` moves to `Ongoing` and is withdrawn when a delivery succeeds after failures. |
| **0019** | The export and import descriptors take the severities and toasts of §3.1 item 2; the 202 answers drive the "Export started" feedback toast (client only). |
| **0014** | `core.UserStamps` gains `AssignmentsSeenAt`; a write-behind preference key `announcements.dismissed`. |
| **0011** | Admin surface v1 gains `GET`, `PUT` and `DELETE /api/admin/v1/announcements[/{id}]`; the catalog gains the announcements table; the tenant API gains a member-only `announcements/active` read, cached in-process for a minute. |
| **0018** | `core.user.added` takes Info without toast. |

### 3.3 New: assignments and announcements

Assignments (D3, D4) and announcements (D11, D12) are not notifications: they are not per-recipient
rows written by `INotifier`, and they have their own lifecycle. Putting them in spec 0021 would grow
an already long spec with two unrelated models; **a new spec, "In-app attention: assignments,
announcements and banners"**, keeps each spec on one subject. It owns `core.Assignments`,
`IAssignments`, the assignment endpoints and the `assignments.changed` event, the announcement
record and its endpoints, the banner slot's precedence, the tenant markers, and the tray and banner
client protocols.
Spec 0021's hub carries the new event; nothing else in spec 0021 refers to it.

---

## Part 4 — Open questions

1. **Read on visit.** GitHub marks a notification read when the user opens its target from
   anywhere. Tellma marks read only on click from the bell. Doing it on visit costs a write on a
   details read (or a client-side call when the client knows of an unread row for the target).
   Worth it?
2. **Actor exclusion.** Teams refuses to let a user notify themselves; Asana leaves the user's own
   actions out of their inbox. Spec 0021 leaves it to the raiser. A rule in `INotifier` that drops
   the actor would break job notices, whose run-as actor is the requester — so a raiser convention,
   or a descriptor flag?
3. **A generic success notice.** A user-started job whose handler raises no success notice simply
   leaves the In progress section. Add a `core.job.succeeded` notice for requested, non-scheduled
   jobs whose handler declares none, or require every user-facing handler to notify?
4. **Assignment ageing.** Colour assignments past a tenant threshold, and send an overdue reminder
   as an alert to the assignee (and their manager?) — and where does that threshold live?
5. **Tenant-admin announcements.** Ship with the operator announcements, or later?
6. **Tab title and favicon.** Prefix the browser tab title with the open-assignment count, as many
   mail clients do? Useful for users who keep Tellma in a background tab; noisy for the rest.
7. **Role-routed assignments.** Confirm that pooled work ("any member of Accounts Payable may take
   it") stays out, given the one-assignee invariant.

## Part 5 — Misalignments to resolve

1. **Spec 0021's context** names "a document was assigned to you" as a notification, and its §3.5
   illustration raises `acme.document.assigned` into the inbox. Under D3 an assignment is never a
   bell row.
2. **"Two counters on the bell icon"** (spec 0021's context) against D6's one number.
3. **The front-end notes'** bell shows "the number of unread notifications" and "clears the unread
   notifications counter" on click — that is the unseen count, and should say so; the "my
   assignments" tray section, still a TODO there, is §2.3 above.
4. **Spec 0021's review flag 11** anticipates a "tenant broadcast type" of notification; D11 makes
   broadcasts a separate record instead.
5. **The architecture overview** lists "baseline notification templates" among a module's
   contributions; with D3, a module also contributes assignment captions, and with D9, rule-based
   alert types — worth naming when the documents and rules specs are written.
