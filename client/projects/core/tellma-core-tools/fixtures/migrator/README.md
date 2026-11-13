# Stand-in Migrator

The fixture app's `tellma.migrator`. No .NET project lives here: the command specs intercept the
`dotnet run … emit-client-assets <dir>` that `tellma-client emit` starts, and `../fake-emit.ts`
writes what the real emit writes from these files instead: each chunk and string pack as compact
JSON with a final line break, named `<kind>-<hash>.json` after the first 12 Crockford base32
characters of its SHA-256, and the `tellma-metadata.ts` loaders module naming them.

| File | Emitted as |
|---|---|
| `core.json` | The Core chunk: the distribution `fixture`, the features `core` and `fixture`, English and Arabic, and the platform's routing words |
| `entities.json`, `contracts.json` | The Entities and Contracts chunks, empty |
| `strings-en.json`, `strings-ar.json` | The string packs: the boot page's strings, and Arabic's localized display and short names |
