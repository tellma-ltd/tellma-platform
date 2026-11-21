# Tellma.Distro.Acme.E2E

The Acme distribution's browser scenarios (Playwright, `Category=E2E`): the client served by the
web host, signed in through the in-process identity engine, driven end to end in Chromium.

It is scaffolded with no tests. The scenarios join with the client's UI; until then the project
builds and runs empty on every pull request, so its references and its place in the solution are
gated from the start.

```bash
dotnet test distributions/acme/test/Tellma.Distro.Acme.E2E --filter "Category=E2E&Category!=Nightly"
```

Once it carries scenarios, install Chromium first through the script the identity suite's
browser tests use (`eng/playwright-browsers/install-browsers.sh`).
