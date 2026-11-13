# Tellma.Distro.Acme.Web

Acme's web host. `Program.cs` holds exactly the platform's three calls, `AddTellma`, `UseTellma` and
`MapTellma`; everything the host serves comes from the platform and from the two composition
methods it passes:

- `AcmeComposition.Compose`, which the migrator runs too;
- `AcmeWebComposition.Compose`, the web host's own half: the Azure web defaults (webhooks, the key
  ring in Blob storage under a Key Vault key, the Azure Monitor exporter) and, in Development only,
  the provisioning trigger that runs the migrator from this checkout for a tenant the admin surface
  registers.

`appsettings.json` holds what every environment shares and `appsettings.Development.json` the local
defaults: the LocalDB catalog `Tellma.dev.acme.catalog`, the in-process identity engine with
development keys, and the two seed tenants. The migrator reads the same two files. The tracked
`Properties/launchSettings.json` starts the host on `https://localhost:7052`; a worktree's
`.dev-ports.local` overrides the port and the public origin in Development.

```bash
dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web
```

The host publishes framework-dependent (`SelfContained` and `PublishSingleFile` off): the PDF
thumbnail worker runs through `dotnet exec` on the host's runtime configuration.
