# Tellma.Distro.Acme.Client

Acme's single-page app: the Angular application a distribution writes over the Tellma app framework
(`@tellma/core`). It holds data, not plumbing: its feature, its languages, its manifest, its icons
and one theme delta. Everything else, from the routes to the service worker, the build derives.

| Path | Holds |
|---|---|
| `src/app.config.ts` | `appConfig`: one `provideTellma` call with the emitted metadata, the languages (English, the default, then Arabic) and the features, Acme's last |
| `src/app/acme.feature.ts` | `acmeFeature`, the client side of the server's `acme` feature (`AcmeFeature`); it declares nothing yet |
| `src/main.ts` | The entry point: `bootstrapTellma(appConfig)`, which starts the framework's root component in `<tm-app>` |
| `src/index.html` | `<base href="/">`, the manifest, the colour-scheme meta and `tm-boot.js` as the first script |
| `src/styles.css` | Acme's one theme delta, `--color-primary` in `@layer tm.theme` |
| `public/` | `manifest.webmanifest`, the favicon and the three PNG icons (192, 512, maskable 512) |
| `generated/` | Gitignored: the client metadata, string packs and loaders module the Migrator emits, and `tellma-entities.d.ts`, the typed entity model every `tsconfig` includes so rows and paths type-check by entity name |
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

## Stand-ins

Two pieces of the framework that a distribution's scaffold names are not there yet, so the app
stands in for them, each in the place the scaffold puts it:

- `app.config.ts` defines English and Arabic with empty client string tables, in place of
  `tmEnglish()` and `tmArabic()`.
- `angular.json` lists the platform's stylesheets (the tokens, the fonts of Latin and Arabic, the
  directive and boot-page styles) before `src/styles.css`, in place of `src/styles.css` importing
  `@tellma/core/styles.css` and `@tellma/locale-ar/styles.css`.

## Icons

`public/icons/favicon.svg` is the source of the icons: an ink tile with a white "A". The two square
PNGs are it rendered at their sizes; the maskable one is the same letter at 62% on a full-bleed tile,
inside the safe zone a mask may crop to.
