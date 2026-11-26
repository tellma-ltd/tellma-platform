# @tellma/locale-ar

The Arabic locale pack — and the template every future locale pack copies.

A Tellma app offers Arabic with its language definition, and imports the
pack's stylesheet after the platform's:

```ts
provideTellma({ metadata: tellmaMetadata, languages: [tmEnglish(), tmArabic()], features: [] });
```

```css
@import '@tellma/core/styles.css';
@import '@tellma/locale-ar/styles.css';
```

An application on the component library alone wires the pack with one
provider instead:

```ts
providers: [provideTellmaUi(), provideTellmaLocaleAr()]
```

## What the pack contributes

- **Strings** — the library's built-in messages in Arabic
  (`strings/strings-ar.ts`, the `@tellma/locale-ar/strings` entry point),
  merged into Transloco's `ar` resources under the shared `tmUi` namespace,
  and the platform's own (`platform-strings/strings-tm-ar.ts`, under `tm`, the
  `@tellma/locale-ar/platform-strings` entry point, with the shell's part, the
  shell frame's, the tenantless shell's, the tenant picker's, the
  signed-out page's, the tenant app bar's, the session menu's, the markers'
  and the About and Shortcuts dialogs' strings, in `shell-strings/strings-shell-ar.ts`, the
  `@tellma/locale-ar/shell-strings` entry point, the details page's part,
  its header's buttons and badges, status lines and discard question, in
  `details-strings/strings-details-ar.ts`, the
  `@tellma/locale-ar/details-strings` entry point, and the Queryex tools' part,
  the expression editor's and the path picker's strings, in
  `queryex-strings/strings-queryex-ar.ts`, the
  `@tellma/locale-ar/queryex-strings` entry point), which `tmArabic()` merges
  and hands the platform. `tmArabic()` loads the five entry points at once on
  the first switch to Arabic, so an app's other languages never download
  them, and an app on the component library alone never downloads the
  platform's tables.
  Plurals carry the full Arabic ICU
  categories (one/two/few/many/other); imperative verbs conjugate for the
  addressee via the ambient `{gender}` parameter (see below).
- **Fonts** — self-hosted Noto Sans Arabic woff2 with an Arabic
  `unicode-range`, so the face downloads only when Arabic glyphs render.
  `fonts/fonts.css` carries the `@font-face`, `styles.css` imports it, and
  OFL.txt ships alongside; the pipeline fingerprints the binary like any
  other asset.

## Gendered strings

Arabic imperatives differ by addressee (أدخل / أدخلي). The strings branch on
the ambient `{gender}` ICU parameter supplied by `TM_UI_MESSAGE_CONTEXT`
(default `other` — the base form). A distribution provides one signal, e.g.
mapped from the user profile, and every visible string re-renders when it
changes.

## Writing a new pack

Copy this package's structure:

1. `strings/strings-xx.ts` — translate every key of `TM_UI_STRINGS_EN` and of the
   tables entry points ship beside it (`TM_PIVOT_STRINGS_EN` under `pivot`,
   `TM_TILES_TREE_STRINGS_EN` under `tilesTree`, `TM_CHART_STRINGS_EN` under
   `chart`); keep the ICU categories
   your language needs; branch on `{gender}` only where grammar requires it.
2. Vendor the script's font subsets (see `tools/fonts/vendor-fonts-ar.mjs`)
   if the language needs a non-Latin face; skip fonts entirely for
   Latin-script locales.
3. `platform-strings/strings-tm-xx.ts` — translate every key of the platform's English
   `tm` table (`tmEnglish()` in `@tellma/core`); `shell-strings/strings-shell-xx.ts` —
   the `shell`, `tenantless`, `tenantShell`, `shellOverlays`, `shellHistory`, `aboutDialog` and
   `shortcutsDialog` scopes the shell frame, the tenantless pages and the tenant
   shell register in English; `details-strings/strings-details-xx.ts` — the `details`
   scope the details page registers in English; `queryex-strings/strings-queryex-xx.ts` — the `queryex`
   and `pathPicker` scopes the expression editor and the path picker register in
   English.
4. `provide-tellma-locale-xx.ts` — register the strings; `tm-xx.ts` — the
   language definition `tmXx()`, with its direction and its tables, loaded
   together with `import()` from the `platform-strings`, `shell-strings`,
   `details-strings`, `queryex-strings` and `strings` entry points;
   `styles.css` — import the pack's fonts.
5. If the script's leading differs from Latin, add the language to the token
   preset's `leadingByLang`; if it needs a new face, add the family to the
   preset's `font.ui` stack.
