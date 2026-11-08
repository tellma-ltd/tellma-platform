# Tellma.Core.Abstractions

The platform's contract surface: the interfaces, value objects, attributes, exceptions and enums
every layer above Core consumes, and the only Tellma package a module, industry, compliance, locale,
or connector library references. It carries **no package dependencies at all**. Its one edge is a
project reference to `Tellma.Core.Queryex`, which itself references nothing: the filter and query
types contract members name are Queryex types, so the edge carries no weight. Everything else here is
BCL, which is what keeps the dependency graph of the packages built on top of it honest.

Nothing in this project has an implementation worth speaking of. Behaviour lives in `Tellma.Core`
and its optional siblings (`Tellma.Core.Email`, `Tellma.Core.Webhooks`), and only a composition root
picks which implementations to register.

## What lives here

| Namespace | Contents |
|---|---|
| `Tellma.Core.Abstractions.TableTypes` | The canonical bulk-operation row shapes (`IdList`, `BigIdList`, …) the SQL Server table-type path binds into table-valued parameters. |
| `Tellma.Core.Abstractions.Email` | The transport-agnostic email contract: [EmailMessage](Email/EmailMessage.cs), [IEmailSender](Email/IEmailSender.cs) and its batch invariants, [EmailCorrelation](Email/EmailCorrelation.cs), delivery events and their [dispatcher](Email/IEmailDeliveryEventDispatcher.cs)/[handler](Email/IEmailDeliveryEventHandler.cs) seams, the [transport registration](Email/EmailTransportRegistration.cs) record adapters declare themselves through, and the [telemetry names](Email/EmailTelemetryNames.cs) plus the [tag values](Email/EmailTelemetryTagValues.cs) the pipeline and its adapters share. |
| `Tellma.Core.Abstractions.Webhooks` | The transport-neutral inbound-callback contract: [WebhookRequest](Webhooks/WebhookRequest.cs), [WebhookResult](Webhooks/WebhookResult.cs), and [IWebhookReceiver](Webhooks/IWebhookReceiver.cs). Email is its first consumer; nothing about it is email-specific. |
| `Tellma.Core.Abstractions.Hosting` | [DeploymentIdentity](Hosting/DeploymentIdentity.cs) — the fleet-unique name of the running deployment, hardcoded per composition rather than configured. |
| `Tellma.Core.Abstractions.Tenancy` | [ISandboxContext](Tenancy/ISandboxContext.cs) — whether the ambient unit of work belongs to a sandbox tenant, the seam every side-effecting connector routes on. |
| `Tellma.Core.Abstractions.Composition` | The feature contract a module ships a feature through without referencing the runtime: [ITellmaFeature](Composition/ITellmaFeature.cs), [RequiresAttribute](Composition/RequiresAttribute.cs), [FeatureDeclaration](Composition/FeatureDeclaration.cs) and its [options bindings](Composition/FeatureOptionsBinding.cs), [FeatureContribution](Composition/FeatureContribution.cs) and the data-only item records it collects ([services](Composition/ServiceContributionItem.cs), [standalone table types](Composition/TableTypeContributionItem.cs), [shape requirements](Composition/ShapeRequirementContributionItem.cs)), [CollectionServiceAttribute](Composition/CollectionServiceAttribute.cs), [IStartupCheck](Composition/IStartupCheck.cs), [CompositionProblem](Composition/CompositionProblem.cs) and [TellmaCompositionException](Composition/TellmaCompositionException.cs), [DeploymentVersions](Composition/DeploymentVersions.cs), and [TellmaRoutes](Composition/TellmaRoutes.cs) — the one source of the reserved route words. |
| `Tellma.Core.Abstractions.Client` | The client metadata a distribution's build emits: [IClientAssetContributor](Client/IClientAssetContributor.cs), the [ClientAssetContext](Client/ClientAssetContext.cs) it writes sections and generated files into, and [ClientMetadataChunk](Client/ClientMetadataChunk.cs). |
| `Tellma.Core.Abstractions.Api` | The wire contract. [TellmaJson](Api/TellmaJson.cs), the one set of JSON rules every serializer uses (`long` and `decimal` as exact strings, seven-digit timestamps, strict numbers, enums as names), with its [scalar converters](Api/ScalarJsonConverters.cs) and the source-generated [TellmaApiJsonContext](Api/TellmaApiJsonContext.cs); the request records ([QueryRequest](Api/QueryRequest.cs), [GetRequest](Api/GetRequest.cs), [IdsRequest](Api/IdsRequest.cs), [IdRequest](Api/IdRequest.cs), [EntityActionRequest](Api/EntityActionRequest.cs), [ParentIdsRequest](Api/ParentIdsRequest.cs), [DeleteByQueryRequest](Api/DeleteByQueryRequest.cs), …) and result records ([AffectedResult](Api/AffectedResult.cs), [JobAccepted](Api/JobAccepted.cs), [QueryDiagnostic](Api/QueryDiagnostic.cs), …); [WireIds](Api/WireIds.cs), which narrows request ids to a stack's key; the projection attributes [ApiResource](Api/ApiResourceAttribute.cs), [DefaultSelect](Api/DefaultSelectAttribute.cs), [RelatedSelect](Api/RelatedSelectAttribute.cs) and [DetailsExpand](Api/DetailsExpandAttribute.cs) with [McpExposure](Api/McpExposure.cs); the [header names](Api/TellmaHeaders.cs) and the [web host's](Api/ApiTelemetryNames.cs) and [MCP server's](Api/McpTelemetryNames.cs) telemetry names. |
| `Tellma.Core.Abstractions.Crud` | The annotations that declare a stack: [StackOperations](Crud/StackOperations.cs) and [StackAttribute](Crud/StackAttribute.cs) (operations and ceilings), [EntityActionAttribute](Crud/EntityActionAttribute.cs) and [ApiActionAttribute](Crud/ApiActionAttribute.cs) (the security boundary of every custom operation), [DetailsExtrasAttribute](Crud/DetailsExtrasAttribute.cs), [ApiRouteAttribute](Crud/ApiRouteAttribute.cs) and [EntityRefAttribute](Crud/EntityRefAttribute.cs). |
| `Tellma.Core.Abstractions.Validation` | What a validator writes errors against: [ValidationPath](Validation/ValidationPath.cs) and its [segments](Validation/PathSegment.cs) (immutable, compared by value), [ValidationErrors](Validation/ValidationErrors.cs) with its typed, expression-named view, and the platform's [ValidationCodes](Validation/ValidationCodes.cs). |
| `Tellma.Core.Abstractions.Errors` | The closed exception set the edge maps by type: [TellmaException](Errors/TellmaException.cs), whose constructor only this assembly can call, and its sealed members ([ValidationException](Errors/ValidationException.cs), [NotFoundException](Errors/NotFoundException.cs), [ForbiddenException](Errors/ForbiddenException.cs) with [ForbiddenReason](Errors/ForbiddenReason.cs), [ConcurrencyException](Errors/ConcurrencyException.cs), [ConfirmationRequiredException](Errors/ConfirmationRequiredException.cs), [CountMismatchException](Errors/CountMismatchException.cs), [LimitExceededException](Errors/LimitExceededException.cs) with [LimitNames](Errors/LimitNames.cs), [InvalidQueryException](Errors/InvalidQueryException.cs), [BadRequestException](Errors/BadRequestException.cs), [IndividualRequiredException](Errors/IndividualRequiredException.cs), [DependencyUnavailableException](Errors/DependencyUnavailableException.cs), [PartialFailureException](Errors/PartialFailureException.cs)); the coded error items [CodedError](Errors/CodedError.cs) and [ValidationError](Errors/ValidationError.cs); and the problem-detail records [ConcurrencyConflict](Errors/ConcurrencyConflict.cs) and [ConfirmationRequest](Errors/ConfirmationRequest.cs). |
| `Tellma.Core.Abstractions.Access` | The endpoint metadata the startup audit reads: [SecurableEndpointMetadata](Access/SecurableEndpointMetadata.cs) and [MemberEndpointMetadata](Access/MemberEndpointMetadata.cs). |
| `Tellma.Core.Abstractions.Notifications` | The descriptors a feature registers its notifications with: [NotificationTypeDescriptor](Notifications/NotificationTypeDescriptor.cs) with [NotificationSeverity](Notifications/NotificationSeverity.cs), and [NotificationChannelDescriptor](Notifications/NotificationChannelDescriptor.cs). |

## Rules this project lives by

- **No package references, ever.** A dependency here propagates to every library in the platform.
  When a contract seems to need one (a hosting constant, a logging abstraction), spell the value out
  instead and note why in a comment. `System.Text.Json` and its source generator are part of the
  shared framework, not a package.
- **Validated value objects validate in their constructor, and only there.** `EmailCorrelation`,
  `DeploymentIdentity`, `ValidationPath` and the coded errors redeclare their validated components
  as get-only properties, so a `with` expression that would bypass validation does not compile, and
  they copy the collections they capture, so the caller's original cannot change them later.
- **Declarations are data.** Attributes such as `[Stack]` and `[EntityAction]` hold what a type or
  method declares and validate nothing: the startup checks read them and report every problem at
  once, naming the entity or method, which an exception thrown while reading an attribute could not.
- **The exception set is closed.** Only this assembly derives from `TellmaException`, and every
  member is sealed, so the edge maps each by its exact type and anything else is a 500.
- **Contract types carry their invariants in XML docs.** `IEmailSender`'s batch rules — one result
  per message in input order, throw only when nothing was sent, no adapter-level retries — are
  binding on every implementation and are pinned by an executable conformance suite in
  `Tellma.Core.Testing`.
- **Nothing here decides anything at runtime.** Selection, policy, and telemetry emission belong to
  the pipeline packages; this project only names the shapes they agree on.
