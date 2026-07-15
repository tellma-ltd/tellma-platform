# Identity Server: Duende IdentityServer vs OpenIddict

> **Status:** Research / recommendation — input to the Identity Server spec.
> **Date:** 2026-06-29
> **Decision owner:** Platform architecture.
> **Recommendation (TL;DR):** Adopt **OpenIddict**. Duende's June‑2026 per‑deployment, subscription‑only licensing — and especially its redistribution addendum's new exclusion of consulting / systems‑integration / managed‑services businesses — is structurally misaligned with two load‑bearing facts of the Tellma architecture: a fleet of **100s of distributions** each acting as an OIDC client, and the **in‑proc identity mode** used for **on‑premises / standalone delivery**. OpenIddict's Apache‑2.0 grant matches the platform's own licensing posture, costs nothing per deployment, and is irrevocable. The price is engineering effort and single‑maintainer governance risk — both manageable and on‑brand for an AI‑native, bespoke‑first platform.

All facts below were pulled from primary sources in **June 2026** and are cited inline. Where a figure is volatile (pricing especially), the source URL is given so it can be re‑checked before any contract is signed. This document is a recommendation, not a final authority; like [ARCHITECTURE.md](../../ARCHITECTURE.md) it should be revised in light of better evidence.

---

## 1. The decision in context

Per [ARCHITECTURE.md → Identity](../../ARCHITECTURE.md), Tellma needs a **single, globally shared OIDC authority** that all distributions (and a future landing page) federate against for SSO, plus an **opt‑in in‑proc mode** where a distribution hosts the authority inside its own ASP.NET host. The in‑proc mode serves two cases the document calls out explicitly:

1. **Local development** — a fresh clone authenticates with no dependency on shared platform services.
2. **Standalone hosting** — a distribution deployed in isolation from the shared estate, *"e.g. on‑premises delivery."*

Two architectural facts dominate the licensing math and therefore the whole decision:

- **Fan‑out to 100s of distributions.** The wildcard‑trust design makes each distribution either its own confidential client (BFF, `client_id = <slug>`) or a participant in one shared public client. The default — and architecturally preferred — shape is **one confidential client + one BFF front‑end per distribution**.
- **On‑prem is a first‑class product capability.** The [Licensing & IP](../../ARCHITECTURE.md) section states a distribution's IP *"may be sold or delivered to its customer, on‑premises included."* When such a distribution runs in‑proc identity, it is **embedding an identity server into software delivered to a third party** — the textbook trigger for redistribution/OEM licensing.

The user's stated evaluation criteria, used as the spine of this report:

| # | Criterion | Why it matters here |
|---|---|---|
| 1 | **Security** of the product, and resistance to insecure extension | It is the authority for the entire estate; a weak or footgun‑prone design is systemic risk. |
| 2 | **Performance / scale** to 100,000s of users, globally shared | One authority fronts every SaaS distribution. |
| 3 | **Licensing cost** — SaaS and on‑prem isolated deployments | Tellma is a startup that may grow; on‑prem multiplies any per‑deployment fee. |
| 4 | **Modern auth feature coverage** — OIDC flows (SPA, S2M, machine accounts), SAML, SSO, 2FA (email/SMS/TOTP), social, passkeys, custom branding | The product must not be capped by the IdP. |
| 5 | **Embeddability** for local + standalone hosting | The in‑proc mode is non‑negotiable for dev and on‑prem. |
| 6 | **Build‑on‑ability** — docs, support, ecosystem, longevity | A 10‑year platform dependency. |

Two criteria the brief did not list but that materially change the answer, added here:

| 7 | **Licensing stability / legal risk** | A core dependency whose terms shift under you — adding carve‑outs that may exclude your business model — is a strategic hazard for a platform meant to host 100s of distributions for years. |
| 8 | **Reversibility / lock‑in** | Distributions are plain OIDC relying parties, so the authority is swappable. This lowers the stakes of the choice and should be stated explicitly. |

---

## 2. The candidates at a glance

