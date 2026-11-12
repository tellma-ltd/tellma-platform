# PC-B01: Phase 1 batch review (PC-000…PC-014)

Three read-only finders walked the integrated phase-1 tree for issues that span checkpoints
(cross-checkpoint contracts, end-to-end journeys, failure/concurrency/security). An Opus fixer
verified each finding against the code, fixed the real ones and recorded the verdicts below.

## Batch review

### Findings

- **L1-B01-E2E-1 (minor).** `client/projects/core/tellma-core-ui/file-preview/internal/tm-file-preview-content.ts`
  (download anchor, `buttonSize` / `downloadClasses`). PC-008 hand-assembled the Download link's
  button classes because "the directive's selector is button-only"; PC-011 widened `TmButton` to
  `button[tmButton], a[tmButton]` and never revisited the file. The comment is now false, the anchor
  is the only `.tm-button` not produced by the directive (a later change to its host classes would
  skip it), and `TmButtonHarness` cannot see it. Fix: make the anchor a `tmButton variant="primary"`
  host and delete the private class logic.
- **L2-B01-1 (major).** `src/apps/Tellma.Identity/Infrastructure/ApiPolicies.cs:40-47`,
  `Services/Provisioning/ClientDescriptorFactory.cs:188, 301-304`,
  `Services/AuthenticationPolicy/TellmaPrincipalFactory.cs:181, 255-267`. The public CLI client holds
  `scp:tellma_identity`; a CLI device or code flow asking for it without a resource gets
  `aud = urn:tellma:identity`, and `ApiPolicies.IdentityScope` checks only scope and audience, never
  the `tellma_kind` claim PC-006 added for this distinction. Any person with an account can then use
  the management API: probe up to 1,000 addresses per call with `existingOnly`, create users and send
  invitation mail to arbitrary addresses, and read the delivery status of every CLI-raised
  invitation. Fix: require `tellma_kind = service` in both management policies, drop the scope from
  the CLI, and test it standalone and in-proc.
- **L2-B01-2 (major).** `Controllers/AuthorizationController.cs:220-246, 379-394`,
  `Services/AuthenticationPolicy/TellmaPrincipalFactory.cs:190-210`,
  `Controllers/DeviceVerificationController.cs:114-121`, `Services/Provisioning/ClientRegistrationConverger.cs:262-284`.
  Consent memory is keyed on (user, client, scopes) and both consent surfaces show only the
  application and the scopes. A user who allows a third-party agent for `<A>/17/mcp` has silently
  allowed it for `<A>/18/mcp`, for `<A>` and for every other distribution the client is granted,
  `prompt=none` included; approving one device code has the same effect. Fix: bind consent to the
  place (record the consented resources on the authorization, match only when they cover the
  request), show the place on Consent and Verify, and test it.
- **L2-B01-3 (minor).** `Services/Provisioning/ClientRegistrationConverger.cs:82, 111, 262-284,
  378-398`, `Services/ResourceGrantValidator.cs:156, 163, 270-288`. A Distribution's moved origin
  is never removed from the `tellma_api` scope's resources (the distribution-origin list) or from the
  CLI/native grants, while PC-009 made that list authoritative for control-plane tokens, document
  clients and the per-tenant pattern; the control plane, the CLI and third-party agents keep minting
  tokens for the old origin indefinitely. Fix: retire the old origin on convergence, with an audit
  trail, and test it.

### Responses

- **L1-B01-E2E-1: fixed in "PC-B01: batch review fixes (PC-000…PC-014)".** The Download anchor is
  now `<a tmButton variant="primary" data-tm-preview-action="download" [href] [download]>`, so the
  directive applies the variant and resolves the size exactly as it does for Print beside it, and
  the harness sees it. `buttonSize`, `downloadClasses` and the `ɵtmControlSize` import are gone; the
  template and stylesheet comments now say the link is a directive host. The parity unit test
  (`sizes the download anchor exactly like the print button, touch size included`) and the
  file-preview e2e download assertions pass unchanged.
