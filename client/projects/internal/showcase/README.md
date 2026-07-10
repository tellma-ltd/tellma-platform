# showcase (internal)

The component showcase and browser-test host. Never published.

- Every component has a story page addressable as `/story/<id>`, in any
  combination of `?dir=ltr|rtl` and `?theme=light|dark` — the Playwright
  suite's addressing scheme.
- The persistent header offers the story menu and live light/dark + EN/AR
  toggles on every page; the URL stays the source of truth for appearance.
- It doubles as the reference consumer wiring: `angular.json` (token
  stylesheet + font asset globs), `index.html` (font stylesheets), and
  `app.config.ts` (providers + font preload injection) show exactly what a
  distribution sets up.

```bash
pnpm start        # dev server on a worktree-local port
pnpm run e2e      # the Playwright/axe suite against this app
```
