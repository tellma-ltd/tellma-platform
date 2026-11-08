# Tellma.Core

The platform's runtime. Every distribution host references it; the web host, the migrator and any
worker compose it through the same composition method. It takes no ASP.NET Core dependency, so a
host without the web stack composes it unchanged, and its services are testable without an HTTP
context.

Today it holds the composition root and the first piece of blob storage, the file-system store.
Data access, the CRUD pipeline, access control, settings, the rest of blobs, Excel, jobs and
notifications join it as namespaces.

## The composition root

```csharp
services.AddTellma("acme", configuration, environment, tellma => tellma
    .AddFeature<GlFeature>()
    .UseEntity<Center, MyCenter>());
```

`AddTellma` runs four steps, then installs the second of two gates:

1. **Collect.** Registers `DeploymentIdentity(slug, environment)` and `DeploymentVersions`, adds
   `CoreFeature` (`core`) first, and runs the composition method. Selection is explicit: there is no
   assembly scanning.
2. **Declare.** Each feature's `Declare` records its `Requires` edges and options bindings, and its
   `Contribute` its data-only items. A nested feature (`contribution.Feature(...)`) joins the same
   two steps.
3. **Graph gate.** Every problem below is collected and thrown at once in one
   `TellmaCompositionException`, before the host is built: a requirement no added feature has
   (naming the feature that requires it), two features with one name, a cycle (with its path), a
   feature type added as two instances, an options type bound to two paths, a slug or feature name
   outside its grammar, a second leaf for one default, an item no realizer handles, and two
   features registering one non-collection service type when neither depends on the other.
4. **Realise.** Items are realised in topological order (`core` first, each feature after what it
   requires and nests, otherwise in the order added). A later feature's service registration
   replaces an earlier one's, so a distribution replaces a pack's service by requiring the pack and
   registering the same service type; a collection service only ever adds: a type marked
   `[CollectionService]` (`IStartupCheck`, `IClientAssetContributor`) or one of the framework's
   multi-registration types (`IHostedService`, `IHostedLifecycleService`, `IConfigureOptions<>`,
   `IConfigureNamedOptions<>`, `IPostConfigureOptions<>`, `IValidateOptions<>`,
   `IOptionsChangeTokenSource<>`, `IStartupFilter`). Realizer problems are thrown together, again before
   the host is built.

The **realised gate**, `TellmaStartupGate`, is inserted as the first hosted service. In
`IHostedLifecycleService.StartingAsync` it runs every registered `IStartupCheck` inside one scope
and throws one `TellmaCompositionException` carrying every problem, before any other hosted service
starts and before the host serves traffic. When a contributed check cannot be created, the gate
reports that and still runs the built-in checks. `TellmaStartupGate.RunChecksAsync` runs the same checks
for a host that is not started, optionally skipping those with `RequiresInfrastructure`.

`TellmaComposition.Validate` runs the same composition host-free and returns, rather than throws,
every problem `AddTellma` would throw.

### Built-in startup checks

| Check | Fails when | Needs infrastructure |
|---|---|---|
| `core.options` | a bound options type fails its data-annotation validation (one problem per failure) | yes |
| `core.deployment-identity` | the registered `DeploymentIdentity` names another application, or no `ISandboxContext` resolves | no |
| `core.globalization` | the process does not run ICU (invariant mode or Windows NLS) | no |
| `core.key-vault-literals` | a value under `Tellma:` is an unresolved `@Microsoft.KeyVault(` reference | yes |
| `core.public-origin` | `Tellma:PublicOrigin` is missing, relative, more than an origin (a trailing slash included, since derived URIs append `/…` to it), or not https outside Development | yes |
| `core.taxonomy` | in Development, the repository's `taxonomy.json` does not list the slug | no |
| `core.entity-shapes` | the leaf registered for an entity does not implement a shape another feature requires of it; the fix lists the members | no |

Options a feature declares are validated by `core.options` rather than by the host's
`ValidateOnStart`, which would stop startup at the first failing options type before the gate could
report the other problems.

## Blobs: the file-system store

```csharp
builder.Services.AddFileSystemBlobStore();                       // binds Tellma:Blobs:FileSystem
builder.Services.AddFileSystemBlobStore(o => o.RootPath = "/srv/tellma/blobs");
```

`AddFileSystemBlobStore` registers the process's one `IBlobStore`: the default for local development
and for air-gapped on-premises deployments. Tenant `42`'s objects live under `{RootPath}/t42/`, the
object name's `/` becoming the directory separator (`user-image/3f/3fa9….thumb`).

- **Writes are create-only and atomic.** Each object streams to a fresh
  `{RootPath}/t42/.tmp/{guid}.part`, is flushed to disk, and is published under its final name
  without ever replacing one: a non-overwriting move on Windows, a hard link (`link(2)`) then an
  unlink on Unix, so of two writers racing to one name exactly one wins. On Unix the root's file
  system must support hard links; the store probes that before its first write or provisioning and
  fails closed otherwise (`FileSystemBlobStore.ProbeCreateOnlyPublish` is the startup check's seam). A reader sees an object whole or not at all; a crash leaves at most a `.part` file,
  which `PurgeIncompleteAsync` removes once it is older than the cut-off. A content stream that
  yields more or fewer bytes than declared fails the write and leaves nothing.
- **Names are validated twice.** Every name must satisfy `BlobName.IsValid` before storage is
  touched, and every resolved path must stay inside the tenant's directory.
- **Missing storage reads as absent.** A read, delete, list or purge in a tenant whose directory does
  not exist creates nothing; a write creates the directory once and retries once.
- **Reads share deletes.** A read stream is opened with `FileShare.Read | FileShare.Delete`, so the
  sweep can delete an object while a download of it is still streaming.
- **The root.** In Development an unset `RootPath` defaults to `Tellma/{slug}/blobs` under the
  user's local application data folder (`%LOCALAPPDATA%` on Windows, `~/.local/share` on Linux);
  outside Development it must be set. It must be absolute and, on Windows, at most 153 characters,
  so the deepest object path stays within 259. The resolved root is logged (`BlobFileSystemRoot`)
  when the store is created.
- **Telemetry.** `tellma.blobs.store.operations` and `tellma.blobs.store.duration` on the
  `Tellma.Blobs` meter, one measurement per call, tagged with the operation, the outcome and, on a
  write's duration, its size class; never a tenant.

Internally the namespace also holds the upload content-type sniffer (signatures over the
declaration; executables, HTML and SVG never stored) and the file-name sanitiser, which the
staging service uses.

## Layout

| Folder | Contents |
|---|---|
| `Composition/` | `AddTellma`, `TellmaBuilder`, `TellmaComposition.Validate`, `CoreFeature`, `IContributionRealizer<T>`, `RealizationContext`, `TellmaStartupGate`; internally the pipeline, the feature graph, the composition record the checks and later runtimes read, and Core's model contributions. |
| `Composition/Realizers/` | One realizer per item kind the platform understands: services, standalone table types, shape requirements and Core's model configurations. Each spec that adds an item kind adds its realizer to `ContributionRealizerSet.CreateBuiltIn`. |
| `Composition/Checks/` | The built-in startup checks above. |
| `Blobs/` | `FileSystemBlobStore`, its options, default root and validator, `AddFileSystemBlobStore`, the store instruments and log events; internally the content-type sniffer and the file-name sanitiser. |

## Tests

`test/core/Tellma.Core.Tests` (`dotnet test test/core/Tellma.Core.Tests`).