- **L2-B01-1: fixed in "PC-B01: batch review fixes (PC-000…PC-014)".** Verified: the CLI descriptor
  held the scope, nothing outside the issuing handler read `tellma_kind`, and a CLI token without a
  resource carried `scope=tellma_identity` and `aud=urn:tellma:identity`, which the policy accepted.
  - `ApiPolicies.IsService` (exactly one `tellma_kind` claim, valued `service`) is now required by
    `IdentityScope` and by `HasControlPlaneAuthority`, which `ControlPlaneScope` and the
    service-account visibility rule share. A person's token, a token without the claim, and one with
    two kind claims answer `403`.
  - The CLI is no longer permitted `tellma_identity` (`PublicNativeClient` lost its
    `includeIdentityScope` flag; convergence rewrites a stored CLI row on the next start). A CLI code
    or device request for the scope is refused before any interaction with OpenIddict's own
    `invalid_request` ("not allowed to use the specified scope").
  - Tests: `ApiPoliciesTests` (individual / missing / wrong-case / two kinds refused by both
    policies), `TenantResourceTests.An_authorization_cannot_ask_for_the_management_scope` (with a
    tenant resource, an origin, and none), `A_device_authorization_cannot_ask_for_the_management_scope`,
    and in-proc `InProcManagementAudienceTests.The_cli_cannot_ask_for_the_management_scope_in_proc`.
    The `<slug>-svc` client-credentials token still answers `200` (the existing invitation,
    delivery-status, service-account and in-proc audience suites).
  - `eng/identity-ui-inspection` obtained its invitation token through a CLI device grant, the very
    path this closes; `inspect.ps1` and the guide now seed a `local` Distribution entry and use
    `local-svc` client credentials.
  - The old integration test asserting `invalid_target` for "CLI with a distribution resource plus
    `tellma_identity`" is replaced by the scope refusal above, since no interactive client holds the
    scope any more. `ResourceGrantValidator`'s interactive management-scope rule and the principal
    factory's derived-origin guard stay as defence in depth.
  - **Deviation (for the spec owner):** spec 0003 §4 row 2a lists "server management" as a CLI
    audience while §11.4 says the management API is called by the distribution backend, M2M. The
    code follows §11.4; row 2a should drop "server management".
- **L2-B01-2: fixed in "PC-B01: batch review fixes (PC-000…PC-014)".** Verified: consent lookup and
  the authorization find-or-create ignored resources, and neither page showed the target.
  - Every permanent authorization now records the resources it covers (`AuthorizationResources`,
    property `tellma:resources`). `TellmaPrincipalFactory.HasCoveringAuthorizationAsync` is the one
    consent-memory check (the controller's private lookup is gone): a valid permanent authorization
    of the user's for the client must hold the scopes **and** cover every requested resource.
    Otherwise the consent page shows again, and `prompt=none` answers `consent_required`.
  - Granting (Accept, device approval, or an implicit client's first use of a new resource) adds the
    resources to the existing authorization instead of creating a second one, because the session
    registry keeps one authorization id per (session, client) and global logout revokes only that
    one. A concurrent change to the same authorization makes the grant answer
    `temporarily_unavailable` rather than overwrite it.
  - Consent and device-approval pages list each place (`ConsentTargetCatalog`): a distribution's
    host, or its host and the tenant id (`acme.app.tellma.com/17`, as the tenant's own address
    reads), hosts in punycode so a look-alike host cannot pass for a familiar one; new resource key
    `ConsentTargets` in en and ar.
  - Granularity: **per resource** (a tenant consent covers neither another tenant nor the whole
    distribution). The spec is silent; this is the strictest reading and the spec owner may widen it
    to per distribution before §A.3's document clients land (decision flagged to the orchestrator).
  - Tests: `AuthorizationResourcesTests`, `ConsentTargetCatalogTests`;
    `ConsentTests.The_consent_screen_names_the_place_the_access_is_for`,
    `Consent_for_one_tenant_does_not_cover_another` (17 remembered; 18 and the origin asked again),
    `A_silent_request_for_an_unconsented_place_answers_consent_required`,
    `Consenting_to_a_second_place_extends_the_one_authorization`; and
    `TenantResourceTests.A_first_party_client_moves_between_tenants_without_consent_on_one_authorization`
    (implicit clients unaffected, one authorization recording both tenants).
- **L2-B01-3: fixed in "PC-B01: batch review fixes (PC-000…PC-014)".** Verified: the origin was
  only ever appended. `ClientRegistrationConverger.ConvergeDistributionAsync` now reads the BFF
  client's stored origin before rewriting it; when the convergence moves it, the old origin is
  retired: the distribution's service accounts move with it (owner origin and `rsrc:` grant become
  the new origin, since ownership is recorded as the owner's origin and an account left behind would
  be invisible to its own distribution), the old origin leaves the `tellma_api` scope's resources and
  the `rsrc:` grants of every client that calls distribution APIs, and an Information log names both
  origins. An old origin another distribution's clients still carry is kept, with a Warning.
  `DistributionConvergence.RetiredOrigin` carries the result into both audit events
  (`DistributionSeedApplied`, onboarding's `ClientCreated`, as `retiredOrigin`).
  - Tests: `DistributionSeedTests.A_moved_origin_retires_the_old_one_from_the_audiences_and_the_grants`,
    `A_moved_distribution_takes_its_service_accounts_with_it`, `An_old_origin_another_distribution_carries_is_kept`;
    integration `MovedOriginTests` (re-onboarding at a new origin: a control-plane token and a CLI
    authorization naming the old origin or a tenant under it answer `invalid_target`, the new origin
    is granted, and the list holds the new origin alone).
  - Residual: refresh tokens issued for the old origin before the move are evaluated against their
    own stored grant, so they keep that audience until they lapse or are revoked (documented in the
    identity README). A convergence interrupted between rewriting the BFF client and retiring the
    old origin leaves it on the list, since the next run no longer sees a move.
