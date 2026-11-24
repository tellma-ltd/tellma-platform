# Tellma.Distro.Acme.Web

Acme's web host. `Program.cs` holds exactly the platform's three calls, `AddTellma`, `UseTellma` and
`MapTellma`; everything the host serves comes from the platform and from the two composition
methods it passes:

- `AcmeComposition.Compose`, which the migrator runs too;
- `AcmeWebComposition.Compose`, the web host's own half: the Azure web defaults (webhooks, the key
  ring in Blob storage under a Key Vault key, the Azure Monitor exporter) and, in Development only,
  the provisioning trigger that runs the migrator from this checkout for a tenant the admin surface
  registers, and the development mail drop below.

`appsettings.json` holds what every environment shares and `appsettings.Development.json` the local
defaults: the LocalDB catalog `Tellma.dev.acme.catalog`, the in-process identity engine with
development keys, and the two seed tenants. The migrator reads the same two files. The tracked
`Properties/launchSettings.json` starts the host on `https://localhost:7052` with the SPA proxy on;
the public origin is the client dev server's, `https://localhost:4200`. A worktree's
`.dev-ports.local` overrides both ports in Development.

```bash
dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web
```

The project references the client app, `Tellma.Distro.Acme.Client`, without its assembly:

- **Development.** The SPA proxy (`Microsoft.AspNetCore.SpaProxy`, which only the launch profile's
  `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` turns on) runs `pnpm start` in the client project and sends
  the browser to the dev server once it answers. The proxy's target is computed at build from
  `ACME_CLIENT` in `.dev-ports.local`, else 4200, the port the dev server listens on.
  `dotnet run --no-launch-profile` runs Development without it.
- **Publish.** `dotnet publish` builds the client (`tellma-client build`) and copies its output into
  `wwwroot`, which `MapTellma` serves. It needs Node and the repository's `pnpm install`.

The host publishes framework-dependent (`SelfContained` and `PublishSingleFile` off): the PDF
thumbnail worker runs through `dotnet exec` on the host's runtime configuration.

## The development mail drop

The browser suite (`test/Tellma.Distro.Acme.E2E`) reads sign-in codes from the mail the host sends.
For that the host offers one more email transport, `development-drop`, which writes each message to
a directory as one JSON file (its addresses, subject, bodies and attachment sizes) and delivers
nothing:

```bash
Email__Provider=development-drop Email__DevelopmentDrop__Directory=/absolute/drop/folder
```

It runs in Development only. A startup check refuses either setting in any other environment, and
in Development refuses the provider without an absolute directory, or a directory without the
provider; the sender itself refuses to start outside Development. Without these settings Development
keeps the log sink, which writes each message to the console.
