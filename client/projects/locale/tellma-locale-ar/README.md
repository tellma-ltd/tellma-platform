# @tellma/locale-ar

The Arabic locale pack — and the template every future locale pack copies.

One provider wires everything:

```ts
providers: [provideTellmaUi(), provideTellmaLocaleAr()]
```

## What the pack contributes

- **Strings** — the library's built-in messages in Arabic
  (`strings-ar.ts`), merged into Transloco's `ar` resources under the shared
  `tmUi` namespace. Plurals carry the full Arabic ICU categories
  (one/two/few/many/other); imperative verbs conjugate for the addressee via
  the ambient `{gender}` parameter (see below).
- **Fonts** — self-hosted Noto Sans Arabic woff2 with an Arabic
  `unicode-range`, so the face downloads only when Arabic glyphs render.
  `fonts/fonts.css` carries the `@font-face`; OFL.txt ships alongside.

The consuming app adds the pack's stylesheet to its build's `styles` array
(the pipeline fingerprints the binary like any other asset):

```jsonc
"styles": [..., "node_modules/@tellma/locale-ar/fonts/fonts.css"]
```

## Gendered strings

Arabic imperatives differ by addressee (أدخل / أدخلي). The strings branch on
the ambient `{gender}` ICU parameter supplied by `TM_UI_MESSAGE_CONTEXT`
(default `other` — the base form). A distribution provides one signal, e.g.
mapped from the user profile, and every visible string re-renders when it
changes.

## Writing a new pack

Copy this package's structure:

1. `strings-xx.ts` — translate every key of `TM_UI_STRINGS_EN`; keep the ICU
   categories your language needs; branch on `{gender}` only where grammar
   requires it.
2. Vendor the script's font subsets (see `tools/fonts/vendor-fonts-ar.mjs`)
   if the language needs a non-Latin face; skip fonts entirely for
   Latin-script locales.
3. `provide-tellma-locale-xx.ts` — register the strings.
4. If the script's leading differs from Latin, add the language to the token
   preset's `leadingByLang`; if it needs a new face, add the family to the
   preset's `font.ui` stack.
