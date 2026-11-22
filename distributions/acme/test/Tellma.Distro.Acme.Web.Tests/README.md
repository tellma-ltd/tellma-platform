# Tellma.Distro.Acme.Web.Tests

Unit tests of the Acme distribution's shape, needing no database:

- the web host, built as `Program.cs` builds it, serves the runtime tenant model the migrator's
  design-time factory migrates, compared in full;
- `emit-client-assets` exits 0 with no database reachable, writes the same names and bytes every
  time and under any culture, carries the distribution version the web host reports (the composition library's, with
  Acme's feature last at the same version), and emits every list the client reads, empty where no
  contributor writes it;
- the endpoint audit passes over every endpoint the web host maps, in Development and in
  Production, the webhook endpoint among them;
- no `src/` project references `distributions/`, and the web host's references reach the identity
  engine but never `Tellma.Identity.Migrations` or a design-time package, and the web host's build
  output holds no file of the Migrator the client app's build references;
- no tracked configuration file, the client app's JSON included, holds a secret;
- the SPA proxy's port is read from `.dev-ports.local` as the web host reads it: the last
  `ACME_CLIENT` line wins;
- `Program.cs` of each host holds exactly the platform's calls;
- the collation pin over Acme's tenant migrations;
- the Development provisioning trigger's process and its Production replacement.
- the resource audit over Acme's strings: every key derived from the web host's composition,
  spelled in the distribution's source or pending, every Arabic key declared in English, and every
  label Acme's model derives in its own families shipped;
- the development data's shape: the 40-center tree on four levels with its inactive and invoiced
  lists, the customers and invoices, the step's name slots and key filter, and the development
  identity server seeding every principal the sample data binds, at its address and subject.

```bash
dotnet test distributions/acme/test/Tellma.Distro.Acme.Web.Tests
```
