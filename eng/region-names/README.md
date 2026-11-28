# Region names

Generates the `Region_<code>` entries of Tellma.Core's shared resources
(`src/core/Tellma.Core/Resources/Strings.resx` and every `Strings.<language>.resx` satellite): one
entry per region of the language catalogue, holding the region's CLDR display name in the file's
language. The `core.Region` value set labels its values with these keys, so every region a user can
pick has a name in every language Core ships strings for.

The regions are read from the pinned culture data (`src/core/Tellma.Core/Localization/CultureData.txt`),
whose sections name every language and region pair of the catalogue. The names come from the ICU
data bundled with Node.js, through `Intl.DisplayNames`, with bidirectional control characters
removed. The script refuses to run unless that data is CLDR 48, the release the culture data is
pinned to, so every machine and operating system writes the same bytes.

## Running

Node.js 24 bundles CLDR 48. From the repository root, on Windows or Linux:

```bash
node eng/region-names/generate-region-names.mjs
```

The entries are written between two comments at the end of each file and replace whatever names the
file held, inside or outside that block; nothing else in the file changes. A new satellite picks up
its names on the next run.

To confirm that the checked-in files are what the script writes, without changing them:

```bash
node eng/region-names/generate-region-names.mjs --check
```

Exit codes: `0` up to date (or written), `1` a file differs (`--check` only), `2` the running Node.js
does not carry CLDR 48 or an input is unreadable.

## When to run it

- After the pinned culture data is regenerated (`eng/culture-data`), since a region may join or
  leave the catalogue.
- After adding a satellite of Core's shared resources.
- When moving the platform to a new CLDR release: update the release the script pins together with
  the culture data's, then regenerate both.

The names are never edited by hand; a correction belongs in CLDR or in a deliberate override added
to the script.
