# Tellma.Distro.Acme.Web.Tests

Unit tests of the Acme distribution's shape, needing no database:

- the web host and the migrator build byte-identical tenant models;
- `emit-client-assets` exits 0 with no database reachable;
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
