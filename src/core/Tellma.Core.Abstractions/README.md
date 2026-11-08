# Tellma.Core.Abstractions

The platform's contract surface: the interfaces, value objects, and enums every layer above Core
consumes, and the only Tellma package a module, industry, compliance, locale, or connector library
references. It carries **no package dependencies at all** — everything here is BCL-only, which is
what keeps the dependency graph of the packages built on top of it honest.

Nothing in this project has an implementation worth speaking of. Behaviour lives in `Tellma.Core`
and its optional siblings (`Tellma.Core.Email`, `Tellma.Core.Webhooks`, `Tellma.Core.Imaging`), and only a composition root
picks which implementations to register.

## What lives here

| Namespace | Contents |
|---|---|
| `Tellma.Core.Abstractions.TableTypes` | The canonical bulk-operation row shapes (`IdList`, `BigIdList`, …) the SQL Server table-type path binds into table-valued parameters. |
| `Tellma.Core.Abstractions.Email` | The transport-agnostic email contract: [EmailMessage](Email/EmailMessage.cs), [IEmailSender](Email/IEmailSender.cs) and its batch invariants, [EmailCorrelation](Email/EmailCorrelation.cs), delivery events and their [dispatcher](Email/IEmailDeliveryEventDispatcher.cs)/[handler](Email/IEmailDeliveryEventHandler.cs) seams, the [transport registration](Email/EmailTransportRegistration.cs) record adapters declare themselves through, and the [telemetry names](Email/EmailTelemetryNames.cs) plus the [tag values](Email/EmailTelemetryTagValues.cs) the pipeline and its adapters share. |
| `Tellma.Core.Abstractions.Webhooks` | The transport-neutral inbound-callback contract: [WebhookRequest](Webhooks/WebhookRequest.cs), [WebhookResult](Webhooks/WebhookResult.cs), and [IWebhookReceiver](Webhooks/IWebhookReceiver.cs). Email is its first consumer; nothing about it is email-specific. |
| `Tellma.Core.Abstractions.Hosting` | [DeploymentIdentity](Hosting/DeploymentIdentity.cs) — the fleet-unique name of the running deployment, hardcoded per composition rather than configured. [DatabaseCollations](Hosting/DatabaseCollations.cs) — the platform collation (`Latin1_General_100_CI_AS`) every platform database is created in; every platform EF model declares it, and every host that meets an existing database refuses any other. |
| `Tellma.Core.Abstractions.Tenancy` | [ISandboxContext](Tenancy/ISandboxContext.cs) — whether the ambient unit of work belongs to a sandbox tenant, the seam every side-effecting connector routes on. |
| `Tellma.Core.Abstractions.Composition` | The feature contract a module ships a feature through without referencing the runtime: [ITellmaFeature](Composition/ITellmaFeature.cs), [RequiresAttribute](Composition/RequiresAttribute.cs), [FeatureDeclaration](Composition/FeatureDeclaration.cs) and its [options bindings](Composition/FeatureOptionsBinding.cs), [FeatureContribution](Composition/FeatureContribution.cs) and the data-only item records it collects ([services](Composition/ServiceContributionItem.cs), [standalone table types](Composition/TableTypeContributionItem.cs), [shape requirements](Composition/ShapeRequirementContributionItem.cs)), [CollectionServiceAttribute](Composition/CollectionServiceAttribute.cs), [IStartupCheck](Composition/IStartupCheck.cs), [CompositionProblem](Composition/CompositionProblem.cs) and [TellmaCompositionException](Composition/TellmaCompositionException.cs), [DeploymentVersions](Composition/DeploymentVersions.cs), and [TellmaRoutes](Composition/TellmaRoutes.cs) — the one source of the reserved route words. |
| `Tellma.Core.Abstractions.Client` | The client metadata a distribution's build emits: [IClientAssetContributor](Client/IClientAssetContributor.cs), the [ClientAssetContext](Client/ClientAssetContext.cs) it writes sections and generated files into, and [ClientMetadataChunk](Client/ClientMetadataChunk.cs). |
| `Tellma.Core.Abstractions.Blobs` | The blob contracts: [IBlobStore](Blobs/IBlobStore.cs) (bytes by name for one tenant per call, create-only writes) with [BlobWrite](Blobs/BlobWrite.cs), [BlobContent](Blobs/BlobContent.cs), [BlobStoreEntry](Blobs/BlobStoreEntry.cs) and its two failures ([BlobStoreException](Blobs/BlobStoreException.cs), [BlobAlreadyExistsException](Blobs/BlobAlreadyExistsException.cs)); [BlobName](Blobs/BlobName.cs), the `{kind}/{k0k1}/{key32}[.{variant}]` grammar every store validates names against; [BlobKindPolicy](Blobs/BlobKindPolicy.cs) and its four presets, [ImagePolicy](Blobs/ImagePolicy.cs), [ThumbnailPolicy](Blobs/ThumbnailPolicy.cs), [ImageFit](Blobs/ImageFit.cs), [ImageFormat](Blobs/ImageFormat.cs) and [BlobContentTypes](Blobs/BlobContentTypes.cs); the image-processing seam [IImageProcessor](Blobs/IImageProcessor.cs) with [ProcessedImage](Blobs/ProcessedImage.cs) and [ImageRejectedException](Blobs/ImageRejectedException.cs); and the [telemetry names](Blobs/BlobTelemetryNames.cs) and [tag values](Blobs/BlobTelemetryTagValues.cs) the runtime and every store adapter share. |
| `Tellma.Core.Abstractions.Entities` | Entity annotations. So far the blob-reference declaration: [BlobReferenceAttribute](Entities/BlobReferenceAttribute.cs), with [BlobPreset](Entities/BlobPreset.cs) and [BlobReadAccess](Entities/BlobReadAccess.cs). |

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
  `Tellma.Core.Testing`; so are `IBlobStore`'s (create-only writes, null for a missing object,
  success for a missing delete, whole-segment listing prefixes).
- **Nothing here decides anything at runtime.** Selection, policy, and telemetry emission belong to
  the pipeline packages; this project only names the shapes they agree on.
