// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmTokens } from './contract/tokens';
import { tmEmitCss, tmEmittedSchemeVars, tmRefToVarName, tmTokenValueToCss } from './emit/emit-css';
import { tmTokensDefault } from './presets/tellma-default';
import { tmResolveVar, tmValidateTokens } from './schema/validate';

describe('tmRefToVarName', () => {
  it('maps primitive and semantic paths to the brand variable names', () => {
    expect(tmRefToVarName('teal.500')).toBe('--teal-500');
    expect(tmRefToVarName('grey.25')).toBe('--grey-25');
    expect(tmRefToVarName('white')).toBe('--white');
    expect(tmRefToVarName('radius.sm')).toBe('--radius-sm');
    expect(tmRefToVarName('space.4')).toBe('--space-4');
    expect(tmRefToVarName('font.size.sm')).toBe('--text-sm');
    expect(tmRefToVarName('font.sans')).toBe('--font-sans');
    expect(tmRefToVarName('motion.durationFast')).toBe('--duration-fast');
    expect(tmRefToVarName('motion.easeInOut')).toBe('--ease-in-out');
    expect(tmRefToVarName('text.strong')).toBe('--text-strong');
    expect(tmRefToVarName('action.primary')).toBe('--color-primary');
    expect(tmRefToVarName('action.onPrimary')).toBe('--color-on-primary');
    expect(tmRefToVarName('action.accent')).toBe('--accent');
    expect(tmRefToVarName('status.error.fg')).toBe('--error');
    expect(tmRefToVarName('status.error.bg')).toBe('--error-bg');
    expect(tmRefToVarName('field.bgDisabled')).toBe('--field-bg-disabled');
    expect(tmRefToVarName('formField.heightSm')).toBe('--field-height-sm');
    expect(tmRefToVarName('focusRing.color')).toBe('--focus-ring-color');
  });

  it('turns refs into var() and passes literals through', () => {
    expect(tmTokenValueToCss('{teal.600}')).toBe('var(--teal-600)');
    expect(tmTokenValueToCss('#FEFEFE')).toBe('#FEFEFE');
    expect(tmTokenValueToCss('rgba(255, 255, 255, 0.12)')).toBe('rgba(255, 255, 255, 0.12)');
  });
});

