# tellma-client fixture app

A minimal distribution client, in the layout `tellma-client` expects, that the command specs of
`@tellma/core-tools` build. It is not a workspace package and never ships.

- `package.json` names the Migrator, `../migrator/`, a stand-in: the specs answer the emit's
  `dotnet run … emit-client-assets` themselves, from the JSON files in that folder.
- `angular.json` holds one application, `fixture`, with the build options a distribution uses:
  `serviceWorker: false`, `outputHashing: all`, hidden source maps, `public/**` as assets and
  `generated/assets/**` copied to `tm-metadata/`, and the Latin and Arabic font stylesheets.
- `tsconfig.json` extends the client workspace's, whose path mappings resolve every `@tellma`
  package to its sources, so the fixture builds without the packages being built.
- `src/app.config.ts` exports `appConfig` with one `provideTellma` call, English and Arabic, and
  one feature with one lazy page (`src/app/fixture.feature.ts`).
- `public/` holds the manifest, the three PNG icons at their stated sizes, a favicon and the four
  brand SVGs, so every source check passes.

The specs copy this folder to `../run-<name>/` before building, so the paths relative to the
workspace hold and this folder stays clean; `dist/`, `generated/` and `.angular/` are gitignored.
