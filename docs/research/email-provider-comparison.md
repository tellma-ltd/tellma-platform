# Transactional Email Provider Comparison — ACS Email vs Twilio SendGrid vs Postmark

**Purpose:** Decide which hosted transactional email provider the Tellma platform builds its next
connector adapter against — and whether the SendGrid adapter specified in
[spec 0007](../specs/0007-email-connectors.md) remains the hosted default — by evaluating Azure
Communication Services Email (ACS), Twilio SendGrid, and Postmark against the platform's email
contract and fleet economics.

**Status:** Research complete; recommendation in Part F. All load-bearing claims were verified
against primary sources on **11 August 2026** (source-page dates noted inline). Facts, estimates,
and judgment are kept separate: anything not verified is marked as such.

---

## Question & requirements

Tellma is a multi-tenant ERP platform: one platform repo plus N distribution web apps (tens today,
targeting hundreds), each distribution deployed to Azure App Service in its own resource group with
its own Key Vault and its own Bicep IaC, plus a shared identity server. The shop is Azure-first,
.NET 10, System.Text.Json-only, with all Azure infrastructure provisioned per distribution by
Bicep. Spec 0007 defines a provider-neutral email contract with swappable connector adapters; the
provider question is therefore about *fit and economics*, not lock-in of application code.

The contract facts a provider is judged against:

1. **Batch sending.** The send contract is batch-shaped: each message carries its own subject and
   body; a dispatch run can hand the adapter a few thousand messages; the adapter must return a
   per-message outcome (sent / transient failure / rejected).
2. **Delivery events.** Provider callbacks (delivered, deferred, bounce, dropped/suppressed,
   open, click, spam complaint) are consumed through a webhook-receiver abstraction. Receivers
   must be able to verify a callback's authenticity — a signature over the raw bytes or an
   equivalently trustworthy mechanism — and every event must be attributable to a message.
3. **Correlation echo.** At send time the platform attaches an opaque correlation string (e.g.
   `etpharma:outbox:42:1093`); delivery events must carry it back so each event finds the record
   it updates. Where a provider cannot echo it, the workaround's cost must be assessed honestly.
4. **Per-deployable isolation.** Each distribution and the identity server should have its own
   credentials, its own webhook endpoint, its own suppression state, and ideally its own sender
   reputation — without expensive plan gating.
5. **Sandbox/testing.** A per-request or per-credential way to validate without delivering, plus a
   workable local/dev/staging story.
6. **Webhook mechanics.** The platform fronts receivers at `POST /api/webhooks/{key}` and supports
   verification handshakes (challenge echo in the response body); each provider's callback
   delivery, endpoint verification, and retry behaviour must fit that shape.

## A. Maturity & track record