describe('tmEmitCss', () => {
  const css = tmEmitCss(tmTokensDefault);

  it('starts every sheet with the canonical layer order statement', () => {
    const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '');
    const firstStatement = withoutComments.split('\n').find((l) => l.trim());
    expect(firstStatement?.trim()).toBe('@layer tm.base, tm.theme;');
  });

  it('emits both schemes inside @layer tm.base', () => {
    expect(css).toContain('@layer tm.base {');
    expect(css).toContain(':root {');
    expect(css).toContain('[data-theme=dark] {');
    expect(css).toContain('color-scheme: light;');
    expect(css).toContain('color-scheme: dark;');
  });

  it('reproduces the brand anchors', () => {
    expect(css).toContain('--teal-400: #4CA0B6;'); // canonical logo teal
    expect(css).toContain('--ink-900: #001722;'); // canonical wordmark ink
    expect(css).toContain('--field-height: 38px;');
    expect(css).toContain('--field-height-sm: 30px;');
    expect(css).toContain('--field-height-lg: 46px;');
    expect(css).toContain('--field-border-focus: var(--teal-500);');
    expect(css).toContain('--focus-ring-color: var(--teal-500);'); // spec §4, not the stale teal-400
    expect(css).toContain('--color-primary: var(--teal-600);');
    expect(css).toContain('--grey-900: var(--ink-900);');
  });

  it('emits --font-ui as the single multi-script stack, with no direction coupling', () => {
    // Ordered-contains, not exact-equals: adding faces later must not break
    // this — the invariants are brand faces before generics and no exact
    // stack pinning.
    const stack = /--font-ui: (.*);/.exec(css)?.[1] ?? '';
    const order = ["'Noto Sans'", "'Noto Sans Arabic'", 'sans-serif'].map((f) =>
      stack.indexOf(f),
    );
    expect(order.every((i) => i >= 0)).toBe(true);
    expect([...order].sort((a, b) => a - b)).toEqual(order);
    // Typography never keys on direction: per-glyph face selection comes from
    // the stack + unicode-range, leading from :lang() below.
    expect(css).not.toContain('[dir=');
  });

  it('emits language-keyed leading at explicit lang roots: set, reset, AND apply', () => {
    // [lang]:lang(x), not bare :lang(x): the rule must hit only elements
    // explicitly MARKED with a lang attribute and inherit below — a bare
    // :lang() matches every element via language inheritance and would pin
    // each one to the tm.base value, defeating inheritance-based app
    // overrides (body { line-height: 1.5 } would win on body alone).
    // Each block re-points the variable AND applies line-height: the
    // property inherits by computed value, so without the application a
    // lang island below the root would keep its parent's leading.
    expect(css).toMatch(
      /\[lang\]:lang\(ar\) \{\n\s*--leading-ui: var\(--leading-arabic\);\n\s*line-height: var\(--leading-ui\);/,
    );
    // The en rule restores the body leading, so a lang="en" island inside an
    // Arabic page snaps back instead of inheriting 1.9.
    expect(css).toMatch(
      /\[lang\]:lang\(en\) \{\n\s*--leading-ui: var\(--leading-body\);\n\s*line-height: var\(--leading-ui\);/,
    );
    expect(css).not.toMatch(/[^\]]:lang\(/); // no bare :lang() anywhere
    // Source order beats the specificity tie on <html>, so the lang blocks
    // must come after the :root block.
    expect(css.indexOf('[lang]:lang(ar)')).toBeGreaterThan(css.indexOf(':root {'));
  });

  it('emits the ::selection rule with per-scheme teal highlight tokens', () => {
    expect(css).toMatch(/::selection \{\n\s*background-color: var\(--selection-bg\);\n\s*color: var\(--selection-text\);/);
    const light = tmEmittedSchemeVars(tmTokensDefault, 'light');
    expect(light.get('--selection-bg')).toBe('var(--teal-100)');
    const dark = tmEmittedSchemeVars(tmTokensDefault, 'dark');
    expect(dark.get('--selection-bg')).toBe('var(--teal-800)');
  });

  it('primitives never lie: identical in both schemes; dark is all semantic', () => {
    const dark = tmEmittedSchemeVars(tmTokensDefault, 'dark');
    const light = tmEmittedSchemeVars(tmTokensDefault, 'light');
    // --white is always white, --grey-900 always the darkest grey.
    expect(dark.get('--white')).toBe('#FEFEFE');
    expect(dark.get('--white')).toBe(light.get('--white'));
    expect(dark.get('--grey-900')).toBe(light.get('--grey-900'));
    // The dark appearance flows entirely through the semantic roles.
    expect(tmResolveVar(dark, '--field-text')).toBe('#F2F7F8');
    expect(tmResolveVar(dark, '--field-bg')).toBe('#16252D');
    expect(tmResolveVar(light, '--field-text')).toBe('#001722');
  });
});

describe('tmValidateTokens (missing-ref gate)', () => {
  it('passes the default preset', () => {
    expect(tmValidateTokens(tmTokensDefault)).toEqual([]);
  });

  it('fails on a missing token reference', () => {
    const broken: TmTokens = {
      ...tmTokensDefault,
      component: { badComponent: { color: '{does.not.exist}' } },
    };
    const issues = tmValidateTokens(broken);
    expect(issues.some((i) => i.gate === 'missing-ref')).toBe(true);
  });

  it('fails on a leadingByLang ref to a missing token', () => {
    const broken: TmTokens = {
      ...tmTokensDefault,
      semantic: {
        ...tmTokensDefault.semantic,
        leadingByLang: {
          ...tmTokensDefault.semantic.leadingByLang,
          he: '{font.leading.hebrew}',
        },
      },
    };
    const issues = tmValidateTokens(broken);
    expect(
      issues.some((i) => i.gate === 'missing-ref' && i.message.includes('leadingByLang.he')),
    ).toBe(true);
  });
});
