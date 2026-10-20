# Ad-hoc Report Sharing — Market Analysis and Recommendation

**Purpose:** Survey how the most widely used enterprise systems let users author ad-hoc reports and
share them — with individual users, with roles or groups, by administrators — where people find
them, and above all whether sharing a report shares the data behind it. Settle, from that evidence,
how Tellma report definitions and dashboards should be owned, shared, found and secured.

**Status:** Research and recommendation, 13 October 2026. Not a spec: the recommendations are inputs
to the report-definitions and dashboards specs, and where they conflict with a later spec, the spec
wins.

**Method.** Sixteen products were examined against eight questions, through vendor documentation
fetched and read on 13 October 2026. Where documentation was silent and source code was public
(Odoo, Frappe), the shipping source was read. Tellma 1.0's own schema and permission loader were
re-read. Claims resting only on community or partner material are marked *(2°)*; claims that could
not be confirmed are marked *(unverified)*; deductions from verified facts are marked *(inferred)*.
Confidence markers on recommendations follow the convention of the other research documents:

| Marker | Meaning |
|---|---|
| **Firm** | Well supported by evidence and by platform conventions already in force. Changing it should require new information, not a preference. |
| **Leaning** | The better of two defensible options. Worth revisiting if implementation or usage data pushes back. |
| **Open** | A genuine coin-flip, or something that needs a usability test. Listed in [Part 4](#part-4--open-questions). |

**Products.** Three CRM/platform suites — Salesforce (Lightning Reports & Dashboards), Microsoft
Dynamics 365 / Dataverse model-driven apps, HubSpot. Four ERP suites — Oracle NetSuite, SAP S/4HANA
Cloud (with SAP Analytics Cloud), Oracle Fusion Cloud (OTBI and BI Publisher), Workday. Five platform
and mid-market ERPs — ServiceNow, Odoo 17–19, Frappe/ERPNext, Acumatica, Sage Intacct. Four
self-service BI tools, included because their sharing models are the most mature — Power BI, Tableau,
Looker, Metabase. Tellma 1.0 is the baseline.

---

## The eight questions, answered in brief

"The field" below means the sixteen products; the detail is in Part 1, the design in Part 2.

| # | Question | What the field does | Recommendation for Tellma |
|---|---|---|---|
| 1 | Can a user share a report they authored with another user? | **Yes in 12 of 16.** The exceptions are deliberate: Acumatica and Frappe grant only through roles, SAP keeps query authoring with key users, Metabase grants only to groups. The shared thing is always a **pointer** to the same report; "Save as" makes a copy. | **Yes**, View or Edit, as a pointer. [D3](#23-ownership-and-grants--d3) |
| 2 | Can a user share it with a role (every member)? | **Yes in 12 of 16** (role, group, team or security group); in SAP, Odoo, Acumatica and Metabase only administrators or key users distribute to roles or groups. Often a **separate, stronger privilege** than sharing with one user (Workday, Salesforce, ServiceNow, NetSuite). | **Yes**, under a separate `Publish` grant. [D4](#24-who-may-share--d4) |
| 3 | Can an admin see user-authored reports? | **Mostly yes, and the products that said no regret it.** Workday, NetSuite, HubSpot, Odoo, Intacct, Power BI, Tableau, Looker and Metabase give admins a UI. Salesforce offers only an API scope for cleanup; Dataverse documents nothing, and admins use third-party tools. | **Yes**, through the ordinary read grant, with private reports hidden from admin lists until asked for. [D6](#26-administrators--d6) |
| 4 | Can an admin share an existing report with a user? | **Yes** wherever the admin can see the report. Salesforce cannot reach private folders; role-only products (Acumatica, Frappe) can only grant via a role. | **Yes.** [D6](#26-administrators--d6) |
| 5 | Can an admin share an existing report with a role? | **Yes in every product.** | **Yes.** [D6](#26-administrators--d6) |
| 6 | Where do users find their reports and those shared with them? | A **reports hub** with "Mine / Shared with me / All / Favorites" (Salesforce, ServiceNow, Intacct, HubSpot, Power BI, Tableau); reports **in context** on the entity's list page (Dataverse, Odoo, Frappe, NetSuite); **menu placement** for published reports (NetSuite, Acumatica, Intacct, SAP, Tellma 1.0). Products without a "shared with me" view (Dataverse, Workday, Looker, Metabase) have documented discoverability complaints. | **All three surfaces**, plus pins and Launcher search. [D7](#27-where-reports-live--d7) |
| 7 | Does sharing a report grant the recipient access to the underlying data? | **No, by default, in every ERP and CRM examined:** live reports run under the viewer's security. The exceptions are named, privilege-gated features (Salesforce static dashboards, NetSuite Run Unrestricted, Intacct Primary Consumer, Odoo frozen shares) or acknowledged leaks (ServiceNow aggregate charts, Frappe query reports, Acumatica inquiries, Oracle unsecured data models). **Scheduled delivery is the common leak:** most products email the scheduler's view of the data. | **No — never, for live reports.** Delivery renders per recipient. A deliberate data grant, if ever needed, is a separate, audited feature. [D1](#21-sharing-shares-the-definition-never-the-data--d1), [D11](#211-scheduled-delivery--d11) |
| 8 | Can a user share a dashboard they authored? | **Yes in 10 of 16**, usually with the same mechanics as reports; in NetSuite, SAP, Odoo, Frappe, Acumatica and Metabase dashboards are published to roles or groups by privileged users only. Whether dashboard access reaches the reports on it splits the field, and the products that do not propagate (Salesforce, Dataverse, Metabase) document "dashboard shared but its reports are not" as their most common failure. | **Yes, same model.** Dashboard access reaches the report *definitions* on it, never their data. [D8](#28-dashboards--d8) |

---

## Part 1 — Market analysis

### 1.1 The field at a glance

| Product | Q1 user → user | Q2 user → role/group | Q3 admin sees private | Q4/Q5 admin shares others' reports | Q7 live report runs as | Q8 dashboard sharing |
|---|---|---|---|---|---|---|
| **Salesforce** | Yes, but only by sharing the **folder**; View / Edit / Manage | Roles, roles + subordinates, public groups, territories, whole org | **No UI**; API scope `allPrivate` for query and delete only | Yes if in a shared folder; private folders unreachable | Report: viewer. **Static dashboard: a fixed running user** | Same folder model; does not reach source-report folders |
| **Dynamics 365 / Dataverse** | Yes; per-grantee Read / Write / Delete / Assign / Share | **Teams only** — not security roles, not business units | Not documented; third-party tools *(2°)* | Only with Share privilege at depth *(inferred)* | **Always viewer** | Yes; contained personal views and charts must be shared separately |
| **HubSpot** | Yes; View or View and edit (Enterprise) | Teams (Enterprise), Everyone | Yes (Super Admin, reporting admin) | Yes, plus change owner | Viewer (documented pipeline-restriction leak) | Yes; **the dashboard's access governs its reports** |
| **NetSuite** | Yes (saved-search audience); one "Allow audience to edit" flag | Roles, departments, subsidiaries, groups, Public | Yes ("Show All Private Searches") | Yes | Viewer; **"Run Unrestricted"** lifts row restrictions per search | Published **to roles only**, by permission; no propagation |
| **SAP S/4HANA (+SAC)** | No for queries and KPIs (key-user objects); SAC stories yes | Key users: catalog → business role → launchpad page | Not private (tenant objects) | Via business roles | **Always viewer** (CDS access control) | Admin-owned pages per role; tile needs its catalog too |
| **Oracle Fusion (OTBI/BIP)** | Yes, by catalog ACL in a shared folder; My Folders unshareable | Application roles, catalog groups | Catalog Admin View *(2°)* | Yes for shared folders | OTBI: viewer. **BI Publisher unsecured data model: all rows** | Catalog ACL; needs read on each embedded analysis |
| **Workday** | Yes, run-only; report writers may copy | Security groups — **only those already able to read the data source** | Yes (Manage: All Custom Reports domain) | Yes; ownership transfer task | **Always viewer** | Discovery Boards: Can View / Can Edit; no propagation |
| **ServiceNow** | Yes; never edit (Core UI); viewer/editor (Platform Analytics) | Groups; roles for admins | Not in report list; via table | Yes (`report_admin`, `dashboard_admin`) | Lists: viewer. **Aggregate charts count rows the viewer cannot read** | Yes; edit does not propagate, view of table visualizations does |
| **Odoo 17–19** | Favorites: no (v17/18), yes (v19); spreadsheets Owner/Editor/Viewer | Dashboards by user group, admin only | Yes (User-defined Filters) | Yes | Viewer; **"Freeze and share" snapshot = sharer's data** | Admin-only, group-based |
| **Frappe / ERPNext** | **No private reports**: a saved report is visible to every role that reads its doctype | Roles | Yes (nothing is private) | Via roles only | Report Builder: viewer. **Query/Script: shallow post-filter** | Manager roles only; each chart re-checked |
| **Acumatica** | No — "you never assign access rights directly to individual users" | Roles, admin-set | Inquiries never private | Via roles | Viewer session, but **inquiry access is independent of screen access** *(2°)* | Role-owned; locked widgets show a placeholder |
| **Sage Intacct** | Yes (financial and interactive reports: users/groups Allow/Deny) | User groups, Everyone | Yes ("Include private") | Yes | Viewer; **"Primary Consumer"** is an admin-only per-report data grant | Yes; reports may be placed only where the user has permission |
| **Power BI** | Yes, read-only; edit needs a workspace role; Reshare on by default | Security groups, app audiences | Yes (24-hour admin access to any My workspace) | Yes | Viewer against the model's RLS; **no RLS = all model data; editors bypass RLS** | Yes; **propagates** to reports and model |
| **Tableau** | Yes via permission rules; "Share" only notifies | Groups, group sets | Yes (Personal Space visible to site admins) | Yes | **Embedded credentials: the publisher's** | Same as views |
| **Looker** | Folder grant or URL ("you are sharing the query, not the actual data") | Groups | Yes (People folder; personal folders public by default) | Yes | **Viewer**, access filters apply even to admins | Same folder; tiles blank without model access |
| **Metabase** | No — groups only, admin only | Groups, admin only | Yes ("Other users' personal collections") | Groups only | **Collection access shows saved results**; SQL questions bypass row security | No propagation |
| **Tellma 1.0** | No — administrators author definitions | Roles, including public roles (menu visibility) | n/a | Roles only | **Viewer** (ordinary view permissions) | Same role list |

Sources for every cell are in [§1.7](#17-evidence-by-product).

### 1.2 What the field has converged on

**C1 — A live report runs under the viewer's security.** Every ERP and CRM examined defaults to it,
and the vendors say so in nearly the same words. Microsoft: "Having access to a particular view
doesn't mean you also have access to the data that's available in the view." Workday: "Workday
considers all security permissions before a user can access and run a shared report." Odoo: "Live
data is computed for the user who opens the spreadsheet, not the sharer." Looker: "you are sharing
the query, not the actual data." Sage Intacct repeats that recipients "must have the permissions to
access the data included in the report". A consequence every vendor documents: **the same report
shows different numbers to different people**, and Odoo goes as far as warning that a company-wide
dashboard given to a group limited to its own documents "would produce misleading results".

**C2 — The shared thing is a pointer; a copy is an explicit "Save as".** Without exception. Changes
by the owner reach every recipient at once ("Changes to a shared report appear to other users right
away" — Workday), and a copy starts private and unshared (Workday: a copy defaults to "don't share").

**C3 — Role-wide distribution is a stronger privilege than person-to-person sharing.** Workday has
three separate domains (all authorised users / specific groups / specific users). Salesforce
separates "Send to Groups and Roles" from adding recipients. ServiceNow reserves sharing with roles
to `admin` and `viz_admin`. NetSuite gates the whole audience tab behind "Publish Search". Metabase
lets only administrators grant collections. Products that make role distribution an
administrator act (SAP, Acumatica, Odoo dashboards, Tellma 1.0) are the strict end of the same idea.

**C4 — Administrators can see private content, and the ones that could not had to add a way.**
Salesforce added the API-only `allPrivate` scope "when deactivating users or auditing report
storage"; Dataverse administrators rely on an XrmToolBox plug-in *(2°)*. Products designed later make
it explicit and grantable — Workday's "Manage: All Custom Reports" domain, NetSuite's Analytics
Administrator permission, Power BI's time-boxed, audited "Get Access".

**C5 — Ownership transfer is an administrator function, and authored content outlives its author.**
HubSpot, Workday, Tableau, Looker, Metabase, Power BI and Salesforce dashboards all have it. Workday
recommends it as an off-boarding step and re-checks the new owner's data access.

**C6 — The leak is asynchronous delivery.** Interactive viewing is viewer-scoped almost everywhere;
scheduled email is not. Salesforce subscriptions "Run Report As" a chosen user ("Recipients see the
same report data as the person running the report"); HubSpot recurring emails use "the permissions
of the user who set up the email"; ServiceNow uses a "Run as" user and warns "there may also be a
security risk in setting this user"; Frappe's Auto Email Report has a field literally labelled
"Based on Permissions For User"; Power BI's subscription image renders "based on the owner of the
subscription"; Metabase recipients "can see whatever the notification creator can see". The
products that fixed it did so with **per-recipient rendering**: Looker's "Run schedule as
recipient", Workday's "Share based on Securing Entity" bursting, SAP Analytics Cloud bursting,
Oracle Analytics workbook bursting.

### 1.3 Where products genuinely disagree

**Unit of sharing — folder or object.** Salesforce shares only folders ("you share the folder, not
the report or dashboard itself"), and folder-per-audience sprawl is its most-cited friction *(2°)*.
Looker and Metabase also share containers; Dataverse, HubSpot, Workday, Intacct, ServiceNow and Power
BI share the object. Microsoft's own Power BI guidance tells customers to "use per-item permissions
sparingly" because revoking 40 individually shared reports is "tedious and error prone" — an argument
for **role grants as the primary path**, not for folders.

**Does dashboard access reach the reports on it?** Three positions:

- **No** — Salesforce, Dataverse, Metabase, Workday, Oracle. Every one documents the broken-dashboard
  failure: Salesforce's troubleshooting article lists "the dashboard folder is shared with the user,
  but the source report folder is not"; Dataverse tells owners to "explicitly share any contained,
  personal charts or personal views"; Metabase shows a permissions notice per card.
- **Yes, entirely** — HubSpot ("Reports that are added to your dashboards will be controlled by the
  dashboard's access settings") and Power BI (sharing a dashboard grants its reports and model).
- **Partly** — ServiceNow: viewing reaches table-data visualizations, editing does not.

Because all of these products evaluate data per viewer anyway, propagation of *definition* access is
safe; the products that refused it bought a support problem and no security.

**What a recipient without data access sees.** Workday's Discovery Boards hide inaccessible charts
from viewers ("no indication that other content exists") but show editors a placeholder; Acumatica
shows a grey lock; Looker blanks the tile; Oracle hides the analysis and marks it "!" for editors;
HubSpot shows "partial data, a blank report, or an error". Workday's split is the most considered.

**Who may author at all.** Frappe restricts saving reports to Report Manager; SAP to key users;
Acumatica to holders of the inquiry form; Workday to Report Writers; NetSuite, Salesforce,
Dataverse, HubSpot and Odoo let ordinary users author private views or reports.

### 1.4 The data-granting exceptions, and how each is guarded

Every product that lets a shared report show the recipient more than they could otherwise see does
it through a named feature behind its own privilege — or ships it as a leak and documents the
workaround.

| Product | Feature | What is granted | Guard |
|---|---|---|---|
| Salesforce | Static dashboard "View Dashboard As" another person | The running user's data, for every viewer. Docs: viewers "could see personally identifiable information" | View My Team's Dashboards (subordinates) or View All Data (anyone). Viewer-scoped dynamic dashboards are capped at 5 (Enterprise) |
| Salesforce | Reporting snapshots | "This bypasses all security settings" | Only Modify All Data may choose another running user |
| NetSuite | Saved search "Run Unrestricted" | Lifts role *restrictions* (subsidiary, department, class, location, employee) but not record-type *permissions* | Described as an administrator option; companion "Disallow Drill Down" limits viewers to aggregates |
| Sage Intacct | Interactive report "Primary Consumer" role | Run-only access to a report's data outside the user's permissions | Company-wide opt-in, per report, full report-writer admins only *(page 404; confirmed via search excerpts)* |
| Workday | Report output sharing | Scheduled output "exactly as you see it, regardless of their security permissions" | Dedicated domain, an "I agree" acknowledgement, a 2,000-recipient cap |
| Odoo | Spreadsheet "Freeze and share" / dashboard Share link | A frozen snapshot of the sharer's data on a public token URL | The link is re-checked on every hit and dies once its creator loses dashboard access (source) |
| Oracle | BI Publisher data model on base tables | All rows, for anyone allowed to open the report | Oracle advises minimising who may author data models |
| Power BI | Model without RLS; workspace Contributor and above | All model data | None beyond workspace roles; Microsoft advises separate creator and consumer workspaces |
| Tableau | Embedded credentials | The publisher's database identity | Ownership change deletes embedded credentials |
| ServiceNow | Aggregate charts; Performance Analytics | Counts include records the viewer cannot read; PA collection "runs without any restrictions" | Only PA power users may add PA widgets |
| Frappe | Query and Script reports | SQL aggregates and non-link columns are not filtered for the viewer (source) | Authoring requires Script Manager |
| Acumatica | Generic inquiries | Data behind screens the user cannot open *(2°)*; subaccount restrictions excluded "for performance reasons" | Role access to the inquiry; the 2024 R1 removal of "open to all" defaults |

Two patterns stand out. **Deliberate grants are rare, narrow and administrator-only** — Primary
Consumer and Run Unrestricted were added because "let the CFO's team see company totals without
general-ledger access" is a real need. **Accidental grants come from a second query path** that does
not go through the platform's row security (raw SQL, pre-aggregated scores, an imported model, a
stored credential). Tellma has one query path for user-authored reports; keeping it that way is the
whole of D1.

### 1.5 Where users find reports

| Surface | Who does it | Notes |
|---|---|---|
| Reports hub with scope filters | Salesforce (Created by me, Shared with me, Favorites; Analytics tab), ServiceNow (My reports / Group / Global / All), Sage Intacct (Favorites, All, Memorized, Scheduled, My reports), HubSpot (All / Custom / Favorites; Owner and Assigned filters), Power BI and Tableau (Shared with me) | The only place a user can answer "what has been shared with me?" |
| In context, on the entity's list page | Dataverse view selector, Odoo search-bar Favorites, Frappe "Saved Reports" in the list-view switcher, NetSuite "Available as List View", Acumatica filter tabs | The surface ordinary users actually use; scoped to one entity |
| Menu placement for published reports | NetSuite "Show in Menu" and center links, Acumatica workspaces, Intacct "Save and add to a menu", SAP launchpad tiles, Tellma 1.0 `ShowInMainMenu` | Administrator-controlled; how a role's standard reports appear |
| Personal shortcuts | Favorites everywhere; SAP "Save as Tile"; Power BI favorites; Metabase bookmarks | User-owned |
| Dashboards and home | Every product | Reports as widgets |

Missing a "shared with me" list is a documented pain: Dataverse mixes shared views into personal
ones; a Workday partner publishes audit reports just to discover what managers can see *(2°)*.

### 1.6 Where Tellma 1.0 stood

`ReportDefinitions` were authored by administrators. `ReportDefinitionRoles` (and the same shape for
dashboards and printing templates) listed the roles that could see a definition, and the permission
loader returned the shared ids as "definitions with `ShowInMainMenu = 1` granted to one of my roles
or to a public role". The list drove main-menu visibility only: running a report applied the
viewer's ordinary view permissions to its collection, so 1.0 already answered Q7 "no". A diagnostic
function, `ft_ReportsUnauthorizedAccess__SELECT`, found users whose favourites pointed at reports no
longer granted to them — the stale-pointer problem the shell research answers with gracefully
degrading pins.

What 1.0 lacked: user authoring, person-to-person sharing, ownership, a "shared with me" surface,
and any delivery.

### 1.7 Evidence by product

Each line gives the decisive sources; quotations above are from these pages.

- **Salesforce** — folders: [Report and Dashboard Folders](https://help.salesforce.com/s/articleView?id=analytics.analytics_sharing.htm&type=5), [Share a Lightning folder](https://help.salesforce.com/s/articleView?id=analytics.rd_folders_share_enhanced.htm&type=5), [FolderShare grantee types](https://developer.salesforce.com/docs/atlas.en-us.api_meta.meta/api_meta/meta_foldershare.htm); admin: [sharing permissions](https://help.salesforce.com/s/articleView?id=analytics.analytics_sharing_permissions.htm&type=5), [`allPrivate` cleanup KA 000383270](https://help.salesforce.com/s/articleView?id=000383270&type=1); data: [dashboard data visibility considerations](https://help.salesforce.com/s/articleView?id=analytics.dashboards_view_as_considerations.htm&type=5), [dynamic dashboards](https://help.salesforce.com/s/articleView?id=analytics.dashboards_dynamic_overview.htm&type=5), [report subscriptions](https://help.salesforce.com/s/articleView?id=analytics.reports_subscribe_lex.htm&type=5), [reporting snapshots](https://help.salesforce.com/s/articleView?id=analytics.data_defining_analytic_snap.htm&type=5); dashboards: [component errors KA 000386112](https://help.salesforce.com/s/articleView?id=000386112&type=1), [change owner](https://help.salesforce.com/s/articleView?id=analytics.dashboards_change_owner.htm&type=5); navigation: [Analytics Home](https://help.salesforce.com/s/articleView?id=analytics.analytics_home.htm&type=5).
- **Dynamics 365 / Dataverse** — [saved queries](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/saved-queries), [personal views on a grid](https://learn.microsoft.com/en-us/power-apps/user/grid-filters-advanced), [share rows](https://learn.microsoft.com/en-us/power-apps/user/share-row), [access to public system views](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/manage-view-access), [create or edit views](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/create-edit-views), [dashboards and charts](https://learn.microsoft.com/en-us/power-apps/user/track-your-progress-with-dashboard-and-charts), [Power BI modelling guidance](https://learn.microsoft.com/en-us/power-bi/guidance/powerbi-modeling-guidance-for-power-platform); admin tooling *(2°)*: [Personal Views Migration plug-in](https://www.nuget.org/packages/Carfup.XTBPlugins.PersonalViewsMigration/).
- **HubSpot** — [reports list](https://knowledge.hubspot.com/reports/manage-reports-in-your-reports-list), [dashboards](https://knowledge.hubspot.com/dashboards/manage-your-dashboards), [limit access to assets](https://knowledge.hubspot.com/account-security/limit-access-to-your-hubspot-assets), [share or export](https://knowledge.hubspot.com/dashboard/email-or-export-reports-and-dashboards), [user permissions](https://knowledge.hubspot.com/user-management/hubspot-user-permissions-guide), [deactivating users](https://knowledge.hubspot.com/user-management/remove-hubspot-users).
- **NetSuite** — [saved-search audiences](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N678294.html), [permissions for searches](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N664557.html), [admin access to private searches](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N687899.html), [Run Unrestricted / Disallow Drill Down](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_160795034394.html), [sharing custom reports](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N745002.html), [sharing workbooks](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_0801122218.html), [publishing a dashboard](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N633690.html).
- **SAP** — [CDS access control](https://help.sap.com/doc/abapdocu_751_index_htm/7.51/en-US/abennews-740_sp10-abap_cds.htm); embedded analytics distribution via catalogs and roles *(2°, SAP Learning)*: [Describing Embedded Analytics](https://learning.sap.com/courses/implementing-sap-s-4hana-cloud-public-edition/describing-embedded-analytics_bd89eeb8-5bb6-43a7-a8aa-906a4d32a3c4); SAC sharing and broadcast vs bursting *(2°)*: [Managing Files](https://learning.sap.com/courses/exploring-sap-analytics-cloud-for-sport-industry/managing-files), [bursting blog](https://blogs.sap.com/2021/11/26/burst-story-to-deliver-personalized-content-to-each-recipients-based-on-their-respective-data-authorization/). help.sap.com pages render only with JavaScript and could not be fetched, so the SAP row rests partly on SAP Learning and SAP blog material.
- **Oracle Fusion** — [Manage OTBI folders](https://docs.oracle.com/en/cloud/saas/otbi/otbi-admin/manage-otbi-folders.html), [catalog permissions](https://docs.oracle.com/en/cloud/saas/sales/fasry/permissions-for-catalog-objects.html), [SCM reports security (secured vs unsecured data models)](https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/25d/fassc/overview-of-security-for-oracle-fusion-cloud-scm-reports.html), [troubleshooting analyses and dashboards](https://docs.oracle.com/en/cloud/saas/otbi/otbi-admin/troubleshoot-issues-analyses-and-dashboards.html), [agent Run As modes (legacy Oracle BI)](https://docs.oracle.com/cd/E12096_01/books/AnyUser/ibotgeneral.html).
- **Workday** — [sharing options](https://doc.workday.com/admin-guide/en-us/reporting-and-analytics/custom-reports-and-analytics/set-up-reports/gth1496859316438.html), [share custom report definitions](https://doc.workday.com/admin-guide/en-us/reporting-and-analytics/custom-reports-and-analytics/report-administration/dan1370797803507.html), [report security](https://doc.workday.com/workday-education/en-us/course-manuals/workday-reporting/report-security.html), [report groups and output sharing](https://doc.workday.com/admin-guide/en-us/reporting-and-analytics/custom-reports-and-analytics/run-and-schedule-reports/dan1370797222795.html), [Discovery Board security](https://doc.workday.com/admin-guide/en-us/reporting-and-analytics/discovery-boards/discovery-board-concepts/jpj1601505705531.html), [termination impact](https://doc.workday.com/admin-guide/en-us/human-capital-management/staffing/hire-and-terminate/hire-and-terminate-concepts/dan1370797453963.html).
- **ServiceNow** — [share a report](https://www.servicenow.com/docs/r/bB1Dk50bqXxOZKZ5dD_4fw/QLYGPjMghds83YNJ2nRFNQ), [distribute reports (aggregates and ACLs)](https://www.servicenow.com/docs/bundle/zurich-now-intelligence/page/use/reporting/concept/c_DistributeReports.html), [reports list](https://www.servicenow.com/docs/r/bB1Dk50bqXxOZKZ5dD_4fw/DEIv5D69NPkEu9BU_Wy6xg), [share a data visualization](https://www.servicenow.com/docs/r/zurich/now-intelligence/share-dv-ac.html), [share a dashboard](https://www.servicenow.com/docs/r/zurich/now-intelligence/share-db-in-ac.html), [dashboard permissions](https://www.servicenow.com/docs/r/3dUp4IlSpaXzwDcoDj_v6g/bisCmUN0C5vt6O_6Q5NOyA), [scheduled report emails](https://www.servicenow.com/docs/r/pVA_t2FBRJGAybmbtWqZ2A/5foLLDcdyo8QDl7YYtBGeg), [PA data collection](https://www.servicenow.com/docs/r/bB1Dk50bqXxOZKZ5dD_4fw/gmgavPuXK3KleMIqIP8sAA).
- **Odoo** — [search and favorites (v19)](https://www.odoo.com/documentation/19.0/applications/essentials/search.html), [spreadsheet sharing](https://www.odoo.com/documentation/19.0/applications/productivity/spreadsheet/share_collaborate.html), [dashboards](https://www.odoo.com/documentation/19.0/applications/productivity/dashboards.html); source: [`ir_filters.py` 17.0](https://github.com/odoo/odoo/blob/17.0/odoo/addons/base/models/ir_filters.py) and [19.0](https://github.com/odoo/odoo/blob/19.0/odoo/addons/base/models/ir_filters.py), [`base_security.xml` 19.0](https://github.com/odoo/odoo/blob/19.0/odoo/addons/base/security/base_security.xml), [dashboard share model](https://github.com/odoo/odoo/blob/19.0/addons/spreadsheet_dashboard/models/spreadsheet_dashboard_share.py).
- **Frappe / ERPNext** — source: [`report.py`](https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/report/report.py), [`query_report.py`](https://github.com/frappe/frappe/blob/develop/frappe/desk/query_report.py), [`reportview.py`](https://github.com/frappe/frappe/blob/develop/frappe/desk/reportview.py), [Auto Email Report](https://github.com/frappe/frappe/tree/develop/frappe/email/doctype/auto_email_report), [`dashboard.py` chart cache](https://github.com/frappe/frappe/blob/develop/frappe/utils/dashboard.py); docs: [Role Permission for Page and Report](https://docs.frappe.io/erpnext/user/manual/en/role-permission-for-page-and-report).
- **Acumatica** (via the near-identical MYOB Acumatica help) — [inquiry access rights](https://help.myob.com.au/myobadvancedhelpdraft/Docs/Published/UserGuide/GI_Access_Rights_GeneralInfo.html), [2024 R1 access-rights changes](https://help.myob.com.au/advanced/Docs/Published/ReleaseNotes/RN_2024_R1_Platform_Changes_to_Access_Rights.html), [row-level security overview](https://help.myob.com.au/advanced/Docs/Published/UserGuide/RS__RS_Features.html), [dashboard access rights](https://help.myob.com.au/advanced/Docs/Published/UserGuide/RPT_Administering_Dashboard_Forms_Access_Rights_Concept.html), [roles principle](https://beacon.acumatica.com/r/System-Administration-Guide/Configuring-User-Roles); inquiry-vs-screen access *(2°)*: [DataSelf KB](https://kb.dataself.com/ds/acumatica-rights-by-role-for-generic-inquiries-gi-).
- **Sage Intacct** — [share reports](https://www.intacct.com/ia/docs/en_AU/help_action/Reporting/Distribute_and_run_reports/Schedule_and_deliver_reports/share-reports-with-others.htm), [grant and restrict access to reports](https://www.intacct.com/ia/docs/en_ZA/help_action/Reporting/control-access-to-reports.htm), [permission basics](https://www-p504.intacct.com/ia/docs/en_US/help_action/Reporting/Get_started/Permission-basics.htm), [reports center](https://www-p504.intacct.com/ia/docs/en_US/help_action/Reporting/Reports_Center/access-reports-in-reports-center.htm), [dashboard access for groups](https://www-p504.intacct.com/ia/docs/en_US/help_action/Administration/User_groups/allow-user-groups-access-to-dashboards.htm), [Primary Consumer release note](https://www.intacct.com/ia/docs/en_AU/releasenotes/2024/2024_Release_1/Reporting-Advanced/2024-R1-primary-consumer-role.htm) *(404 on fetch)*.
- **Power BI** — [share reports and dashboards](https://learn.microsoft.com/en-us/power-bi/collaborate-share/service-share-dashboards), [workspace roles](https://learn.microsoft.com/en-us/power-bi/collaborate-share/service-roles-new-workspaces), [row-level security](https://learn.microsoft.com/en-us/fabric/security/service-admin-row-level-security), [report-consumer security planning](https://learn.microsoft.com/en-us/power-bi/guidance/powerbi-implementation-planning-security-report-consumer-planning), [subscriptions](https://learn.microsoft.com/en-us/power-bi/collaborate-share/end-user-subscribe), [admin workspace access](https://learn.microsoft.com/en-us/fabric/admin/portal-workspaces), [Shared with me](https://learn.microsoft.com/en-us/power-bi/explore-reports/end-user-shared-with-me).
- **Tableau** — [permissions](https://help.tableau.com/current/online/en-us/permissions.htm), [effective permissions](https://help.tableau.com/current/online/en-us/permission_effective.htm), [sharing](https://help.tableau.com/current/pro/desktop/en-us/shareworkbooks.htm), [Personal Space](https://help.tableau.com/current/online/en-us/personal_space.htm), [publishing authentication](https://help.tableau.com/current/pro/desktop/en-us/publishing_sharing_authentication.htm), [row-level security options](https://help.tableau.com/current/online/en-us/rls_options_overview.htm), [content owner](https://help.tableau.com/current/online/en-us/owner.htm).
- **Looker** — [access control](https://docs.cloud.google.com/looker/docs/access-control-and-permission-management), [access levels](https://docs.cloud.google.com/looker/docs/access-levels), [sharing URLs](https://docs.cloud.google.com/looker/docs/sharing-urls), [access filters](https://docs.cloud.google.com/looker/docs/reference/param-explore-access-filter), [scheduling and run as recipient](https://docs.cloud.google.com/looker/docs/scheduling-and-sending-dashboards), [users](https://docs.cloud.google.com/looker/docs/admin-panel-users-users).
- **Metabase** — [collection permissions](https://www.metabase.com/docs/latest/permissions/collections), [data permissions](https://www.metabase.com/docs/latest/permissions/data), [row and column security](https://www.metabase.com/docs/latest/permissions/row-and-column-security), [notification permissions](https://www.metabase.com/docs/latest/permissions/notifications), [alerts](https://www.metabase.com/docs/latest/questions/alerts).

---

## Part 2 — Recommendation for Tellma

The recommendation assumes the access model of
[spec 0014](../specs/0014-users-roles-and-permissions.md): users; roles with many-to-many
memberships; public roles whose permissions apply to every active user; permissions as
`(Resource, Action, Filter)` with the filter a Queryex predicate; bespoke criteria contributed by
`IAccessCriteriaProvider`; one `AccessDecision` per securable whose `FilterTree` the CRUD pipeline
conjoins into every read.

### 2.0 Decision summary

| # | Decision | Choice | Firmness |
|---|---|---|---|
| D1 | Data access | **Sharing shares the definition, never the data.** Every live run is the viewer's query; no run-as-owner, no unrestricted flag, no editor bypass | Firm |
| D2 | Scope | **One sharing model** for report definitions and dashboards | Firm |
| D3 | Ownership and grants | Owner + **grants to users or roles** at View or Edit + a public flag; pointer, never copy; "Save as" copies without grants | Firm |
| D4 | Who may share | Authoring, sharing with users, and publishing to roles are **three separate grants** | Leaning |
| D5 | Share-target check | **Warn, don't block**, when a grantee cannot read the report's root entity | Leaning |
| D6 | Administrators | See, share, re-own and delete any definition through ordinary grants; others' private ones hidden from lists until asked for | Firm |
| D7 | Where reports live | Reports hub (Mine / Shared with me / All / Favorites), in-context on entity lists, Launcher, pins, dashboards | Leaning |
| D8 | Dashboards | Same model; dashboard access **reads through** to the definitions of its widgets, never their data | Leaning |
| D9 | Lifecycle | Content outlives its author; bulk ownership transfer; delete blocked while in use | Firm |
| D10 | Standard definitions | Pack-shipped definitions are ownerless, read-only, copied to customise | Leaning |
| D11 | Scheduled delivery | **Rendered per recipient**, Tellma users only; "send my view" is a separate, guarded data grant | Firm |
| D12 | Interactive data grants | **None** in the first release; the need is met by permission design or a tier-2 report's own securable | Leaning |

### 2.1 Sharing shares the definition, never the data — D1

**Q7's answer is no, by construction.** A report definition is Queryex text plus presentation. Running
it compiles that text through the same read path as any CRUD query and conjoins the viewer's
`Read` decision for the report's root entity, so:

- **Rows are the viewer's.** Two people opening the same report see the rows each could see by
  querying the entity directly. This is C1, and it is what Tellma 1.0 already did.
- **Aggregates are computed over the viewer's rows.** The access filter is part of the `WHERE`, so
  every `GROUP BY` total, count and chart sums only visible rows — the ServiceNow failure ("they are
  included in visualizations of data") cannot occur. The report spec should pin this with a test:
  a summary's grand total equals the sum of the detail rows the same viewer sees.
- **Navigations follow spec 0014's bounds.** A report rooted at invoice lines that shows the
  customer's name reaches it through the customer's `[RelatedSelect]` projection, exactly as a
  details page does; a filtered or absent grant on the customer limits the path to that projection.
  A report never reveals more than direct queries by the same viewer would.
- **Drill-down agrees with the summary.** Because both are the viewer's queries, the Salesforce
  static-dashboard symptom (totals that disagree with the drill-down) cannot occur.
- **`me()` in a definition means the viewer.** "My open invoices" shared with a team shows each
  member their own invoices. Viewer-relative definitions are a feature to document, not a hazard.
- **No report-level escape hatch.** No "run as owner", no "run unrestricted", no stored running
  user, and — the Power BI lesson — **edit rights on a report never change whose security applies**.
  The Administrator sees everything only because its grant is unfiltered, not because reporting
  treats it specially.

**Why Firm.** It is the field's universal default, it is Tellma 1.0's behaviour, and every
exception in [§1.4](#14-the-data-granting-exceptions-and-how-each-is-guarded) is either a
privilege-gated feature its vendor wraps in warnings or a leak its vendor documents a workaround
for. The accidental grants all come from a second query path; Tellma has exactly one, so the rule
costs nothing to keep.

**One honest residue.** A definition can itself carry information — a filter literal naming an
employee, a title such as "Layoff candidates Q3". Sharing exposes the *definition* to the grantee.
That is the author's choice and needs no mechanism, but the share dialog should not hide it: the
recipient sees what the author wrote.

### 2.2 One model for reports and dashboards — D2

Q8's answer is **yes, with exactly the same mechanics as Q1 and Q2.** NetSuite (four sharing models
for searches, reports, workbooks and dashboards), Sage Intacct (separate controls for six report
kinds) and ServiceNow (two generations side by side) are all criticised for fragmentation; HubSpot,
Dataverse and Power BI use one dialog for both. Tellma has a chance to start with one: the same
ownership columns, the same grant table shape, the same actions, the same "Shared with me" surface.
Anything else that is later shared — saved list views, print templates — should adopt it rather
than inventing another.

### 2.3 Ownership and grants — D3

Sketch, in the specs' contract notation (names illustrative; the report-definitions spec settles
them). The dashboard entity takes the same three members and the same child shape.

```csharp
// Tellma.Core.Abstractions.Reports
[Table("ReportDefinitions", Schema = "core"), TableType, Temporal, ApiResource]
public class ReportDefinition : TopLevelEntity, IActivatable
{
    // ... title, root entity, Queryex measures/dimensions/filter, chart, parameters ...
    public int? OwnerId { get; set; }                          // FK -> core.Users; null = standard (pack-shipped) definition, D10
    public bool IsPublic { get; set; }                         // View for every active user, like Role.IsPublic
    [ServerOwned] public int? BasedOnId { get; set; }          // FK -> core.ReportDefinitions; lineage of a "Save as" copy
    [NotMapped] public IReadOnlyList<ReportDefinitionShare> Shares { get; set; }
}

[Table("ReportDefinitionShares", Schema = "core"), TableType, Temporal]
public class ReportDefinitionShare : ChildEntity
{
    [ParentKey] public int ReportDefinitionId { get; set; }
    public int? UserId { get; set; }                           // FK -> core.Users; exactly one of UserId, RoleId
    public int? RoleId { get; set; }                           // FK -> core.Roles; a public role means everyone
    public ShareAccess Access { get; set; }                    // View | Edit
}

public enum ShareAccess { View = 1, Edit = 2 }
```

- **Private by default.** A new definition has an owner, no shares and `IsPublic = false`, like a
  private folder, a personal view or a Personal Space workbook.
- **Grantees are users or roles.** Q1 and Q2 both need answering, and Tellma's role is the grouping
  construct users already understand. Granting to a public role reaches every user, as 1.0's loader
  already treated it; `IsPublic` exists so "everyone" is available without first creating a public
  role. Teams, departments and other groupings are roles in Tellma.
- **Two access levels, with ownership as the third.** View runs the report and copies it; Edit also
  changes the definition. Only the owner and holders of the administrative grant (D6) change shares,
  transfer ownership or delete. That is Odoo's Owner / Editor / Viewer and Workday's Can View / Can
  Edit. **Edit does not confer re-sharing**: Power BI's Reshare-on-by-default is the documented
  over-sharing path, and ServiceNow needed an extra "right to share" flag with non-escalation rules
  to tame it.
- **`IsPublic` is View only.** HubSpot offers "Everyone: view and edit"; Odoo 19's global favorites
  are writable and deletable by every internal user. Neither is worth copying.
- **Pointer, not copy** (C2). Editing a shared definition changes it for every grantee. "Save as"
  creates a private copy owned by the caller with `BasedOnId` set and **no shares copied** (Workday:
  a copy defaults to "don't share").
- **Reading is a bespoke criterion.** An `IAccessCriteriaProvider` for the report securable
  contributes, for `Read`, "`OwnerId = me()` or `IsPublic` or a share to me or to one of my roles",
  and for `Save`, "`OwnerId = me()` or an Edit share to me or to one of my roles". This is the shape
  spec 0014 already reserves for self-scoped data, so a user with no stored grant on reports still
  sees their own and those shared with them, and `access/check` explains why. Role permissions add
  to it — an Administrator's unfiltered grant, or an auditor role with `Read` filtered to a module.

### 2.4 Who may share — D4

Three abilities, three grants, because the field consistently separates them (C3):

| Ability | Grant | Typical holder |
|---|---|---|
| Author private reports, edit own | `Save` on the report securable, filter `OwnerId = me()` | A "Report author" role, or every user via a public role, at the tenant's choice |
| Share with individual users | `Share` action | Report authors |
| Share with roles, make public, place in the menu | `Publish` action | Administrators and module leads |

Sharing changes are entity actions (`share`, `publish`) rather than writes to the child collection
inside an ordinary save, so each maps to one securable and one audit entry; the spec decides the
exact shape. The split lets a tenant allow peer-to-peer sharing without letting anyone broadcast to
the whole company — the line Workday draws with separate "specific users" and "specific groups"
domains, and Salesforce with "Send to Groups and Roles".

**Why Leaning.** Some tenants will want authors to publish to their own role; a filtered `Publish`
grant can express that, but whether the filter language can say "only roles I belong to" is a
question for the spec.

### 2.5 Share-target check — D5

Workday's share picker lists only principals who can already read the report's data source, so an
author can never share into an empty report. Tellma should **warn rather than block**: when the
author adds a grantee, the share dialog asks the server which grantees' `Read` decision on the
report's root entity is `Denied`, and shows "Hana cannot read Invoices and will see an empty report";
for a role, it counts the members whose decision is `Denied`. This needs its own narrow check that
answers only allowed-or-denied: spec 0014's `access/check` for another user requires user- and
role-read grants an author typically lacks, and returns role-editor information an author should
not see.

**Why not block.** Role membership changes after the share; a `Filtered` decision may still yield
rows; and an empty report is a support question, not a breach (D1). Blocking would also make the
order of administrative steps matter (grant data first, then share), the coupling that makes
Workday's ownership transfer require a pre-check audit of the new owner's field access.

### 2.6 Administrators — D6

**Q3, Q4, Q5: yes, yes, yes.** All three fall out of the grant model with no special path:

- An unfiltered `Read` on the report securable sees every definition, private or not — Workday's
  "Manage: All Custom Reports" expressed as an ordinary permission. The Administrator role's `*/*`
  includes it.
- Unfiltered `Share` and `Publish` let an administrator share any definition with any user or role.
- A **`transfer` action** reassigns ownership, singly or in bulk by current owner — the Salesforce
  dashboards, HubSpot and Workday off-boarding step.
- **Lists stay uncluttered.** The reports hub shows an administrator the same "Mine / Shared with me
  / All" scopes as anyone else; others' private definitions appear only under an explicit "Include
  others' private reports" toggle (NetSuite's "Show All Private Searches", Intacct's "Include
  private") and an "Owned by inactive users" filter for clean-up (Salesforce's documented
  `allPrivate` use).
- **Running someone else's private report shows the administrator's data**, not the owner's (D1). To
  see what a user sees, support uses impersonation, which is a separate, consented and audited
  feature and not a reporting concern.

**Why Firm.** The products that hid private content from administrators (Salesforce UI, Dataverse)
had to add escape hatches later, and the risk of exposure is low: a definition carries no data.

### 2.7 Where reports live — D7

**Q6:** five surfaces, each with an evidence-backed job.

1. **The reports hub** — one page listing every definition the user can read, with scope tabs
   **Mine / Shared with me / All / Favorites**, filters for owner, root entity and module, and search.
   This answers "what has been shared with me?", which Dataverse, Workday, Looker and Metabase
   cannot.
2. **In context on the entity's list page** — a "Saved reports" menu on every stack's list showing
   the readable definitions rooted at that entity, the Dataverse view selector, Odoo favorites,
   Frappe's "Saved Reports" and NetSuite's "Available as List View". This is where ordinary users
   meet reports, and it needs no navigation decision from anyone.
3. **The Launcher** — published definitions with a menu placement (section, sort key, icon — 1.0's
   `ShowInMainMenu` and `MainMenuSection`, now a `Publish` property) appear in the Launcher's browse
   content for the roles they are published to; typed search finds every readable definition under
   a Reports bucket.
4. **Pins** — any report can be pinned; the shell owns the pin control, and a pin to a report the
   user can no longer read renders disabled with a remove affordance (the shell research's rule,
   and the answer to 1.0's `ft_ReportsUnauthorizedAccess` problem).
5. **Dashboards and home** — reports as widgets (D8).

A share to an individual user also posts an inbox notification ("Ahmad shared *Overdue receivables*
with you"), the way Tableau's Share and Power BI's share email do; role grants and publishing do not,
to avoid notifying hundreds of people.

**Why Leaning.** The surfaces are well supported; the exact tab names and whether the in-context
menu also lists reports that merely *join* the entity are UX details for the spec.

### 2.8 Dashboards — D8

Dashboards use D3 unchanged: owner, `IsPublic`, user and role shares at View or Edit. Q8's answer is
yes.

**Dashboard access reads through to its widgets' definitions.** A viewer of a dashboard may render
every report widget on it, and drill from a widget into the report (read-only), even if the report
definition itself was never shared with them. The read-through is authorised by the dashboard, not
added to the report's share list, so the report does not appear under "Shared with me". This is the
HubSpot and Power BI position, and it removes the failure Salesforce, Dataverse and Metabase each
document as their most common dashboard problem. It is safe in Tellma because of D1: the viewer still
sees only their own rows.

**A widget whose data the viewer cannot read degrades, never breaks.** When the viewer's `Read`
decision on a widget's root entity is `Denied`:

- **viewers** see the widget hidden and the grid reflows, so a dashboard built for a mixed audience
  stays clean (Workday Discovery Boards: "no indication that other content exists");
- **editors** see a "Not available to some viewers" placeholder, so they know what others miss.

A `Filtered` decision renders normally — the widget shows that viewer's rows.

**Why Leaning.** The read-through is well supported; the hide-versus-placeholder split is one
product's design and worth a usability check.

### 2.9 Lifecycle — D9

- **Content outlives its author.** Deactivating a user changes nothing for their shared definitions:
  there is no running user to go stale, so Salesforce's "The Running User is inactive" failure
  cannot occur. Their private definitions remain and are found through the "Owned by inactive
  users" filter (D6).
- **Ownership transfer** is an administrative action (D6), recommended in the user deactivation
  flow: "This user owns 14 reports shared with others — transfer them?"
- **Deleting a definition used by a dashboard is blocked** with a validation naming the dashboards,
  and deactivation is offered instead (Workday: "You cannot delete a custom report definition in use
  anywhere"). The foreign key from the widget already forces this.
- **Deleting a shared definition warns with its reach** ("shared with 3 users and 2 roles"), because
  — as Dataverse states — deleting a shared view "deletes it for everyone who has access to it".

### 2.10 Standard definitions — D10

Packs ship report definitions as seeds, found by `Code`, with `OwnerId = null`. They are read-only
in the tenant (SAP: delivered KPIs "cannot be edited directly"; Frappe: "Cannot edit a standard
report. Please duplicate"); a user customises by "Save as". Who sees them is decided by `Publish`
grants and menu placement, seeded with the pack's baseline roles. When an administrator copies a
published standard report to replace it, the copy dialog offers to carry over its role grants —
Oracle's documented gotcha is that a customised copy silently loses the original's permissions.

### 2.11 Scheduled delivery — D11

Not one of the eight questions, but it is where Q7 actually fails in the field (C6), so the rule
should be set before the feature is built on [spec 0020](../specs/0020-background-jobs-and-scheduler.md)
and [spec 0022](../specs/0022-email-outbox.md):

- **A subscription renders per recipient** under that recipient's own decision — Looker's "as if each
  one of them had run the query", Workday's securing-entity bursting, SAP Analytics Cloud bursting.
  Each recipient gets their own rows, exactly as if they had opened the report.
- **Recipients are Tellma users** — individuals or roles — of the same tenant. A role subscription
  resolves membership at each run (Tableau's snapshot-at-creation is a documented surprise).
- **"Send my view to others"** — one rendering under the sender's permissions, possibly to external
  addresses — is a data grant. If it is ever built, it is a separate securable held by
  administrators, with an explicit acknowledgement at setup (Workday's "I agree"), an audit entry
  per send, the sender's permissions re-evaluated at every run, and automatic suspension when the
  sender is deactivated (Looker disables a deactivated user's schedules; Metabase's keep running,
  which its own docs present as a risk).

### 2.12 Interactive data grants — D12

The need behind NetSuite's Run Unrestricted and Intacct's Primary Consumer is real: "the regional
directors may see company-wide revenue totals but not other regions' invoices". Tellma should not
answer it with a per-report flag in the first release:

- Often the permission model already answers it: a role permission on the revenue fact entity with
  a broader filter than the invoice permission.
- When the answer is genuinely "aggregates over rows you may not see", the right vehicle is a
  **tier-2 report** shipped by a pack — reviewed C# with its own securable in the registry —
  because then the grant is a deliberate, code-reviewed decision with one name, not a checkbox any
  author can tick on any definition.
- A later **published snapshot** (Odoo's "Freeze and share") — frozen output, stored with its
  publisher, re-checked against the publisher's access — is the cleanest user-facing form if demand
  appears, because it is honest about being data at a point in time.

**Why Leaning.** Whether tier-2 reports apply viewer row security or their own semantics is not yet
specified; this recommendation depends on that spec allowing the second.

### 2.13 Misalignments to resolve

These are flagged so one document can be brought in line with the other, not decided here.

1. **ARCHITECTURE.md** describes how tier-1 report definitions compile but says nothing about who
   owns, shares or may run them, and groups "dashboards" with BI analytics as out of scope for the
   OLTP contract. Tellma 1.0 and this document both treat dashboards over tier-1 definitions as part
   of the product; the home-page customization research made the same flag.
2. **The home-page customization research** (`docs/research/home-page-customization.md`, on the
   ERP-shell line of work, not yet merged) models dashboards as `OwnerId = null` for shared,
   `IsPublic`, and a `DashboardRoles` visibility table, with a "Publish" that clears the owner. This
   document needs **user** shares for Q8 and keeps the owner on shared content for accountability
   and transfer. Recommended: replace `DashboardRoles` with the D3 share table (users or roles, View
   or Edit), keep `OwnerId` on shared dashboards, and reserve `OwnerId = null` for pack-shipped
   standard dashboards (D10). Home assignments and forks are unaffected.
3. **Widget degradation.** The home-page research renders a "Not available to you" placeholder to
   every viewer; D8 hides it from viewers and shows the placeholder only to editors. One rule should
   win.

---

## Part 3 — Questions the brief did not ask

- **Folders.** Recommended none at first. NetSuite, Workday, ServiceNow, Frappe and HubSpot run
  without them; Salesforce's folder-only sharing is its most-cited friction. Modules, root entities,
  menu sections, favorites and dashboards organise reports well enough; tags are a cheap addition if
  hubs grow large.
- **Exports.** An export is a rendering of the viewer's own result; it carries what the viewer could
  already see and needs no new rule. Export size limits belong to the Excel codec.
- **Parameters.** Parameter values a viewer enters are filters on their own rows; parameter defaults
  in a definition are part of the definition and travel with sharing.
- **Audit.** Share, publish, transfer and delete are entity actions on temporal tables, so who
  granted what and when is answered by history without a separate log.
- **MCP.** The tenant MCP server exposes the user's own permissions; report definitions the user can
  read can be run there under exactly the same D1 rule, with no additional surface.

## Part 4 — Open questions

1. **Can an Edit grantee change shares?** Recommended no (D3). Some tenants may want co-owners;
   a "Manage" level would be the extension.
2. **Should publishing to a role move ownership to the system** (Salesforce public folders, the
   home-page research's Publish), or keep the author as owner (this document)? Keeping the owner aids
   accountability; moving it avoids a departing author's content needing transfer.
3. **Can `Publish` be scoped to "roles I am a member of"** in the permission filter language (D4)?
4. **Hide or placeholder for viewers** of an inaccessible widget (D8), pending a usability check.
5. **Aggregate-only access** (D12): is a tier-2 report enough, or is a governed per-definition grant
   needed in a later release?
6. **Share limits.** A cap on shares per definition (Salesforce: 25 in the UI) keeps the dialog and
   the bespoke criterion cheap; the number is a spec detail.