**ACS Email** reached general availability on
[5 April 2023](https://techcommunity.microsoft.com/blog/azurecommunicationservicesblog/simpler-faster-azure-communication-services-email-now-generally-available/3788541)
— by far the youngest of the three. It is a first-party Microsoft product (ownership risk is
effectively Azure platform risk) and positions itself for high-volume A2P sending, with SMTP
submission also supported
([overview](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/email-overview),
page updated March 2026). The youth shows at the edges: engagement events are limited to open and
click, there is no spam-complaint feedback event, custom per-domain suppression-list management is
still in preview (see B), and new resources start with deliberately tiny send quotas that are
lifted by support ticket only (see B/E). Azure Communication Services is covered by the
consolidated Microsoft Online Services SLA; the email-specific figure could not be verified on a
public page (the SLA ships as a downloadable licensing document) — *unconfirmed*. No public record
of major email-specific outages was found; that is an absence of evidence, not a clean bill.

**Twilio SendGrid** has operated since 2009 and has been a wholly owned Twilio subsidiary since
[February 2019](https://www.twilio.com/en-us/press/releases/twilio-completes-acquisition-sendgrid)
(≈$3B acquisition). No divestiture had been announced as of August 2026 (searched; none found).
Scale and API maturity are excellent, but the recent operator signals are mixed: the **free tier
was retired in 2025** (announced 27 May 2025, terminated 26 July 2025 —
[Twilio changelog](https://www.twilio.com/en-us/changelog/sendgrid-free-plan)); the official C#
SDK has been dormant since April 2024 (assessed in spec 0007, which is why the platform builds a
first-party client); and SendGrid's shared infrastructure has a documented abuse history —
compromised customer accounts used for large-scale phishing
([KrebsOnSecurity, August 2020](https://krebsonsecurity.com/2020/08/sendgrid-under-siege-from-hacked-accounts/)),
with abuse reports continuing into 2026. Contractually, the
[Twilio APIs SLA](https://www.twilio.com/en-us/legal/service-level-agreement/twilio-apis) commits
99.95% monthly API availability for Twilio Services APIs; for SendGrid the stated 99.99%
commitment attaches to premium email services packages (Email Deliverability, Expert Services,
etc.) — standard Email API plans carry no stated availability threshold of their own per that page
(reading as of August 2026).

**Postmark** launched in 2009–2010 (Wildbit) and has been owned by **ActiveCampaign since May
2022** ([announcement](https://www.businesswire.com/news/home/20220502005892/en/)). Its reputation
rests on a strict transactional-first posture (marketing/broadcast traffic is segregated into
separate message streams and IP pools) and unusual transparency: it publishes live
**Time-to-Inbox** measurements — probe emails to Gmail, Yahoo, Outlook, iCloud, and AOL every five
minutes — at [tti.postmarkapp.com](https://tti.postmarkapp.com/) alongside its status page. It
offers **no formal uptime SLA** (none found on public pages; treated as confirmed-absent for plan
tiers examined). Operator scale is much smaller than the other two. Ownership stability is decent
but it is a product line inside a private marketing-automation company — strategic drift (e.g. the
2025 price rise and the early-2026 move to plan-gated features, see C) is the visible risk.

## B. Feature fitness against the contract

At-a-glance matrix (details and sources follow):

| Contract fact | ACS Email | SendGrid | Postmark |
|---|---|---|---|
| Batch send | ✗ one message per request, long-running operation; default quota 30/min | ◐ one request per message, 10,000 req/s ceiling | ✓ native `/email/batch`, 500 messages/call |
| Per-message outcome | ✓ 202 + pollable operation status | ✓ per-request status code | ✓ per-message success/error, ordered |
| Delivery events | ◐ delivered/bounced/suppressed/quarantined/filtered-spam/failed + open/click; no deferred, no spam-complaint FBL | ✓ full set incl. deferred, dropped, spamreport | ✓ full set incl. spam complaint, subscription change |
| Correlation echo | ✗ none — events carry only `messageId` (= send operation id) | ✓ `custom_args` echoed (≤10,000 bytes) | ✓ `Metadata` echoed (≤10 fields, 20-char keys, 80-char values) |
| Webhook verification | ✓ Event Grid handshake + Entra ID auth option | ✓ ECDSA signature over raw bytes | ✗ no signature; basic auth + custom headers + IP allowlist |
| Per-deployable isolation | ✓ resource per distribution, own Bicep | ◐ subusers gated behind Pro (~$90/mo, 15 incl.) | ✓ server per deployable; unlimited servers on Platform plan ($18/mo) |
| Sandbox channel | ✗ none | ✓ `sandbox_mode` per request (no events) | ✓ sandbox servers (full events) + `POSTMARK_API_TEST` |
| Message size | 10 MB total (pre-Base64; ~7.5 MB effective) | 30 MB total | 10 MB per email; 50 MB batch payload |
| Recipients/message | 50 | 1,000 per personalization | 50 per To/Cc/Bcc field |
| Inline images (`cid:`) | ✓ `contentId` | ✓ `content_id` + inline disposition | ✓ `ContentID` |
| Data residency | ✓ many data locations incl. Europe, UAE | ◐ US default; EU residency for Pro+ since July 2025 | ✗ US only, no EU plans |

### ACS Email

**Send model.** The REST API
([Email – Send, api-version 2025-09-01](https://learn.microsoft.com/en-us/rest/api/communication/email/email/send?view=rest-communication-email-2025-09-01))
queues **one message per request** and returns `202 Accepted` with an `Operation-Location` header
and a pollable status (`NotStarted/Running/Succeeded/Failed/Canceled`). There is no batch
endpoint; the .NET SDK (`Azure.Communication.Email` 1.1.0) wraps this as a long-running operation.
Two properties matter for the adapter: the caller may supply its own **`Operation-Id`** (a UUID)
for the operation, and the message accepts **custom `headers`** (which become MIME headers on the
delivered mail — the request sample literally shows a `ClientCorrelationId` header). A batch of N
messages is therefore N HTTP requests plus optional N status polls, executed with bounded
concurrency — mechanically fine, except for quota (next).

**Default quotas are the headline constraint.** Per the
[service limits](https://learn.microsoft.com/en-us/azure/communication-services/concepts/service-limits#email)
(page updated 5 March 2026), a new subscription sending from a **custom domain** may send **30
emails/minute and 100 emails/hour — scoped per subscription**, with status polling similarly
capped (60/min, 200/hour). Azure-managed domains get 5/min, 10/hour and are **not raisable**
(testing only). Higher limits require a
[support-ticket questionnaire](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/email-quota-increase)
(business description, volumes, list hygiene; failure rate must stay below 1–2%; evaluation can
take up to 72 hours; approval is discretionary and reputation-gated). Once granted, the service
"supports high volume up to 1–2 million messages per hour". Consequence for the contract's
"few-thousand-message dispatch": at default quota a 3,000-message run takes ~30 hours; **the quota
ticket is a hard onboarding prerequisite, per subscription**, and because the documented scope is
the subscription, co-located distributions share one pool (see D and Open questions).

**Delivery events** are published through **Event Grid**
([event schema](https://learn.microsoft.com/en-us/azure/event-grid/communication-services-email-events),
page updated May 2026). `EmailDeliveryReportReceived` fires on terminal states with statuses
`Delivered`, `Suppressed`, `Bounced`, `Quarantined`, `FilteredSpam`, `Expanded`, `Failed`; data
carries `sender`, `recipient`, `messageId`, `status`, `deliveryStatusDetails.statusMessage`, a
timestamp, and (per the SDK event models) an `internetMessageId`. The engagement-tracking event
carries `engagementType` (`View`/`Click`), `engagementContext` (the clicked URL), `userAgent` —
and its recipient field is empty when the original mail had multiple recipients
([quickstart](https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/email/handle-email-events),
updated July 2026). Gaps against the contract's event taxonomy: **no deferred event** (only
terminal states) and **no recipient spam-complaint event** (`FilteredSpam`/`Quarantined` are
outbound filtering verdicts, not a feedback loop). Opens/clicks require enabling engagement
tracking on the domain resource.

**Correlation echo — the critical question — is a confirmed no.** The event data contains no
custom key/value fields; nothing attached at send time (including custom headers) is returned. A
[Microsoft Q&A answer (19 March 2025)](https://learn.microsoft.com/en-us/answers/questions/2236528/microsoft-communication-emaildeliveryreportreceive)
confirms custom parameters are unsupported and recommends the workaround this analysis assumes:
the `messageId` in every event **is the send operation id**, and the client may *supply* that id.
The Q&A's wording is loose ("your custom UUID … or any unique identifier"), which invites the
idea of passing the correlation string itself as the id; the REST contract forecloses it — the
[Email – Send reference](https://learn.microsoft.com/en-us/rest/api/communication/email/email/send?view=rest-communication-email-2025-09-01)
declares the `Operation-Id` header `string (uuid)` and the operation-id field's definition says
"Use a UUID" — and even under the looser reading the id could not carry the correlation verbatim,
because it must be unique per send operation while a correlation is not unique per attempt (a
resend of the same document reuses its correlation; reuse behaviour of an existing id is
undocumented). An ACS adapter therefore needs a **durable messageId→correlation map**: mint a
UUID per message, persist `(uuid, correlation)` *before* the send (crash-safe because the id is
client-chosen), send with `Operation-Id: uuid`, and have the webhook receiver resolve
correlations by lookup before dispatch. Architectural cost, honestly stated: a storage seam spec 0007 does not have — the
adapter cannot stay stateless as the SendGrid one does; every send performs a durable write, every
event a read; retention and cleanup become adapter concerns; and for outbox mail the map
duplicates what the outbox row could store, so the seam should let the outbox *be* the map for its
own mail. Tractable — one table, one small interface — but real platform work, not configuration.

**Webhook mechanics.** Event Grid pushes to a webhook after a
[subscription-validation handshake](https://learn.microsoft.com/en-us/azure/event-grid/receive-events):
the endpoint receives a `SubscriptionValidationEvent` containing a `validationCode` and must echo
`{"validationResponse": "<code>"}` in the response body (a manual `validationUrl` GET is the
fallback; CloudEvents-schema subscriptions instead use an HTTP OPTIONS abuse-protection
handshake). This fits the platform's `WebhookResult.ResponseBody` challenge-echo design as
specified. Ongoing delivery is at-least-once, unordered, with exponential backoff retries for up
to 24 hours and optional dead-lettering to a storage account
([delivery and retry](https://learn.microsoft.com/en-us/azure/event-grid/delivery-and-retry),
updated February 2026). Two sharp edges: **a webhook returning 401/403 is not retried** — Event
Grid drops or dead-letters immediately, so a key-rotation mistake on the receiver loses events
unless dead-lettering is configured (the platform's `Unauthorized→401` mapping is correct for
forgeries but unforgiving for misconfiguration); and authenticity of deliveries is not
signature-based — options are a secret embedded in the endpoint URL/query, up to 10 custom
delivery headers (can carry a static shared secret), or **Microsoft Entra ID authentication**
(Event Grid presents a bearer token the receiver validates), which is the option that meets the
platform's verification bar. Alternatively Event Grid can deliver to Service Bus / Storage Queues
/ Functions instead of a public webhook — stronger, but it bypasses the platform's uniform
webhook fronting and adds per-distribution infrastructure.

**Suppression.** A platform-run
[managed suppression list](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/sender-reputation-managed-suppression-list)
auto-suppresses hard-bounced addresses globally (escalating 24 h → 14 days; such sends surface as
`Suppressed`). Customer-managed per-domain suppression lists exist as ARM child resources but are
**in preview, explicitly not recommended for production**
([quickstart](https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/email/manage-suppression-list-management-sdks),
updated June 2026).

**Sandbox.** None. The 2025-09-01 send schema has no validate-only flag (the only per-request
toggle is `userEngagementTrackingDisabled`), and no emulator exists. The spec 0007 router's
channel-less path — suppress external sandbox mail with an explicit `Suppressed` outcome — is the
applicable design; dev/staging inspection falls back to the log sink and the SMTP/Mailpit path.

**Domains, size, misc.** 50 recipients/message and 10 MB total request (Base64 inflation makes
~7.5 MB effective; both raisable by support request to 30 MB attachments / >50 recipients). 100
domains linkable per Communication Services resource; 100 sender usernames per domain. SPF/DKIM
are provider-generated DNS records per domain; verification is automatable (see D).

### Twilio SendGrid

Already specified as the platform's first hosted connector (spec 0007); recapped here only for
comparison. One `v3/mail/send` request per contract message (each has its own subject/body) under
a documented **10,000 requests/second** endpoint ceiling and **30 MB** total message size
([Mail Send API](https://www.twilio.com/docs/sendgrid/api-reference/mail-send),
[v3 FAQ](https://www.twilio.com/docs/sendgrid/for-developers/sending-email/v3-mail-send-faq));
`custom_args` (≤10,000 bytes) are echoed verbatim on every event — the cleanest correlation story
of the three. The Event Webhook signs each POST with an **ECDSA signature over the raw bytes**
(or OAuth), batches events, and retries failed deliveries **for a rolling 24 hours**
([Event Webhook](https://www.twilio.com/docs/sendgrid/for-developers/tracking-events/getting-started-event-webhook)).
Event taxonomy is the platform's reference model (delivered, deferred, bounce, dropped, open,
click, spamreport, unsubscribe family). `mail_settings.sandbox_mode` validates a request without
delivering, consuming credits, or emitting events. Isolation is the weak point: **subusers —
each with own credentials, suppression lists, webhook, and reputation — require the Pro plan; 15
are included, more by support ticket**
([subusers doc](https://www.twilio.com/docs/sendgrid/ui/account-and-settings/subusers)). Domain
authentication and subuser provisioning are fully API-automatable
([domain authentication API](https://www.twilio.com/docs/sendgrid/api-reference/domain-authentication),
[automating subusers](https://docs.sendgrid.com/for-developers/sending-email/automating-subusers)).
EU data residency (EU-designated subusers, `api.eu.sendgrid.com`) became generally available
**15 July 2025**, Pro and Premier only
([announcement](https://www.twilio.com/en-us/blog/products/data-residency-for-email-eu)); no MENA
region exists.

### Postmark

**Send model.** The [Email API](https://postmarkapp.com/developer/api/email-api) has a native
batch endpoint: **up to 500 messages per call, 50 MB payload**, each message with its own
subject/body/metadata, returning per-message success/error codes **in input order** — a direct
match for the spec 0007 batch contract (one call per 500-message chunk; the response maps
positionally). 10 MB per email; 50 recipients per To/Cc/Bcc field; `ContentID` attachments for
inline images; `TrackOpens`/`TrackLinks` per message. No published rate limits — the
[API overview](https://postmarkapp.com/developer/api/overview) documents only a 429 "acceptable
use" response (a soft, unquantified ceiling — noted as a risk).

**Correlation echo: yes.** Custom `Metadata` key/value pairs attached at send time are echoed on
webhooks — verified against the
[delivery-webhook payload](https://postmarkapp.com/developer/webhooks/delivery-webhook), which
carries a `Metadata` object plus `MessageID`, `Recipient`, `Tag`, `ServerID`, `MessageStream`.
Limits per the [custom metadata FAQ](https://postmarkapp.com/support/article/1125-custom-metadata-faq):
**10 fields per message, keys ≤20 characters, values ≤80 characters**. The platform's canonical
correlation fits in one 80-character value for realistic key lengths, but the cap is two orders
of magnitude tighter than SendGrid's and would need a stated length budget.

**Delivery events.** Webhooks exist for delivery, bounce (hard/soft classified), **spam
complaint**, open, click, and subscription change
([webhooks overview](https://postmarkapp.com/developer/webhooks/webhooks-overview)) — the fullest
taxonomy here, including the FBL events ACS lacks. Two material weaknesses. First,
**authentication**: the docs state plainly that "Postmark does not currently support HMAC webhook
signature verification"; the offered protections are HTTPS, **basic-auth credentials in the URL**,
custom HTTP headers, and an IP allowlist — all static shared secrets, below the platform's
signature-over-raw-bytes bar. An adapter would mitigate with a long random receiver key, a
basic-auth secret validated from the `Authorization` header, source-IP checking, and handler-side
dedupe — adequate for low-sensitivity delivery telemetry, but the weakest of the three. Second,
**retry windows**: bounce webhooks retry ~10 times over ~10 hours, but delivery/open/click/
subscription-change webhooks retry only three times (1, 5, 15 minutes) — a receiver outage longer
than ~21 minutes silently loses those events; a 403 stops retries entirely.

**Isolation & sandbox.** Postmark's isolation unit is the **server** (own API tokens, streams,
suppressions, statistics, webhooks); webhooks are provisioned per server + message stream via API
([webhooks API](https://postmarkapp.com/developer/api/webhooks-api)); servers themselves are
API-creatable with the account token, including **`DeliveryType: Sandbox`** — sandbox servers
black-hole all mail while the API, activity UI, **and webhooks behave fully**
([sandbox mode](https://postmarkapp.com/developer/user-guide/sandbox-mode)); sandboxed sends do
count toward billed volume. This is the best staging story of the three: spec 0007's sandbox
channel maps to a per-deployable sandbox server, and unlike SendGrid's sandbox mode it exercises
the delivery-event pipeline end to end. `POSTMARK_API_TEST` additionally validates payloads
without any server. Suppressions are per stream; domains (DKIM/Return-Path DNS records) are
account-level resources managed via the Domains API and **plan-gated in number** (see C).

**Residency.** US only — primary infrastructure at a Chicago-area data centre plus AWS, with **no
stated plans for EU servers** ([GDPR FAQ](https://postmarkapp.com/support/article/1218-gdpr-faq),
[EU privacy page](https://postmarkapp.com/eu-privacy)). For MENA-sensitive tenants this is the
hardest constraint of the three.

## C. Pricing at fleet scale

Verified unit prices (11 August 2026):

- **ACS**: **$0.00025 per email + $0.00012 per MB** of data transferred
  ([email pricing](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email-pricing),
  page updated March 2026 and labelled illustrative; the live pricing page is script-rendered).
  No fixed fees, no plan tiers. Event Grid adds
  [$0.60 per million operations after a free 100k/month](https://azure.microsoft.com/en-us/pricing/details/event-grid/).
- **SendGrid**: free tier retired 2025; **Essentials** 50k/$19.95, 100k/$34.95; **Pro from
  $89.95/month at 100k emails** (bands up to 2.5M), Premier custom. Official pricing pages are
  script-rendered; tier figures are from third-party price trackers checked August 2026
  ([costbench](https://costbench.com/software/email-api/sendgrid/),
  [sendx](https://www.sendx.io/blog/sendgrid-pricing)) and are consistent with the pre-2025
  published tiers. **Subusers require Pro** (15 included) — isolation forces the ~$90/month floor
  per 15 deployables regardless of volume.
- **Postmark** (live [pricing page](https://postmarkapp.com/pricing), fetched 11 August 2026):
  plan-based since early 2026 — **Basic $15/mo** (10k included, $1.80/1k overage, ≤5 servers, ≤5
  domains), **Pro $16.50/mo** (10k, $1.30/1k, ≤10 servers/domains), **Platform $18/mo** (10k,
  $1.20/1k, **unlimited servers and domains**); free developer tier 100 emails/month; dedicated IP
  $50/month (recommended ≥300k/month); custom pricing by sales at high volume. Overage rates are
  used below as the list-price ceiling.

Scenario models (per-deployable isolation required in each; "deployables" = distributions + the
identity server; ~0.1 MB average message assumed for ACS data transfer):

| Scenario | ACS | SendGrid | Postmark (list) |
|---|---|---|---|
| (i) today: 5 deployables × ~2k/mo ≈ 10k/mo | 10k × $0.00025 = $2.50 + ~$0.12 data ≈ **$3/mo** | 1 Pro account (5 subusers ≤ 15) = **$89.95/mo** | Pro plan (10 servers, headroom): **$16.50/mo** (Basic $15 fits exactly 5 servers) |
| (ii) 50 deployables × 5k/mo = 250k/mo | $62.50 + ~$3 data + ~$0.25 Event Grid ≈ **$66/mo** | 4 Pro accounts (≤15 subusers each, ~62.5k ≤ 100k each) ≈ **$360/mo** | Platform: $18 + 240k × $1.20/1k = **$306/mo** |
| (iii) 300 deployables × 5k/mo = 1.5M/mo | $375 + ~$18 data + ~$2 Event Grid ≈ **$395/mo** | 20 Pro accounts ≈ **$1,800/mo** (or Premier, negotiated) | Platform: $18 + 1.49M × $1.20/1k ≈ **$1,806/mo** (custom pricing likely lower) |

Reading the table: **isolation is what distorts SendGrid's cost** — the subuser plan gate turns a
$3 workload into $90/month today and forces account sharding (4, then 20 Pro accounts, each a
separate dashboard/billing entity) at fleet scale. Postmark's Platform plan makes isolation nearly
free (unlimited servers on an $18 base) but volume is billed at roughly 5× ACS list rates.
**ACS is an order of magnitude cheaper at every scale and has no isolation gating at all** — its
costs are instead paid in engineering (correlation map) and operations (quota tickets).

## D. Operational fit for Tellma

**Provisioning.** ACS is the only provider whose per-deployable footprint is **native Bicep** in
the distribution's own resource group: `Microsoft.Communication/emailServices` (+ `domains`,
`senderUsernames`), a `communicationServices` resource linking the domain, an Event Grid system
topic + webhook subscription — all first-class ARM types
([Bicep reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.communication/emailservices/domains),
latest API 2026-03-18). Custom-domain verification (Domain/SPF/DKIM TXT + CNAME records) is
API-driven via
[`initiateVerification`](https://learn.microsoft.com/en-us/rest/api/communication/resourcemanager/domains/initiate-verification)
— fully automatable when DNS is in Azure DNS (deployment script or pipeline step), leaving quota
tickets as the only manual onboarding act. SendGrid and Postmark are provisioned by REST at deploy
time instead (subusers + domain authentication + per-subuser event webhook; servers + streams +
webhooks + domains respectively) — scriptable, but it is out-of-band imperative automation with
its own drift and secret-bootstrap concerns, not declarative IaC. Standard ARM quotas apply to the
ACS model (800 instances per resource type per resource group by default; no ACS-specific
per-subscription resource cap documented).

**Secrets & auth.** ACS supports Microsoft Entra ID (managed identity) on the data plane — no
long-lived API key needs to exist at all, and Key Vault holds nothing email-specific; access-key
auth remains available where needed. SendGrid and Postmark are bearer-token products whose keys
live in each distribution's Key Vault and need rotation runbooks (Postmark's webhook basic-auth
secret likewise).

**Monitoring.** ACS emits resource-level logs/metrics into Azure Monitor per distribution
(Email Insights dashboards; the same Log Analytics rollup the fleet already uses), complementing
the platform's own telemetry. SendGrid/Postmark expose dashboards and stats APIs outside Azure;
fleet-level rollup stays entirely on platform telemetry.

**Leaving.** All three sit behind the spec 0007 contract, so exit = next adapter + DNS cutover +
reputation re-warming; none holds data the platform cannot reconstruct (SendGrid/Postmark
suppression lists export via API; ACS's managed list is not user-visible, its custom lists are ARM
resources). The sticky parts — ACS quota grants, SendGrid subuser topology, Postmark server
topology — are deploy-time artefacts, none load-bearing at runtime beyond configuration.

## E. Risks & unknowns

**ACS.** Quota grants are discretionary, reputation-gated (failure rate below 1–2%),
support-ticket-paced (up to 72 h), and **documented per subscription** — the noisy-neighbour and
onboarding-latency implications for a many-distributions-per-subscription fleet are the biggest
operational risk, and whether a grant can be scoped per resource is unconfirmed. No deferred event
and no spam-complaint feedback event, so deliverability early-warning is weaker
(bounce/suppression events plus Insights logs only). Custom suppression lists are in preview. The
correlation map is new platform machinery — a bug there orphans delivery events. Email-specific
SLA figure unverified; ~3 years of GA track record; engagement events omit the recipient on
multi-recipient sends.

**SendGrid.** Isolation cost scales stepwise (Pro accounts × 15 subusers; more is a negotiation,
not an API call). Free-tier retirement (2025) and SDK dormancy signal reduced product investment;
shared-IP abuse episodes recur (Krebs 2020, further reports into 2026); and on standard plans no
availability SLA is stated for the Email API itself (Twilio APIs SLA page, August 2026 reading).

**Postmark.** No webhook signatures — below the platform's verification bar, static-secret
mitigations only. Delivery/open/click webhooks retry for only ~21 minutes (bounces ~10 h), so a
receiver outage silently loses those events. No formal SLA; unquantified API rate limits; US-only
data residency; 80-character metadata values; 10 MB messages. ActiveCampaign-era packaging churn
(2025 price rise, 2026 plan-gating) is the strategic signal to watch.

## F. Recommendation

**Build the ACS Email adapter as the next connector, and keep the SendGrid adapter** (already
specified in spec 0007) as the shipping hosted default until the ACS adapter and its quota posture
are proven in staging plus one pilot distribution — then make ACS the default for Azure-deployed
distributions. Deciding factors, ranked:

1. **Fleet economics with isolation.** ACS is the only provider where per-deployable isolation is
   the *native* model rather than a plan feature: ~$3/month today and ~$395/month at 300
   deployables, versus ~$90 → ~$1,800 (SendGrid) and ~$19 → ~$1,800 (Postmark, list). At target
   fleet scale the gap is roughly 4.5×.
2. **Architectural fit.** Provisioned per distribution by that distribution's own Bicep, secrets
   optionally eliminated via managed identity, monitoring in the fleet's existing Azure Monitor
   estate, and data-location choices (including Europe and UAE) that match MENA deployment needs
   no other candidate offers.
3. **The correlation gap is real but bounded.** The client-supplied `Operation-Id` makes a
   durable messageId→correlation map crash-safe and deterministic; it is the one genuine contract
   impedance, and it costs a storage seam plus adapter statefulness (spec 0007 impact below).
4. **Provider diversity.** Keeping the SendGrid adapter preserves a tested exit in both
   directions — SendGrid remains correct for non-Azure/hybrid deployments needing a hosted
   provider with per-request sandbox validation and signed webhooks out of the box, and the SMTP
   adapter already covers on-prem.

**What would change the decision.** (a) If ACS quota grants prove per-subscription-only *and*
sticky (slow re-approval, low shared ceilings), the fleet either shards subscriptions or the
economics erode into operational drag — Postmark then becomes the best-fit fallback for the long
tail of small distributions (batch API, metadata echo, sandbox servers, ~$18-base isolation),
accepting its webhook-authentication mitigations; (b) if the outbox later needs deferred-event
granularity or spam-complaint FBL data as first-class signals, ACS cannot supply them and
SendGrid/Postmark would carry the deployables that need them; (c) if Microsoft ships correlation
echo or batch send, the remaining reservations mostly dissolve.

**Impact on spec 0007** (to fold in when the ACS adapter is specified): the contract's assumption
that correlation is "round-tripped through the transport" does not hold for ACS — the adapter
needs a durable correlation store written before send (designed so outbox mail can use the outbox
row itself); the event-type map must absorb ACS statuses (`Suppressed`→`Dropped`,
`Quarantined`/`FilteredSpam`→`Other`; no `Deferred`, no `SpamReported`); the webhook fronting's
challenge-echo already fits Event Grid's validation handshake, but Event Grid **dead-lettering**
must be configured because a 401/403 from the receiver is never retried; and ACS registers no
sandbox channel (router suppression applies), while a future Postmark adapter would register a
sandbox-server channel.

## Open questions

1. **Quota scope after an increase**: is a granted ACS email quota bound to the subscription, the
   Communication Services resource, or the domain? The limits doc scopes defaults per
   subscription; the request form asks for subscription + resource. Needs a support-ticket answer
   before the fleet topology (subscriptions per N distributions) is fixed.
2. **ACS email SLA figure**: verify against the current consolidated Microsoft Online Services
   SLA document (downloadable licensing doc; not confirmed from public web pages).
3. **ACS throughput ceiling per resource** after a grant (the 1–2M/hour statement is
   service-level; per-resource/per-domain behaviour unconfirmed).
4. **Postmark 429 thresholds** (undocumented) and whether `POSTMARK_API_TEST` sends emit webhook
   events (sandbox servers verified to; the test token unverified).
5. **SendGrid >15 subusers**: price and turnaround of the support-ticket path at, say, 50 subusers
   on one Pro account — determines whether account sharding is actually required at scenario (ii).
6. **ACS Event Grid → Service Bus** as an alternative receiver path for distributions with strict
   ingress rules — cost/complexity not modelled here.

## Sources

Azure Communication Services (Microsoft Learn unless noted; last-updated dates as published):
- [Email overview](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/email-overview) (Mar 2026); [service limits — email](https://learn.microsoft.com/en-us/azure/communication-services/concepts/service-limits#email) (Mar 2026); [quota increase](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/email-quota-increase) (Mar 2026)
- [Email – Send REST API 2025-09-01](https://learn.microsoft.com/en-us/rest/api/communication/email/email/send?view=rest-communication-email-2025-09-01); [Azure.Communication.Email .NET readme](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/communication.email-readme) (Oct 2025)
- [Email events schema](https://learn.microsoft.com/en-us/azure/event-grid/communication-services-email-events) (May 2026); [handle email events quickstart](https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/email/handle-email-events) (Jul 2026)
- Event Grid: [receive events / endpoint validation](https://learn.microsoft.com/en-us/azure/event-grid/receive-events) (Mar 2026); [delivery and retry](https://learn.microsoft.com/en-us/azure/event-grid/delivery-and-retry) (Feb 2026); [pricing](https://azure.microsoft.com/en-us/pricing/details/event-grid/)
- [Managed suppression list](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/sender-reputation-managed-suppression-list) (Feb 2026); [domain suppression lists (preview)](https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/email/manage-suppression-list-management-sdks) (Jun 2026)
- [emailServices/domains Bicep reference](https://learn.microsoft.com/en-us/azure/templates/microsoft.communication/emailservices/domains); [Domains – initiateVerification](https://learn.microsoft.com/en-us/rest/api/communication/resourcemanager/domains/initiate-verification); [ARM 800-count limit exemptions](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resources-without-resource-group-limit)
- [Email pricing](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email-pricing) (Mar 2026); [Q&A: custom params on delivery events](https://learn.microsoft.com/en-us/answers/questions/2236528/microsoft-communication-emaildeliveryreportreceive) (Mar 2025); [GA announcement](https://techcommunity.microsoft.com/blog/azurecommunicationservicesblog/simpler-faster-azure-communication-services-email-now-generally-available/3788541) (Apr 2023)
- [EU Data Boundary for ACS](https://learn.microsoft.com/en-us/azure/communication-services/concepts/european-union-data-boundary); [azurerm email service data locations](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/email_communication_service)

Twilio SendGrid:
- [Subusers](https://www.twilio.com/docs/sendgrid/ui/account-and-settings/subusers); [account structure blog](https://www.twilio.com/en-us/blog/insights/demystifying-twilio-sendgrid-account-structure); [automating subusers](https://docs.sendgrid.com/for-developers/sending-email/automating-subusers)
- [Mail Send API](https://www.twilio.com/docs/sendgrid/api-reference/mail-send); [v3 Mail Send FAQ](https://www.twilio.com/docs/sendgrid/for-developers/sending-email/v3-mail-send-faq); [rate limits](https://www.twilio.com/docs/sendgrid/api-reference/how-to-use-the-sendgrid-v3-api/rate-limits); [domain authentication API](https://www.twilio.com/docs/sendgrid/api-reference/domain-authentication)
- [Event Webhook](https://www.twilio.com/docs/sendgrid/for-developers/tracking-events/getting-started-event-webhook); [Twilio APIs SLA](https://www.twilio.com/en-us/legal/service-level-agreement/twilio-apis)
- [Free plan retirement changelog](https://www.twilio.com/en-us/changelog/sendgrid-free-plan) (May 2025); [EU data residency GA](https://www.twilio.com/en-us/blog/products/data-residency-for-email-eu) (Jul 2025); [docs](https://www.twilio.com/docs/sendgrid/data-residency)
- [Twilio completes SendGrid acquisition](https://www.twilio.com/en-us/press/releases/twilio-completes-acquisition-sendgrid) (Feb 2019); [KrebsOnSecurity: SendGrid under siege](https://krebsonsecurity.com/2020/08/sendgrid-under-siege-from-hacked-accounts/) (Aug 2020)
- Pricing tiers via trackers (Aug 2026): [costbench](https://costbench.com/software/email-api/sendgrid/), [sendx](https://www.sendx.io/blog/sendgrid-pricing)

Postmark:
- [Pricing](https://postmarkapp.com/pricing) (fetched 11 Aug 2026); [ActiveCampaign acquisition](https://www.businesswire.com/news/home/20220502005892/en/) (May 2022)
- [Email API (batch)](https://postmarkapp.com/developer/api/email-api); [API overview](https://postmarkapp.com/developer/api/overview); [custom metadata FAQ](https://postmarkapp.com/support/article/1125-custom-metadata-faq)
- [Webhooks overview](https://postmarkapp.com/developer/webhooks/webhooks-overview); [delivery webhook payload](https://postmarkapp.com/developer/webhooks/delivery-webhook); [Webhooks API](https://postmarkapp.com/developer/api/webhooks-api)
- [Sandbox mode](https://postmarkapp.com/developer/user-guide/sandbox-mode); [Servers API](https://postmarkapp.com/developer/api/servers-api)
- [GDPR FAQ / data location](https://postmarkapp.com/support/article/1218-gdpr-faq); [EU privacy](https://postmarkapp.com/eu-privacy); [Time to Inbox](https://tti.postmarkapp.com/); [status page](https://status.postmarkapp.com/)
