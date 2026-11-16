# Tellma.Distro.Acme.Web.Tests

Unit tests of the Acme distribution's shape, needing no database:

- the web host, built as `Program.cs` builds it, serves the runtime tenant model the migrator's
  design-time factory migrates, compared in full;
- `emit-client-assets` exits 0 with no database reachable, writes the same names and bytes every
  time, carries the distribution version the web host reports (the composition library's, with
  Acme's feature last at the same version), and emits every list the client reads, empty where no
  contributor writes it;
- the endpoint audit passes over every endpoint the web host maps, in Development and in
  Production, the webhook endpoint among them;
- no `src/` project references `distributions/`, and the web host's references reach the identity
  engine but never `Tellma.Identity.Migrations` or a design-time package;
- no tracked configuration file holds a secret;
- `Program.cs` of each host holds exactly the platform's calls;
- the collation pin over Acme's tenant migrations;
- the Development provisioning trigger's process and its Production replacement.

```bash
dotnet test distributions/acme/test/Tellma.Distro.Acme.Web.Tests
```
