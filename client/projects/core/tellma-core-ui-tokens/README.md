# @tellma/core-ui-tokens

The design-token layer: a typed `TmTokens` contract, the brand default
preset, a tokens→CSS emitter, and a shipped validation gate.

- **Contract + preset** — three tiers (primitive ramps → semantic roles →
  component variables), light and dark as two instances of the same scheme
  shape, one multi-script font stack, language-keyed leading.
- **Emitter** — `tmEmitCss(tokens)` produces a static stylesheet; every sheet
  opens with `@layer tm.base, tm.theme;` so load order can never change
  which layer wins. A distribution themes by emitting its delta into
  `tm.theme`, or at runtime via `setProperty` (inline styles beat both
  layers).
- **Gate** — `tmValidateTokens(tokens)` runs at build time *and* ships as
  runtime code (for admin-authored token documents): every emitted `var()`
  reference must resolve within its scheme, including the `:lang()` leading
  map. Color-contrast accessibility is exercised by the axe browser battery
  over the rendered components, not by token validation.
- **Tokens as data** — `generated/tm-tokens.schema.json` (shipped in the
  package) is the language-neutral JSON Schema of the contract, for
  validating token documents that arrive as data rather than TypeScript.

A non-theming app needs only the emitted `css/tellma-default.css` added to
its styles — the TypeScript entry point stays out of the bundle unless
imported.