| | **Duende IdentityServer** | **OpenIddict** |
|---|---|---|
| What it is | Commercial successor to IdentityServer4, by its original authors (Dominick Baier, Brock Allen). The most feature‑complete, turnkey .NET OIDC product. | Apache‑2.0 OAuth2/OIDC **server + client + validation** framework by Kévin Chalet (Microsoft MVP). Lower‑level, "bring‑your‑own‑everything." |
| License | **Commercial, subscription.** Free **Community Edition** under $1M revenue / $3M capital (own‑use only). Paid tiers **$5,750–$24,900/yr** + Custom. ([pricing](https://duendesoftware.com/pricing)) | **Apache‑2.0**, free forever, no revenue gate, freely embeddable/redistributable. Support gated behind GitHub sponsorship. ([repo](https://github.com/openiddict/openiddict-core)) |
| Latest (Jun 2026) | **8.0.2** (Jun 16 2026), targets **.NET 10**; v7.4 is the .NET 10 **LTS** line. ([NuGet](https://www.nuget.org/packages/Duende.IdentityServer)) | **7.5.0** (Apr 22 2026); 8.0.0‑preview.1 (Jun 2 2026). Targets .NET 8/9/10 (+ .NET Standard 2.0). ([NuGet](https://www.nuget.org/packages/OpenIddict)) |
| Turnkey? | **High** — config‑driven; ships BFF, server‑side sessions, dynamic providers, optional User Management SDK, SAML add‑on. | **Low** — you write the authorization/token endpoints, UI, and admin yourself; ships 100+ social providers. |
| SAML / WS‑Fed | **SAML 2.0 IdP/SP** add‑on ($1,500/yr; included in Advanced/Custom/Community). ([pricing](https://duendesoftware.com/pricing)) | Not native. Free **ITfoxtec.Identity.Saml2** (BSD‑3) or commercial **Rock Solid Knowledge SAML2P for OpenIddict** (~£4,269 perpetual). ([openiddictcomponents.com](https://www.openiddictcomponents.com/products/saml2p)) |
| Governance | Founder‑led company; commercial support + SLAs. | Effectively **single maintainer**; support tied to sponsorship; Apache‑2.0 means it can be forked. |

---

## 3. Critical framing: the IdP is only half the stack

Both products are **protocol engines**. Neither authenticates users, stores passwords, manages MFA, or renders a login page. That layer is **ASP.NET Core Identity** (or a custom user store) **plus your own UI**. This is confirmed by both vendors' own docs and Microsoft Learn.

The practical consequence is large: **passkeys, TOTP/email/SMS 2FA, social login, password reset, and per‑tenant branding are delivered by ASP.NET Core Identity + your UI — essentially identically under either framework.** They should not drive the choice.

- **Passkeys / WebAuthn** are native to **ASP.NET Core Identity in .NET 10** (GA Nov 2025) — not to either OIDC framework. Both Duende and OpenIddict simply sit in front of an Identity app that has passkeys lit up. Note .NET 10's implementation is scoped (no attestation‑statement validation by default, passkey‑as‑primary‑factor, Blazor‑template‑only scaffolding); enterprise attestation still calls for [fido2-net-lib](https://github.com/passwordless-lib/fido2-net-lib) or a commercial component. ([MS Learn — passkeys, .NET 10](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0))
- **TOTP authenticator apps** are built into Identity out of the box; **SMS/email codes** require you to wire a gateway and the code‑entry UI yourself (Microsoft recommends *against* SMS). Same under both. ([MS Learn — MFA](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/mfa?view=aspnetcore-10.0))
- **Custom login / white‑label per tenant** — you own the UI in both cases. Duende has more documented per‑tenant hooks (`GetAuthorizationContextAsync`, dynamic providers); OpenIddict gives the same control with more DIY. ([Damien Bod — Duende multi‑tenant](https://damienbod.com/2025/02/03/multiple-client-sign-in-customizations-using-duende-identity-provider/))

So the genuine differentiators reduce to: **licensing/cost, embeddability, protocol depth, SAML strategy, social‑provider breadth, and governance.** The rest is a wash.

---

## 4. Dimension‑by‑dimension analysis

### 4.1 Licensing & cost — the decisive dimension

This is where the two products diverge most, and where Tellma's specific architecture turns a "Duende is pricey" inconvenience into a structural misfit.

#### 4.1.1 Duende's June‑2026 model

Duende **restructured its licensing on June 2, 2026** (v8.x) into modular tiers + add‑ons. Current annual prices ([pricing](https://duendesoftware.com/pricing), [v8 announcement](https://duendesoftware.com/blog/20260602-your-identity-your-terms-duendes-modular-identity-infrastructure-v8-release)):

| Tier | Price/yr | Prod. deployments | Client IDs | BFF front‑ends | User‑Mgmt users | Support |
|---|---|---|---|---|---|---|
| **Lite** | $5,750 | 1 | 2 | 2 | 10,000 | Community |
| **Standard** | $12,500 | 1 | 10 | 10 | 100,000 | 2 escalations/yr |
| **Advanced** | $24,900 | 2 | 30 | 30 | 500,000 | 2‑biz‑day SLA |
| **Custom** | Contact sales | Unlimited | Unlimited | Unlimited | Unlimited | 1‑biz‑day SLA |

Add‑ons: **SAML $1,500/yr**, Automatic Key Management $4,000/yr, Financial‑Grade Security/Conformance $7,500/yr, User Management / Multi‑Issuer / **Redistribution Rights** = scale‑based. BFF is now **bundled** in every paid tier (front‑end counts above). Dev/test/QA is free.

**Community Edition** is feature‑equivalent to Advanced and free for orgs with **< $1M projected annual gross revenue *and* < $3M access to capital** — but it is **self‑hosted, own‑use only**: *"Redistribution and customer‑facing deployments require different licensing"* and *"not a SaaS tier."* It also requires written approval by Duende and carries audit + back‑pay rights. ([Community Edition](https://duendesoftware.com/products/communityedition))

The binding license terms (from the [License Agreement PDF](https://duendesoftware.com/license/SoftwareLicense.pdf), effective Jun 2 2026):

- **A "Deployment" is one production instance at one URL.** A load‑balanced cluster behind one URL counts as one deployment — good news for the shared SaaS authority.
- **Internal use, binary only, one legal entity.** *"Your Affiliates, customers, and any other third party using the Software must procure their own license."* No service‑bureau / managed‑service use; no use to build a "competing product."
- **Subscription only — no perpetual fallback.** Auto‑renews; on termination *"you must immediately cease use of the Software, remove or destroy any instances."* The binary keeps running on an expired key (runtime *warnings*, not a kill‑switch), but continued production use is a contractual breach with audit/back‑pay exposure.

#### 4.1.2 How that model meets Tellma's architecture

**(a) The shared SaaS authority — viable but it bites at scale and on success.**

- One URL = one deployment, so the cluster itself is cheap to license.
- **The user‑count caps are less scary than they look:** Duende's "User‑Management users" cap applies to Duende's *User Management add‑on* (their embeddable identity SDK). If Tellma brings its own ASP.NET Core Identity store, that cap does not meter authenticated users at all. Scaling to 100,000s of users is **not** gated by the tier on this axis — a point in Duende's favour that should be stated fairly.
- **The Client‑ID / BFF‑front‑end caps *do* meter against the fleet.** With the architecturally preferred **per‑distribution confidential client + BFF**, each distribution consumes **1 Client ID + 1 BFF front‑end**. Advanced allows **30**. A fleet of 100s of distributions blows past every published tier into **Custom / negotiated** pricing. The only way to stay within caps is to fall back to the *single shared public client* option — which the architecture itself notes sacrifices BFF and per‑distribution policy. **In other words, Duende's pricing would push Tellma to choose its client topology to fit the license, not the architecture.** That is the wrong tail wagging the dog.
- **Community Edition is "great until you succeed."** While Tellma is sub‑$1M revenue and SaaS‑only, Community Edition is genuinely attractive — all features including SAML, free. But it expires on growth (> $1M / $3M) and is unavailable the moment on‑prem delivery enters the picture.

**(b) The in‑proc / on‑prem mode — this is the disqualifier.**

When a distribution embeds the authority and is delivered to a customer (on‑prem, cloud, or air‑gapped), Duende requires a **Redistribution License** (Exhibit A of the agreement; [use‑case page](https://duendesoftware.com/products/identityserverredist)). Three problems, in increasing severity:

1. **Unbounded, opaque cost.** Redistribution pricing is "scale‑based / contact‑sales," with no public figure. It scales with the number of customer deployments — exactly the axis Tellma intends to grow.
2. **No perpetual fallback for on‑prem customers.** A subscription that contractually requires removal of the software on non‑renewal is operationally hostile for air‑gapped / on‑prem installs you have sold to a third party.
3. **A new exclusion that may rule Tellma out entirely.** As of **June 2, 2026**, the Redistribution Addendum **expressly excludes** *"any Person that is primarily engaged in the provision of professional services, consulting, systems integration outsourced services, or managed services to third parties as a principal line of business,"* and forbids *"structuring multiple engagements as separate Client Products"* to dodge per‑customer licensing. Tellma's model — bespoke per‑customer distributions, some delivered on‑prem — sits uncomfortably close to this carve‑out. **Whether the redistribution license is even *available* to Tellma is an open legal question, not merely a cost.** ([v8 announcement](https://duendesoftware.com/blog/20260602-your-identity-your-terms-duendes-modular-identity-infrastructure-v8-release))

**(c) OpenIddict, against the same two facts.** Apache‑2.0 ([LICENSE](https://github.com/openiddict/openiddict-core/blob/dev/LICENSE.md)) grants a perpetual, irrevocable, royalty‑free right to use, modify, and **redistribute** — commercially, in SaaS, and embedded in on‑prem software sold to third parties — with only attribution/notice obligations. There are **no client/deployment/user caps, no revenue gate, no redistribution addendum, no professional‑services exclusion, and no removal‑on‑termination clause.** Per‑distribution confidential clients + BFF for all 100s of distributions cost **$0** incremental. This is the same Apache‑2.0 posture the Tellma platform itself adopts, so it composes cleanly with "distributions are proprietary and may be sold on‑prem."

#### 4.1.3 Cost summary

| Scenario | Duende | OpenIddict |
|---|---|---|
| Local dev (in‑proc) | Free (dev/test exempt) | Free |
| Shared SaaS authority, sub‑$1M revenue, SaaS‑only | **Free** (Community Edition) | Free |
| Shared SaaS authority, post‑growth, 100s of distributions | **Custom / negotiated** (Client‑ID & BFF caps exceeded) — realistically tens of $k+/yr | Free (+ optional ~$100–$1,000/mo sponsorship for support) |
| On‑prem / standalone distribution (embedded authority) | **Redistribution License** — scale‑based, contact‑sales, **and possibly unavailable** under the June‑2026 professional‑services exclusion | Free |
| SAML | $1,500/yr add‑on (or included Advanced+/Community) | Free (ITfoxtec, BSD‑3) or ~£4,269 perpetual (RSK) |

**Verdict on licensing & cost: strongly favours OpenIddict**, and decisively so once on‑prem delivery is in scope.

### 4.2 Security

Both are mature and standards‑compliant; neither has a security reputation problem in its current form (note: *IdentityServer4*, the free predecessor, is EOL with unpatched CVEs — that risk attaches to IS4, not to Duende or OpenIddict).

- **Duende** carries the strongest pedigree in the .NET OAuth/OIDC world (its authors effectively wrote the reference implementation), offers **FAPI 1.0/2.0 conformance** as a paid module, and provides commercial security SLAs and coordinated disclosure. Its turnkey, config‑driven surface means a basic setup has fewer hand‑rolled footguns.
- **OpenIddict** is also standards‑compliant and notably **encrypts tokens by default** (requires both signing and encryption keys), which is a secure‑by‑default stance. Its lower‑level model means *you* own more of the security‑relevant code (endpoints, UI) — more control, but more surface to get right. Security‑patch cadence is a real consideration: only the **latest major** gets free fixes, and the project is effectively single‑maintainer — though Apache‑2.0 means a critical fix can always be forked/back‑ported, sponsorship buys extended‑support windows, and the ABP ecosystem keeps versions patched.

On the user's sub‑criterion — *"how difficult its design makes it to extend in an insecure way"* — Duende's turnkey defaults edge it for a team that wants guardrails; OpenIddict's explicitness suits a team that wants to own and audit every decision. For an AI‑native shop generating bespoke code under test, the explicit model is workable. **Verdict: a slight, defensible edge to Duende, not decisive.**

### 4.3 Performance & scale

Neither vendor publishes hard throughput/RPS benchmarks (a genuine gap on both sides). In practice **both are stateless ASP.NET Core libraries that scale horizontally** behind a shared operational store (EF Core / MongoDB / custom) and shared signing keys, with cacheable configuration and discovery documents. Real‑world performance is dominated by **your store, caching, and signing strategy** — exactly the layers [ARCHITECTURE.md → Performance](../../ARCHITECTURE.md) already mandates Tellma optimise — not by the framework. OpenIddict additionally supports **Native AOT + trimming** (7.0+) for faster cold‑start/smaller footprint, and ships `OpenIddict.Quartz` for background token pruning. Both can reach 100,000s of users. **Verdict: effectively neutral**; Tellma's own caching/store design is the lever, and it owns that either way.

### 4.4 Feature coverage

| Capability | Duende | OpenIddict | Where it really lives |
|---|---|---|---|
| Auth Code + PKCE (SPA) | ✅ first‑class | ✅ first‑class | the framework |
| Client credentials (S2M, machine accounts) | ✅ config‑driven | ✅ you write the token endpoint glue | the framework |
| Device flow, refresh, introspection, revocation | ✅ | ✅ | the framework |
| Token Exchange, **PAR**, **DPoP** | ✅ (PAR/DPoP in core for all v8 tiers) | ✅ PAR, token exchange; DPoP support present | the framework |
| CIBA, mTLS, JAR, Resource Isolation, FAPI | ✅ (Extended tiers / FGSC add‑on) | Partial / DIY | the framework |
| **SAML 2.0 / WS‑Fed** | ✅ SAML add‑on (paid); WS‑Fed historically available | ❌ native; ✅ via ITfoxtec (free) or RSK (paid) | add‑on either way |
| **Social / external logins** | ASP.NET handlers + dynamic providers (runtime, paid tier) | **100+ built‑in providers** (`OpenIddict.Client.WebIntegration`) | OpenIddict ships the longest ready list |
| SSO across apps/domains | ✅ core competency | ✅ core competency | the framework |
| **Passkeys, TOTP/SMS/email 2FA** | via ASP.NET Core Identity | via ASP.NET Core Identity | **Identity + your UI (a wash)** |
| **Custom branding / white‑label** | you own the UI (+ documented hooks) | you own the UI | your UI (a wash) |
| Admin UI / user management | optional **User Management** SDK (paid) | none native; RSK AdminUI or ABP Volo.OpenIddict.Pro (paid), or build your own | — |

Two honest observations:

- **Duende wins on turnkey breadth and advanced/financial‑grade protocols.** If Tellma needed FAPI conformance, CIBA, or runtime‑managed per‑tenant external IdPs *today*, Duende delivers them with less code. For a general ERP these are not near‑term requirements.
- **OpenIddict's historical gaps are now closed.** The **Rock Solid Knowledge ↔ Duende split (Feb 2026)** produced a commercial component line — **SAML2P, FIDO2, SCIM, AdminUI — targeting OpenIddict**, plus a free IdentityServer4 fork (Open.IdentityServer). SAML and admin‑UI are therefore **no longer disqualifiers** for OpenIddict; they are available free (ITfoxtec) or commercially (RSK) as needed. ([Why we forked](https://www.identityserver.com/articles/why-we-maintain-and-provide-a-free-forever-open-source-identityserver), [openiddictcomponents.com](https://www.openiddictcomponents.com/products))

**Verdict: Duende is broader out of the box; OpenIddict covers every Tellma requirement, with SAML and admin‑UI now available alongside it.** Net edge to Duende on raw breadth, but not on any requirement Tellma actually has.

### 4.5 Embeddability (local dev + standalone/on‑prem)

Both embed as ordinary ASP.NET Core middleware, so *technically* either supports the in‑proc mode. The difference is **legal, not technical**, and it is covered in §4.1.2(b): embedding Duende into distributions delivered to customers triggers redistribution licensing (cost + availability risk + no perpetual fallback); embedding OpenIddict is unrestricted under Apache‑2.0. **Verdict: strongly favours OpenIddict** — this is the criterion where the architecture's on‑prem ambition and Duende's license collide head‑on.

### 4.6 Documentation, support, ecosystem, governance

- **Duende:** broad, well‑regarded docs; quickstarts and per‑version upgrade guides; **commercial support with SLAs** on paid tiers; a stable founder‑led company with the field's most authoritative lineage. The clearest "someone to call" story.
- **OpenIddict:** good docs + an official samples repo + a very strong third‑party tutorial ecosystem (Damien Bod et al.); **100+ social providers**; ~22M+ NuGet downloads; chosen as the **default IdP by the ABP framework** when ABP declined to move to commercial Duende — a meaningful ecosystem endorsement, with an official IdentityServer→OpenIddict migration guide. **The principal risk is bus‑factor:** it is effectively a single‑maintainer project, and *free* support is gated behind GitHub sponsorship ($2–$1,000/mo tiers buy ticket allowances and extended‑support windows). Apache‑2.0 caps the downside (the code can be forked and maintained), and the RSK commercial ecosystem now offers a paid support path around OpenIddict.

**Verdict: Duende wins on turnkey support and institutional stability; OpenIddict wins on ecosystem openness and zero‑cost adoption, with sponsorship/RSK available if a support contract is wanted.** For a team that builds bespoke under test by default, OpenIddict's model is acceptable; sponsoring at a professional tier is cheap insurance.

### 4.7 Additional considerations

- **Licensing stability / legal risk (criterion 7):** Duende has changed its commercial terms twice in four years — going paid (2022) and the modular v8 overhaul with the **new redistribution professional‑services exclusion** (June 2026). For a dependency embedded in 100s of long‑lived distributions, terms shifting under you — in a direction that may exclude your business model — is a strategic hazard. OpenIddict's Apache‑2.0 grant is **irrevocable**; it cannot be relicensed out from under existing versions. **Strongly favours OpenIddict.**
- **Reversibility / lock‑in (criterion 8):** Distributions are plain **OIDC relying parties** — they speak standard OIDC to whatever authority answers. This makes the authority **swappable** (the architecture already names Entra External ID as a possible future migration). Choosing OpenIddict now does **not** foreclose moving to Duende, Entra, or anything else later; it lowers the stakes of the decision and means the cheap, unrestricted option is also the low‑regret one.
- **Operational control:** OpenIddict has no license key, no runtime license‑warning banners, and no audit clause — simpler to operate across a fleet of on‑prem installs. Duende emits runtime warnings on unlicensed feature use and retains audit/back‑pay rights.
- **AI‑native / agentic auth:** Both now support Dynamic Client Registration and token exchange relevant to MCP/agent scenarios (Duende v7.4+ markets this explicitly; OpenIddict supports DCR + token exchange since 7.0). Neutral, slight marketing edge to Duende.
- **Total cost of ownership:** Duende = a recurring subscription that **grows with scale** plus per‑customer redistribution fees — unbounded and partly unpredictable. OpenIddict = a **bounded** upfront engineering cost (server scaffolding, UI, admin) plus optional flat sponsorship. For a platform whose explicit thesis is that *"the cost of bespoke is no longer the bottleneck,"* trading recurring license risk for one‑time engineering is squarely on‑strategy.

---

## 5. Scorecard

Weighted against Tellma's criteria (5 = strongly favours, 3 = neutral, 1 = strongly against — for *this* platform, not in the abstract):

| Criterion | Weight | Duende | OpenIddict | Notes |
|---|---|---|---|---|
| Licensing & cost | ★★★★★ | 2 | 5 | Per‑deployment + redistribution model misfits the fleet/on‑prem shape. |
| Embeddability (on‑prem/local) | ★★★★★ | 1 | 5 | Redistribution license: cost + availability risk + no perpetual fallback. |
| Licensing stability / legal risk | ★★★★ | 2 | 5 | Apache‑2.0 is irrevocable; Duende terms have churned. |
| Security | ★★★★ | 5 | 4 | Both strong; Duende edge on FAPI + commercial SLAs + turnkey guardrails. |
| Feature coverage | ★★★★ | 5 | 4 | Duende broader OOTB; OpenIddict covers every actual requirement. |
| Performance / scale | ★★★ | 4 | 4 | Dominated by Tellma's own store/caching either way. |
| Docs / support / longevity | ★★★ | 5 | 3 | Duende: commercial support. OpenIddict: bus‑factor, sponsorship‑gated. |
| Reversibility / lock‑in | ★★ | 4 | 5 | OIDC RPs make the authority swappable; cheap option is low‑regret. |

The two heaviest criteria (licensing/cost and embeddability) point hard at OpenIddict, and the architecture makes those two criteria the most load‑bearing of all. Duende's wins (security pedigree, turnkey breadth, support) are real but land on lighter‑weighted criteria or on requirements Tellma does not have yet.

---

## 6. Risks and mitigations

**If Tellma picks OpenIddict (recommended):**

| Risk | Severity | Mitigation |
|---|---|---|
| Single‑maintainer bus‑factor | Medium | Apache‑2.0 → forkable; sponsor at a professional tier ($100–$1,000/mo) for support + extended‑support windows; RSK commercial ecosystem now supports OpenIddict; ABP keeps versions patched. |
| More engineering up front (endpoints, UI, admin) | Medium | On‑brand for an AI‑native, bespoke‑first platform; the auth‑server scaffolding is built **once** in `Tellma.Identity` and reused by every distribution and the in‑proc mode. |
| No native SAML / admin UI | Low | ITfoxtec (free, BSD‑3) or RSK SAML2P; admin UI via RSK AdminUI, ABP Volo.OpenIddict.Pro, or a small bespoke console. Defer until a real SAML/admin requirement appears. |
| Only latest major gets free security fixes | Low–Med | Track the current major (cheap, given dependabot discipline already in the architecture); sponsorship extends the window. |

**If Tellma picks Duende instead:**

| Risk | Severity | Mitigation |
|---|---|---|
| Redistribution license may be **unavailable** under the June‑2026 professional‑services exclusion | **High** | Must be confirmed in writing with Duende legal *before* committing; no clean mitigation if denied. |
| Unbounded, opaque cost scaling with distributions/on‑prem customers | High | Negotiate Custom tier; budget for growth — but the cost is structurally tied to the fleet size you intend to grow. |
| License‑model churn | Medium | Contractual grandfathering helps existing orders, but new restrictions apply at renewal/expansion. |
| Client‑ID/BFF caps distort client topology | Medium | Either pay for Custom or fall back to the shared‑public‑client model (losing BFF + per‑distribution policy). |

---

## 7. Recommendation

**Adopt OpenIddict as Tellma's OIDC authority** — for the shared SaaS server and for the in‑proc (local‑dev + on‑prem/standalone) mode alike.

The reasoning in one paragraph: the identity server is the one platform component that is **both** fanned out across 100s of OIDC clients **and** embedded into software delivered to customers on‑prem. Duende's June‑2026 licensing meters on exactly those two axes (client IDs / BFF front‑ends, and redistribution), turns "scale" into "cost," and — most seriously — adds a professional‑services/consulting/SI exclusion that may make the redistribution license **unavailable** to Tellma's bespoke‑distribution business at all, with no perpetual fallback for on‑prem customers. OpenIddict's Apache‑2.0 grant removes every one of those constraints, costs nothing per deployment, matches the platform's own Apache‑2.0 + proprietary‑on‑prem posture, and is irrevocable. The price — more engineering and single‑maintainer governance risk — is bounded, on‑strategy for an AI‑native bespoke‑first platform, and de‑risked by Apache‑2.0 forkability, optional sponsorship, and the new RSK component ecosystem. And because distributions are standard OIDC relying parties, the decision is **reversible**: if Tellma later wants Duende's turnkey breadth or a managed authority like Entra External ID, it can migrate without touching the distributions.

**Conditions under which Duende would be the better choice instead** (worth stating, so the recommendation is falsifiable):

- On‑prem / standalone delivery is **dropped entirely** as a product capability (removing the redistribution trigger), **and**
- the organisation either stays comfortably inside Community Edition's $1M/$3M envelope or is happy to pay Custom‑tier pricing for the fleet, **and**
- the team values turnkey delivery + a commercial support SLA + FAPI conformance over cost, control, and licensing certainty.

If those flip, revisit. They do not describe Tellma today.

---

## 8. Implications & next steps

1. **This recommendation revises [ARCHITECTURE.md](../../ARCHITECTURE.md).** The document currently names **Duende** in the Glossary, the Identity section, the in‑proc‑mode discussion, and two diagrams, and the open question *"when, if ever, do we move from Duende to Microsoft Entra External ID?"* If this recommendation is accepted, those should be updated to OpenIddict (the open question becomes "Duende/Entra as a possible future migration," with the reversibility argument from §4.7 as the rationale). I can prepare that edit on request.
2. **Build `Tellma.Identity` on OpenIddict + ASP.NET Core Identity** — Identity for the user store/login/2FA/passkeys, OpenIddict for the protocol, sharing one codebase between the shared App Service and the in‑proc mode (selected by configuration, as the architecture already specifies).
3. **Defer SAML and admin‑UI** until a concrete requirement exists; when it does, evaluate ITfoxtec (free) vs RSK (commercial) for SAML and RSK AdminUI / ABP / bespoke for admin.
4. **Sponsor OpenIddict** at a professional tier once it is in production, as cheap support insurance and to support the maintainer the platform now depends on.
5. **Validate at scale early:** prototype the per‑distribution confidential‑client + BFF topology against OpenIddict with the intended operational store and caching, to confirm the performance principles hold before many distributions exist.
6. *(If Duende is reconsidered despite this analysis)* get written confirmation from Duende legal that the redistribution license is available to Tellma's business model **before** any commitment — per §6 this is the single highest‑severity unknown on that path.

---

## 9. Alternatives considered and rejected

The field was narrowed to Duende and OpenIddict before the head‑to‑head above. The narrowing filter is Tellma's hardest constraint, the **in‑proc identity mode**: a candidate must be able to **run embedded inside a .NET distribution** (for local dev and on‑prem/standalone delivery) **at zero per‑deployment cost**. That single test eliminates almost everything — most credible IdPs are separate servers or managed services, which can serve the shared SaaS authority but cannot ship inside an air‑gapped, .NET‑only distribution without dragging a second runtime or a cloud dependency along.

| Solution | Category | Why not (for Tellma) |
|---|---|---|
| **Open.IdentityServer** (RSK) | Embeddable .NET library | The closest third option — see below. Rejected over OpenIddict on maturity/ecosystem, not on fit. |
| **ABP / Volo.OpenIddict.Pro** | Embeddable (OpenIddict + admin UI) | It *is* OpenIddict underneath; doesn't change the licensing calculus. Adopting it means adopting the heavy ABP framework. Useful as a way to *get* an OpenIddict admin UI, not as a distinct engine. |
| **TheIdServer** (Aguafrommars) | Embeddable (OpenIddict + admin/multi‑tenancy) | Also OpenIddict underneath; small, effectively single‑maintainer project — more bus‑factor than OpenIddict itself, for the same engine. |
| **IdentityServer4** | Embeddable .NET library | **EOL since Nov 2022 with known unpatched CVEs.** Open.IdentityServer exists precisely to replace it. Rejected outright. |
| **Keycloak** | Self‑hosted standalone server | Mature, Apache‑2.0, native SAML/WS‑Fed, CNCF‑backed — but **Java/JVM**. Cannot embed in the in‑proc mode; theming/extension is FreeMarker + Java SPIs; shipping a JVM beside every on‑prem distribution breaks the "clone authenticates with no external dependency" story. Viable only as a standalone shared authority, at the cost of leaving the .NET stack. |
| **Microsoft Entra External ID** | Managed cloud CIAM | Already named in [ARCHITECTURE.md](../../ARCHITECTURE.md) as a possible future migration. Azure‑native, scales massively, fully managed — but the **opposite of embeddable**: no on‑prem/air‑gapped story, per‑MAU pricing at scale, less UX control. The "stop running your own on the SaaS side" option, not an embed option. |
| **Auth0 / Okta** | Managed cloud CIAM | Mature but expensive at scale, vendor lock‑in, no embed. Surfaced in research mainly as social‑login *providers*, not as candidates. |
| **Zitadel, Ory (Hydra/Kratos), Authentik, Logto, Casdoor, FusionAuth** | Self‑hostable open‑source servers | Credible (FusionAuth even runs on‑prem with a free community tier), but **none are .NET‑native/embeddable** — each is a separate server to operate and ship, the same disqualifier as Keycloak. *Not researched in depth — listed for completeness, treat as pointers rather than findings.* |

Two of the rejected options deserve more than a table row, because they are the ones that would come back into contention if a constraint changed:

- **Open.IdentityServer** is the only genuine peer of OpenIddict on Tellma's filter — a **free‑forever, Apache‑licensed .NET 10 fork of IdentityServer4**, launched Feb 2026 when Rock Solid Knowledge split from Duende ([repo](https://github.com/RockSolidKnowledge/Open.IdentityServer), [rationale](https://www.identityserver.com/articles/why-we-maintain-and-provide-a-free-forever-open-source-identityserver)). It gives the IdentityServer turnkey feel without Duende's license. It was ranked behind OpenIddict, not excluded, for three reasons: it carries the **older IS4 architecture**; it is **brand‑new and unproven as a fork** (no track record yet); and it is **single‑vendor‑stewarded by RSK, who simultaneously sell the commercial add‑ons** (SAML2P/FIDO2/AdminUI) it funnels toward — so its long‑term "free forever" commitment and RSK's bus‑factor are the open questions. If OpenIddict's single‑maintainer risk ever felt unacceptable, this is the first place to look next, and the two are not mutually exclusive (both are embeddable, Apache‑family, and OIDC‑standard, so a later switch is low‑cost).
- **Keycloak** and **Entra External ID** are the strongest options *if the in‑proc/on‑prem requirement were dropped* — Keycloak as a self‑hosted standalone authority, Entra as a managed one. Both solve a different problem (a centralized authority the whole estate federates to) and both fail the embed test, so neither competes with OpenIddict for the role as scoped today. They remain the natural candidates for the architecture's already‑noted "centralize the authority later" path.

The conclusion: the embed + on‑prem‑in‑.NET + zero‑per‑deployment‑cost combination is satisfied by a very short list — **OpenIddict and Open.IdentityServer** — and of those two OpenIddict is the more mature and better‑supported. None of the wider field displaces the recommendation.

---

## 10. Sources

**Duende**
- Pricing (live): https://duendesoftware.com/pricing
- License Agreement PDF (effective 2026‑06‑02): https://duendesoftware.com/license/SoftwareLicense.pdf
- Licensing docs: https://docs.duendesoftware.com/general/licensing/
- Community Edition: https://duendesoftware.com/products/communityedition
- Redistribution use case: https://duendesoftware.com/products/identityserverredist
- Modular v8 / June‑2026 licensing change: https://duendesoftware.com/blog/20260602-your-identity-your-terms-duendes-modular-identity-infrastructure-v8-release
- Core vs Extended protocols (2026‑06‑16): https://duendesoftware.com/blog/20260616-core-vs-extended-protocols-in-duende-identityserver-v8
- v7.4 release (.NET 10 LTS, 2025‑12‑02): https://duendesoftware.com/blog/20251202-duende-identityserver-v74-release-now-available-securing-the-age-of-ai-and-dotnet-10-lts
- NuGet (8.0.2, 2026‑06‑16): https://www.nuget.org/packages/Duende.IdentityServer
- IdentityServer4 lineage / EOL: https://duendesoftware.com/blog/20250306-identityserver4-public-again

**OpenIddict**
- Repo + README (license, support policy): https://github.com/openiddict/openiddict-core
- LICENSE (Apache‑2.0): https://github.com/openiddict/openiddict-core/blob/dev/LICENSE.md
- Documentation: https://documentation.openiddict.com/
- Web providers (100+): https://documentation.openiddict.com/integrations/web-providers
- 7.0 release notes: https://kevinchalet.com/2025/07/07/openiddict-7-0-is-out/
- 6.0 GA (support policy, ABP exception): https://kevinchalet.com/2024/12/17/openiddict-6-0-general-availability/
- GitHub Sponsors tiers: https://github.com/sponsors/kevinchalet
- NuGet (7.5.0, 2026‑04‑22): https://www.nuget.org/packages/OpenIddict
- ABP IdentityServer→OpenIddict migration guide: https://abp.io/docs/latest/release-info/migration-guides/openiddict-step-by-step

**ASP.NET Core Identity (the layer both sit on)**
- Passkeys (.NET 10): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0
- MFA: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/mfa?view=aspnetcore-10.0
- External / social logins: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/
- fido2-net-lib: https://github.com/passwordless-lib/fido2-net-lib

**SAML options & the 2026 Rock Solid Knowledge split**
- RSK "Why we forked IdentityServer4" / Open.IdentityServer: https://www.identityserver.com/articles/why-we-maintain-and-provide-a-free-forever-open-source-identityserver
- RSK SAML2P for OpenIddict: https://www.openiddictcomponents.com/products/saml2p
- RSK component line: https://www.openiddictcomponents.com/products
- ITfoxtec.Identity.Saml2 (BSD‑3): https://github.com/ITfoxtec/ITfoxtec.Identity.Saml2
- Sustainsys.Saml2: https://sustainsys.com/sustainsyssaml2-libraries

**Comparative / sentiment**
- Duende vs Keycloak vs OpenIddict (2026): https://codingdroplets.com/duende-identityserver-vs-keycloak-vs-openiddict-in-net-which-to-use-in-2026
- Thinktecture — alternatives to IdentityServer: https://www.thinktecture.com/en/identityserver/three-alternatives-to-identityserver/
- Damien Bod — Duende multi‑tenant customization: https://damienbod.com/2025/02/03/multiple-client-sign-in-customizations-using-duende-identity-provider/

> **Caveats / re‑verify before contracting:** Duende tier prices and the redistribution exclusion were scraped from the live site in June 2026 and supersede older third‑party figures (e.g. the legacy "$1,500 Starter" and standalone‑BFF listings) — confirm on duendesoftware.com before budgeting. No official throughput benchmarks exist for either product. The redistribution license's availability to Tellma's business model is the single most important item to confirm in writing if Duende is reconsidered.
