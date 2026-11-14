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
    expect(tmRefToVarName('focusRing.haloWidth')).toBe('--focus-ring-halo-width');
    // Two adjacent capitals do NOT split: the padding variants are spelled
    // the way they emit, matching --button-padding-xsm.
    expect(tmRefToVarName('formField.paddingXsm')).toBe('--field-padding-xsm');
    expect(tmRefToVarName('categorical.3.onTint')).toBe('--categorical-3-on-tint');
    expect(tmRefToVarName('categorical.8.mark')).toBe('--categorical-8-mark');
    expect(tmRefToVarName('sequential.4')).toBe('--sequential-4');
    // A digit run after a letter starts a word of its own, as a capital does.
    expect(tmRefToVarName('entityPicker.optionHeight2line')).toBe(
      '--entity-picker-option-height-2line',
    );
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

  it('emits a complete size ladder: four properties, three steps', () => {
    // Shrinking the box alone leaves a dense field looking like a squeezed
    // comfortable one, so every step restates height, control font size,
    // inline padding and the label gap.
    const ladder = [
      '--field-height-sm: 30px;',
      '--field-height-lg: 46px;',
      '--field-font-size-sm: 13px;',
      '--field-font-size-lg: 15px;',
      '--field-padding-xsm: 10px;',
      '--field-padding-xlg: 14px;',
      '--field-label-gap-sm: 4px;',
      '--field-label-gap-lg: 7px;',
    ];
    for (const declaration of ladder) {
      expect(css).toContain(declaration);
    }
  });

  it('emits the icon box, its five steps and the 1.75 stroke, and the 18px key cap', () => {
    // These are the names tm-icon, tm-kbd and the containers that size icons
    // read; a renamed key would leave those declarations silently dropped.
    for (const declaration of [
      '--icon-size: 16px;',
      '--icon-size-xs: 12px;',
      '--icon-size-sm: 14px;',
      '--icon-size-md: 16px;',
      '--icon-size-lg: 20px;',
      '--icon-size-xl: 24px;',
      '--icon-stroke-width: 1.75;',
      '--kbd-height: 18px;',
      '--kbd-min-width: 18px;',
    ]) {
      expect(css).toContain(declaration);
    }
  });

  it('emits the status and identity indicators’ geometry at the sizes they are drawn at', () => {
    // The names tm-badge, tm-counter, tm-avatar, tm-progress-bar,
    // tm-progress-ring, tm-skeleton and tm-empty-state read. The avatar
    // diameters are also the rendition sizes tm-avatar requests from
    // tm-image, and the progress hold is read by script.
    for (const declaration of [
      '--badge-height: 20px;',
      '--badge-dot-size: 6px;',
      '--counter-height: 16px;',
      '--counter-min-width: 16px;',
      '--counter-halo: var(--surface-card);',
      '--counter-halo-width: 2px;',
      '--avatar-size-xs: 20px;',
      '--avatar-size-sm: 24px;',
      '--avatar-size-md: 28px;',
      '--avatar-size-lg: 40px;',
      '--avatar-size-xl: 72px;',
      '--avatar-bg: var(--accent);',
      '--progress-height: 4px;',
      '--progress-min-visible: 250ms;',
      '--progress-sweep-duration: 1.4s;',
      '--progress-ring-stroke: 1.5px;',
      '--skeleton-bar-height: 8px;',
      '--skeleton-last-line-width: 60%;',
      '--empty-state-icon-size: 22px;',
      '--empty-state-detail-max-width: 42ch;',
    ]) {
      expect(css).toContain(declaration);
    }
  });

  it('emits the chip ladder and the feedback surfaces’ geometry at the sizes they are drawn at', () => {
    // The names tm-chip, tm-chip-set, the toast host, tm-banner-bar and the
    // busy overlay read. The chip group is public to read, the toast offset
    // and the busy delay are set by consumers, and the busy delay is read by
    // script.
    for (const declaration of [
      '--chip-height: 24px;',
      '--chip-height-sm: 20px;',
      '--chip-height-lg: 28px;',
      '--chip-padding-xsm: 8px;',
      '--chip-padding-xlg: 12px;',
      '--chip-radius: var(--radius-full);',
      '--chip-target-size: 24px;',
      '--toast-width: 360px;',
      '--toast-offset-block-start: 8px;',
      '--toast-close-size: 28px;',
      '--banner-height: 32px;',
      '--banner-button-size: 24px;',
      '--busy-overlay-delay: 200ms;',
      '--busy-overlay-scrim: rgba(4, 18, 24, 0.4);',
    ]) {
      expect(css).toContain(declaration);
    }
  });

  it('emits the dialog and layout pieces’ geometry at the sizes they are drawn at', () => {
    // The names tm-card, tm-page-header, the description list, tm-splitter
    // and the alert dialogs read: the card header is a 33px band (8px
    // padding around a 17px line), the splitter bar 7px with a 24px target.
    for (const declaration of [
      '--card-header-padding-x: 11px;',
      '--card-header-padding-y: 8px;',
      '--card-line-height: 17px;',
      '--card-subtitle-line-height: 18px;',
      '--card-dot-size: 6px;',
      '--page-header-padding-x: 14px;',
      '--page-header-padding-y: 12px;',
      '--page-header-padding-narrow: 10px;',
      '--page-header-title-font-size: 16px;',
      '--description-list-label-font-size: 12px;',
      '--description-list-value-font-size: 13px;',
      '--description-list-row-gap: 10px;',
      '--description-list-column-gap: 18px;',
      '--splitter-size: 7px;',
      '--splitter-hit-size: 24px;',
      '--splitter-grip-width: 3px;',
      '--splitter-grip-length: 28px;',
      '--modal-alert-icon-size: 26px;',
    ]) {
      expect(css).toContain(declaration);
    }
  });

  it('emits the form controls’ geometry at the sizes they are drawn at', () => {
    // The names tm-radio-group, tm-segmented, tm-switch and the date-time
    // fields read: a 16px circle, segments inset 2px from their track, a
    // 28×16 switch track with a 12px knob, and a 112px time input.
    for (const declaration of [
      '--radio-circle-size: 16px;',
      '--radio-dot-size: 8px;',
      '--radio-gap: 8px;',
      '--radio-inline-gap: 16px;',
      '--segmented-inset: 2px;',
      '--segmented-icon-gap: 4px;',
      '--segmented-icon-size: 16px;',
      '--switch-track-width: 28px;',
      '--switch-track-height: 16px;',
      '--switch-knob-size: 12px;',
      '--switch-gap: 8px;',
      '--date-time-time-width: 112px;',
      '--date-time-gap: 8px;',
    ]) {
      expect(css).toContain(declaration);
    }
  });

  it('carries the focus halo as a semantic role, so it flips with the scheme', () => {
    // The brand sheet gets its dark halo by re-mapping the teal-50 PRIMITIVE.
    // Primitives are scheme-invariant here, so the halo is a field role
    // instead: opaque tint on light, translucent teal on dark.
    const light = tmEmittedSchemeVars(tmTokensDefault, 'light');
    const dark = tmEmittedSchemeVars(tmTokensDefault, 'dark');
    expect(tmResolveVar(light, '--field-focus-halo')).toBe('#EAF4F7');
    expect(dark.get('--field-focus-halo')).toBe('rgba(76, 160, 182, 0.22)');
    expect(dark.get('--teal-50')).toBe(light.get('--teal-50'));
  });

  it('emits --font-ui as the single multi-script stack, with no direction coupling', () => {
    // Ordered-contains, not exact-equals: adding faces later must not break
    // this — the invariants are brand faces before generics and no exact
    // stack pinning.
    const stack = /--font-ui: (.*);/.exec(css)?.[1] ?? '';
    const order = ["'Noto Sans'", "'Noto Sans Arabic'", 'sans-serif'].map((f) => stack.indexOf(f));
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
    expect(css).toMatch(
      /::selection \{\n\s*background-color: var\(--selection-bg\);\n\s*color: var\(--selection-text\);/,
    );
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

/** WCAG 2 relative luminance of a `#RRGGBB` colour. */
function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const channel = parseInt(hex.slice(i, i + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** WCAG 2 contrast ratio between two `#RRGGBB` colours. */
function contrast(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

/** The `#RRGGBB` channels of a colour, 0-255. */
function channels(hex: string): number[] {
  return [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
}

/** Formats 0-255 channels as `#RRGGBB`. */
function toHex(rgb: readonly number[]): string {
  return `#${rgb.map((c) => Math.round(c).toString(16).padStart(2, '0')).join('')}`.toUpperCase();
}

/** A `#RRGGBB` colour, or an `rgba()` one composited over the opaque `base`. */
function opaque(color: string, base: string): string {
  const rgba = /^rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)$/.exec(color);
  if (!rgba) {
    return color;
  }
  const alpha = Number(rgba[4]);
  const under = channels(base);
  return toHex([1, 2, 3].map((i, k) => Number(rgba[i]) * alpha + under[k] * (1 - alpha)));
}

/**
 * The `#RRGGBB` a resolved fill paints: a colour as is, or a
 * `color-mix(in srgb, var(--a) P%, var(--b))` mixed channel by channel.
 */
function paint(value: string, vars: Map<string, string>): string {
  const mix = /^color-mix\(in srgb, var\((--[a-z-]+)\) (\d+)%, var\((--[a-z-]+)\)\)$/.exec(value);
  if (!mix) {
    return value;
  }
  const share = Number(mix[2]) / 100;
  const [a, b] = [mix[1], mix[3]].map((name) => channels(tmResolveVar(vars, name)!));
  return toHex(a.map((c, k) => c * share + b[k] * (1 - share)));
}

describe('the categorical and sequential colour groups', () => {
  const css = tmEmitCss(tmTokensDefault);
  const SLOTS = [1, 2, 3, 4, 5, 6, 7, 8] as const;
  const STEPS = [1, 2, 3, 4, 5, 6, 7] as const;
  const NAMES = [
    ...SLOTS.flatMap((slot) => [
      `--categorical-${slot}-mark`,
      `--categorical-${slot}-tint`,
      `--categorical-${slot}-on-tint`,
    ]),
    ...STEPS.map((step) => `--sequential-${step}`),
  ];

  /** The declarations of one emitted block, by its selector. */
  function blockNames(selector: string): string[] {
    const start = css.indexOf(`${selector} {`);
    const body = css.slice(start, css.indexOf('}', start));
    return [...body.matchAll(/^\s*(--[a-z0-9-]+):/gm)].map((match) => match[1]);
  }

  it('emits the same 31 names in both scheme blocks', () => {
    expect(NAMES).toHaveLength(31);
    const light = blockNames(':root');
    const dark = blockNames('[data-theme=dark]');
    for (const name of NAMES) {
      expect(light, name).toContain(name);
      expect(dark, name).toContain(name);
    }
    expect(dark.filter((name) => /^--(categorical|sequential)-/.test(name))).toHaveLength(31);
  });

  it('gives every slot its own values in each scheme', () => {
    const light = tmEmittedSchemeVars(tmTokensDefault, 'light');
    const dark = tmEmittedSchemeVars(tmTokensDefault, 'dark');
    expect(light.get('--categorical-1-mark')).toBe('#118AA6');
    expect(light.get('--categorical-3-on-tint')).toBe('#2C3173');
    expect(dark.get('--categorical-1-mark')).toBe('#2AA5C2');
    expect(dark.get('--categorical-8-tint')).toBe('#583136');
  });

  it('runs the sequential ramp faint to strong over the teal ramp, reversed in dark', () => {
    const light = tmEmittedSchemeVars(tmTokensDefault, 'light');
    const dark = tmEmittedSchemeVars(tmTokensDefault, 'dark');
    expect(STEPS.map((step) => light.get(`--sequential-${step}`))).toEqual(
      [100, 200, 300, 400, 600, 700, 800].map((stop) => `var(--teal-${stop})`),
    );
    expect(STEPS.map((step) => dark.get(`--sequential-${step}`))).toEqual(
      [900, 800, 700, 600, 400, 300, 200].map((stop) => `var(--teal-${stop})`),
    );
    // References, so a distribution that re-brands teal re-brands the steps.
    expect(tmResolveVar(light, '--sequential-1')).toBe('#CBE6EC');
    expect(tmResolveVar(dark, '--sequential-7')).toBe('#A3D3DD');
  });

  it('reads text on every tint at 4.5:1 or more, in both schemes', () => {
    for (const scheme of ['light', 'dark'] as const) {
      const vars = tmEmittedSchemeVars(tmTokensDefault, scheme);
      for (const slot of SLOTS) {
        const tint = tmResolveVar(vars, `--categorical-${slot}-tint`);
        const onTint = tmResolveVar(vars, `--categorical-${slot}-on-tint`);
        expect(tint, `${scheme} slot ${slot} tint`).toMatch(/^#[0-9A-Fa-f]{6}$/);
        expect(onTint, `${scheme} slot ${slot} onTint`).toMatch(/^#[0-9A-Fa-f]{6}$/);
        expect(contrast(tint!, onTint!), `${scheme} slot ${slot}`).toBeGreaterThanOrEqual(4.5);
      }
    }
  });

  it('reads an unkeyed avatar’s initials and every counter fill at 4.5:1 or more, in both schemes', () => {
    // The initials are 9 to 11 px on the small avatars, and a counter's
    // digits 11 px: both are normal-size text for WCAG.
    const pairs = [
      ['--avatar-bg', '--avatar-text'],
      ['--counter-neutral-bg', '--counter-neutral-text'],
      ['--counter-accent-bg', '--counter-accent-text'],
      ['--counter-attention-bg', '--counter-attention-text'],
      ['--badge-accent-bg', '--badge-accent-text'],
    ] as const;
    for (const scheme of ['light', 'dark'] as const) {
      const vars = tmEmittedSchemeVars(tmTokensDefault, scheme);
      for (const [bg, ink] of pairs) {
        const background = tmResolveVar(vars, bg);
        const text = tmResolveVar(vars, ink);
        expect(background, `${scheme} ${bg}`).toMatch(/^#[0-9A-Fa-f]{6}$/);
        expect(text, `${scheme} ${ink}`).toMatch(/^#[0-9A-Fa-f]{6}$/);
        expect(contrast(background!, text!), `${scheme} ${bg}`).toBeGreaterThanOrEqual(4.5);
      }
    }
  });

  it('reads every progress fill at 3:1 or more against its track, on the page and on a card, in both schemes', () => {
    // The fill is the part that shows the share (a non-text graphic), so
    // it needs 3:1 against the track around it. The dark track is a
    // translucent white, composited over the surface the bar sits on.
    for (const scheme of ['light', 'dark'] as const) {
      const vars = tmEmittedSchemeVars(tmTokensDefault, scheme);
      for (const surface of ['--surface-page', '--surface-card']) {
        const track = opaque(tmResolveVar(vars, '--progress-track')!, tmResolveVar(vars, surface)!);
        for (const tone of ['accent', 'success', 'warning', 'error']) {
          const fill = paint(tmResolveVar(vars, `--progress-${tone}`)!, vars);
          expect(contrast(fill, track), `${scheme} ${tone} on ${surface}`).toBeGreaterThanOrEqual(
            3,
          );
        }
      }
    }
  });

  it('fails the missing-ref gate on a step that references a missing teal stop', () => {
    const light = tmTokensDefault.semantic.colorScheme.light;
    const broken: TmTokens = {
      ...tmTokensDefault,
      semantic: {
        ...tmTokensDefault.semantic,
        colorScheme: {
          ...tmTokensDefault.semantic.colorScheme,
          light: { ...light, sequential: { ...light.sequential, 4: '{teal.450}' } },
        },
      },
    };
    const issues = tmValidateTokens(broken);
    expect(
      issues.some((i) => i.message.includes('--sequential-4') && i.message.includes('--teal-450')),
    ).toBe(true);
  });
});
