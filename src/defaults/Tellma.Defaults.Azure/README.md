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

The blob store and the image processor join the bundle with the blob storage work.

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
