# Tellma.Defaults.Azure

The Azure deployment's common composition: one call a distribution's composition method makes, so
its web host and its migrator compose the same Azure-hosted services.

```csharp
public static void Compose(TellmaBuilder tellma) =>
    tellma.UseAzureDefaults().Languages(["en-GB", "ar-SA"]);
```

`UseAzureDefaults()` composes:

| Concern | What it registers |
|---|---|
| Email | The email pipeline (`AddTellmaEmail()`), with the Azure Communication Services transport (`acs-email`) and the SendGrid transport (`sendgrid`) beside the Development log sink. `Email:Provider` chooses the sender; each transport reads its own section (`Email:AcsEmail`, `Email:SendGrid`). |
| Blobs | The Skia image processor (`AddTellmaSkia()`), which normalises uploaded images and renders thumbnails, and exactly one blob store, chosen below. |

The blob store is chosen once, at composition:

| `Tellma:Blobs:Azure` | Environment | Store |
|---|---|---|
| present | any | The Azure blob store (`AddAzureBlobStore`), bound from the section: `ServiceUri` in a deployment, or a `ConnectionString` for Azurite. |
| absent | Development | The file-system store (`AddFileSystemBlobStore()`), rooted under the user's local application data unless `Tellma:Blobs:FileSystem:RootPath` says otherwise. No Docker and no storage account are needed. |
| absent | any other | The Azure blob store with nothing configured, so its `blobs.azure` startup check fails the host before it serves traffic, rather than the host running without durable blobs. |

The Azure store reaches its account with the host's registered `TokenCredential`: the App Service's
managed identity, which needs *Storage Blob Data Contributor* on the account.

## Rules

- **A distribution-layer package.** It references the connectors it composes; no Core package
  references it, and it never references an ASP.NET Core package, so the migrator composes it
  without the web stack. The web host's half is `Tellma.Defaults.Azure.AspNetCore`.
- **Idempotent.** A second call changes nothing.
- **Replaceable.** A distribution that wants a different sender composes the pipeline and its
  transports itself instead of calling the bundle.
- **No secrets in configuration files.** The transports authenticate with the host's
  `TokenCredential` (a managed identity in Azure) or read their keys from the deployment's secret
  store; tracked configuration names no key.

## Tests

`test/defaults/Tellma.Defaults.Azure.Tests` pins the registrations of both Azure bundles.
