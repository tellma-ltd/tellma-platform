# Tellma.Distro.Acme.Client

Acme's single-page app: the Angular application a distribution writes over the Tellma app framework
(`@tellma/core`). It holds data, not plumbing: its feature, its languages, its manifest, its icons
and one theme delta. Everything else, from the routes to the service worker, the build derives.

| Path | Holds |
|---|---|
| `src/app.config.ts` | `appConfig`: one `provideTellma` call with the emitted metadata, the languages (`tmEnglish()`, the default, then `tmArabic()`) and the features, Acme's last |
| `src/app/acme.feature.ts` | `acmeFeature`, the client side of the server's `acme` feature (`AcmeFeature`): the launcher's Sales section (customers, whose synonyms are `Acme_Customer_Plural_Synonyms`, and sales invoices) and a Financials section standing in for the GL module's; `acme.Customer`'s details page, with its layout and its form's own rules, each loaded on demand; the credit-review page under Financials; `gl.Center` under Administration, standing in for the GL module's own client feature; the sections' icons; and the sales-invoice launcher provider |
| `src/app/invoices/acme-invoice-search.ts` | `AcmeInvoiceSearch`, the launcher's Sales invoices bucket: all the first load carries, since the read loads with the first query |
| `src/app/invoices/invoice-search.ts` | The bucket's read: an invoice by its serial number when the query is digits, else the invoices of the customers whose name, in the content language, holds the query; each row opens the invoices' search filtered to it |
| `src/app/invoices/invoice-search.spec.ts` | Unit tests for the bucket: its declaration, the serial-number and customer-name reads in both languages, and the row mapping |
| `src/app/credit/credit-review-page.ts` | The credit-review page, `/{t}/credit-review`: its title and the threshold the queue lists customers from, the user's own write-behind preference; the queue's rows wait for the credit-exposure report |
| `src/app/customers/customer-details.ts` | The customer details layout, the body of `/{t}/customers/{id}`: one card of the name (a field per tenant language), code, type, currency and credit limit in the platform's layout kit, then the collapsed audit card; each field labels, formats and edits its value from the descriptors, and the host owns the record's lifecycle, modes, header and errors |
| `src/app/customers/customer-details.spec.ts` | The layout's unit test over a fake details context: its fields and collapsed audit card in view mode, a touched empty field's message once in edit mode, and the corporate credit-limit rule |
| `src/app/customers/customer-rules.ts` | The customer form's own rules, which the page applies after the platform's: a corporate customer needs a credit limit; it loads with the layout, so Signal Forms stays out of the first load |
| `src/main.ts` | The entry point: `bootstrapTellma(appConfig)`, which starts the framework's root component in `<tm-app>` |
| `src/index.html` | `<base href="/">`, the manifest, the colour-scheme meta and `tm-boot.js` as the first script |
| `src/styles.css` | The platform's stylesheets and Arabic's fonts, by import, then Acme's one theme delta, `--color-primary` in `@layer tm.theme` |
| `public/` | `manifest.webmanifest`, the favicon and the three PNG icons (192, 512, maskable 512) |
| `generated/` | Gitignored: the client metadata, string packs and loaders module the Migrator emits, and `tellma-entities.d.ts`, the typed entity model every `tsconfig` includes so rows and paths type-check by entity name |
| `vitest-base.config.mjs` | The unit runner's configuration, which the test target names: `@tellma/core-tools`' Vitest preset, whose watchdog ends a run that stops making progress and reports what was pending |
| `Tellma.Distro.Acme.Client.esproj` | The MSBuild project the web project references, so `dotnet publish` builds the app into its `wwwroot` |

Acme ships no brand files of its own, so the build copies the platform's defaults from
`@tellma/core`'s `brand/`. A file of the same name in `public/brand/` would replace one.

## Commands

The project is a member of the repository's pnpm workspace: `pnpm install` at the repository root
installs it. From this folder:

```bash
pnpm start            # tellma-client serve: the dev server, with the server's paths proxied to the web host
pnpm run build        # tellma-client build: emit, checks, ng build, the worker and the output checks
pnpm test             # the unit tests; run a build (or tellma-client emit) first, which writes generated/
pnpm run lint         # ESLint and stylelint with the distribution presets
```

`build` and `serve` run the Migrator's `emit-client-assets` first, so they need the .NET SDK. In the
platform's repository they also write the platform's generated assets (the token stylesheets and the
boot script template), and `tsconfig.json` maps every `@tellma` package to its sources in `client/`;
a distribution in a repository of its own installs the packages instead. `package.json` therefore
names no `@tellma` package but every package their sources import (Angular's, `@jsverse/transloco`,
`lucide`, `@internationalized/date`), as their installed peers and dependencies would be.

## Through MSBuild

The esproj runs `pnpm run build:esproj` (`tellma-client build --no-dotnet-build`, with the build's
configuration) and publishes `dist/acme/browser`. Its reference to the Migrator, without the
assembly, makes MSBuild build the Migrator first, which the emit then runs without building it again.

- `dotnet publish distributions/acme/src/Tellma.Distro.Acme.Web` builds the app and copies its output
  into the web host's `wwwroot`.
- `dotnet build` of the solution does not run the app's build, so it needs no Node; pass
  `-p:ShouldRunBuildScript=true` to build the app with it.
- The esproj runs no install: the repository's `pnpm install` has installed the packages.

## Icons

`public/icons/favicon.svg` is the source of the icons: an ink tile with a white "A". The two square
PNGs are it rendered at their sizes; the maskable one is the same letter at 62% on a full-bleed tile,
inside the safe zone a mask may crop to.
