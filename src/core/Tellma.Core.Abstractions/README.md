# Tellma.Core.Abstractions

The platform's contract surface: the interfaces, value objects, and enums every layer above Core
consumes, and the only Tellma package a module, industry, compliance, locale, or connector library
references. It carries **no package dependencies at all** — everything here is BCL-only, which is
what keeps the dependency graph of the packages built on top of it honest.

Nothing in this project has an implementation worth speaking of. Behaviour lives in `Tellma.Core`
and its optional siblings (`Tellma.Core.Email`, `Tellma.Core.Webhooks`), and only a composition root
picks which implementations to register.

## What lives here

| Namespace | Contents |
|---|---|
| `Tellma.Core.Abstractions.TableTypes` | The canonical bulk-operation row shapes (`IdList`, `BigIdList`, …) the SQL Server table-type path binds into table-valued parameters. |
| `Tellma.Core.Abstractions.Email` | The transport-agnostic email contract: [EmailMessage](Email/EmailMessage.cs), [IEmailSender](Email/IEmailSender.cs) and its batch invariants, [EmailCorrelation](Email/EmailCorrelation.cs), delivery events and their [dispatcher](Email/IEmailDeliveryEventDispatcher.cs)/[handler](Email/IEmailDeliveryEventHandler.cs) seams, the [transport registration](Email/EmailTransportRegistration.cs) record adapters declare themselves through, and the [telemetry names](Email/EmailTelemetryNames.cs) the pipeline and its adapters share. |
| `Tellma.Core.Abstractions.Webhooks` | The transport-neutral inbound-callback contract: [WebhookRequest](Webhooks/WebhookRequest.cs), [WebhookResult](Webhooks/WebhookResult.cs), and [IWebhookReceiver](Webhooks/IWebhookReceiver.cs). Email is its first consumer; nothing about it is email-specific. |
| `Tellma.Core.Abstractions.Hosting` | [DeploymentIdentity](Hosting/DeploymentIdentity.cs) — the fleet-unique name of the running deployment, hardcoded per composition rather than configured. |
| `Tellma.Core.Abstractions.Tenancy` | [ISandboxContext](Tenancy/ISandboxContext.cs) — whether the ambient unit of work belongs to a sandbox tenant, the seam every side-effecting connector routes on. |

## Rules this project lives by

- **No package references, ever.** A dependency here propagates to every library in the platform.
  When a contract seems to need one (a hosting constant, a logging abstraction), spell the value out
  instead and note why in a comment.
- **Validated value objects validate in their constructor, and only there.** `EmailCorrelation` and
  `DeploymentIdentity` redeclare their validated components as get-only properties, so a `with`
  expression that would bypass validation does not compile.
- **Contract types carry their invariants in XML docs.** `IEmailSender`'s batch rules — one result
  per message in input order, throw only when nothing was sent, no adapter-level retries — are
  binding on every implementation and are pinned by an executable conformance suite in
  `Tellma.Core.Testing`.
- **Nothing here decides anything at runtime.** Selection, policy, and telemetry emission belong to
  the pipeline packages; this project only names the shapes they agree on.
