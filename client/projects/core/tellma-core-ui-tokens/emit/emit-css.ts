import type { TmSchemeColors, TmTokens } from '../contract/tokens';

/**
 * tokens → CSS custom properties emitter (spec 0002 §4).
 *
 * Pure and build-time only: the output is a static stylesheet. Every emitted
 * sheet begins with the canonical `@layer tm.base, tm.theme;` statement so
 * whichever sheet the browser sees first establishes the same layer order —
 * `tm.theme` (a distribution's delta) always beats `tm.base`, and inline
 * runtime `setProperty` writes beat both by the normal cascade.
 */

const KEBAB = (s: string) => s.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();

/**
 * Maps a token Ref path to its emitted variable name. Primitive paths map
 * mechanically ('teal.500' → '--teal-500'); semantic paths go through the
 * brand's canonical names ('action.primary' → '--color-primary',
 * 'status.error.fg' → '--error', 'font.size.sm' → '--text-sm').
 */
export function tmRefToVarName(path: string): string {
  const parts = path.split('.');
  const [head] = parts;
  switch (head) {
    case 'white':
      return '--white';
    case 'ink':
    case 'teal':
    case 'grey':
      return `--${head}-${parts[1]}`;
    case 'radius':
    case 'space':
    case 'shadow':
      return `--${head}-${KEBAB(parts[1])}`;
    case 'font': {
      if (parts[1] === 'size') {
        return `--text-${parts[2]}`;
      }
      if (parts[1] === 'weight') {
        return `--weight-${parts[2]}`;
      }
      if (parts[1] === 'leading') {
        return `--leading-${parts[2]}`;
      }
      return `--font-${parts[1]}`;
    }
    case 'motion': {
      // durationFast → --duration-fast; easeInOut → --ease-in-out
      return `--${KEBAB(parts[1])}`;
    }
    case 'border':
      return parts[1] === 'width' ? '--border-width' : `--border-${KEBAB(parts[1])}`;
    case 'text':
      return `--text-${KEBAB(parts[1])}`;
    case 'surface':
      return `--surface-${KEBAB(parts[1])}`;
    case 'action': {
      const key = parts[1];
      if (key === 'accent') {
        return '--accent';
      }
      if (key === 'onPrimary') {
        return '--color-on-primary';
      }
      return `--color-${KEBAB(key)}`; // primary | primaryHover | primaryActive
    }
    case 'status': {
      const status = parts[1];
      const part = parts[2];
      return part === 'fg' ? `--${status}` : `--${status}-${part}`;
    }
    case 'field':
      return `--field-${KEBAB(parts[1])}`;
    case 'focusRing':
      return `--focus-ring-${KEBAB(parts[1])}`;
    case 'formField':
      return `--field-${KEBAB(parts[1])}`;
    default:
      return `--${parts.map(KEBAB).join('-')}`;
  }
}

/** A token value: a `{ref}` becomes `var(--…)`; literals pass through. */
export function tmTokenValueToCss(value: string): string {
  const match = /^\{(.+)\}$/.exec(value.trim());
  return match ? `var(${tmRefToVarName(match[1])})` : value;
}

type VarMap = Map<string, string>;

function schemeVars(scheme: TmSchemeColors): VarMap {
  const vars: VarMap = new Map();
  const set = (name: string, value: string) => vars.set(name, tmTokenValueToCss(value));

  const overrides = scheme.primitiveOverrides;
  if (overrides?.white) {
    set('--white', overrides.white);
  }
  for (const [ramp, values] of [
    ['grey', overrides?.grey],
    ['teal', overrides?.teal],
  ] as const) {
    for (const [stop, value] of Object.entries(values ?? {})) {
      set(`--${ramp}-${stop}`, value as string);
    }
  }

  for (const [key, value] of Object.entries(scheme.text)) {
    set(tmRefToVarName(`text.${key}`), value);
  }
  for (const [key, value] of Object.entries(scheme.surface)) {
    set(tmRefToVarName(`surface.${key}`), value);
  }
  for (const [key, value] of Object.entries(scheme.border)) {
    set(key === 'divider' ? '--divider' : tmRefToVarName(`border.${key}`), value);
  }
  for (const [key, value] of Object.entries(scheme.action)) {
    set(tmRefToVarName(`action.${key}`), value);
  }
  for (const [status, triple] of Object.entries(scheme.status)) {
    set(`--${status}`, triple.fg);
    set(`--${status}-bg`, triple.bg);
    set(`--${status}-border`, triple.border);
  }
  for (const [key, value] of Object.entries(scheme.field)) {
    set(tmRefToVarName(`field.${key}`), value);
  }
  vars.set('color-scheme', scheme.colorScheme);
  return vars;
}

function sharedVars(tokens: TmTokens): VarMap {
  const vars: VarMap = new Map();
  const set = (name: string, value: string) => vars.set(name, tmTokenValueToCss(value));
  const { color, radius, space, font, border, shadow, motion } = tokens.primitive;

  for (const [stop, value] of Object.entries(color.ink)) {
    set(`--ink-${stop}`, value);
  }
  for (const [stop, value] of Object.entries(color.teal)) {
    set(`--teal-${stop}`, value);
  }
  set('--white', color.white);
  for (const [stop, value] of Object.entries(color.grey)) {
    set(`--grey-${stop}`, value);
  }
  for (const [key, value] of Object.entries(radius)) {
    set(`--radius-${key}`, value);
  }
  for (const [key, value] of Object.entries(space)) {
    set(`--space-${key}`, value);
  }
  set('--font-sans', font.sans);
  set('--font-arabic', font.arabic);
  set('--font-mono', font.mono);
  // Adaptive UI aliases: components consume these. --font-ui is the single
  // multi-script stack — each glyph resolves to its script's face via the
  // faces' unicode-ranges, so mixed-script lines render every brand face at
  // once, independent of page language or direction. --leading-ui is a
  // line-box property that cannot adapt per glyph; the :lang() blocks
  // emitted by tmEmitCss re-point it by content language.
  vars.set('--font-ui', font.ui.join(', '));
  vars.set('--leading-ui', 'var(--leading-body)');
  for (const [key, value] of Object.entries(font.size)) {
    set(`--text-${key}`, value);
  }
  for (const [key, value] of Object.entries(font.weight)) {
    set(`--weight-${key}`, value);
  }
  for (const [key, value] of Object.entries(font.leading)) {
    set(`--leading-${key}`, value);
  }
  set('--border-width', border.width);
  for (const [key, value] of Object.entries(shadow)) {
    set(`--shadow-${key}`, value);
  }
  for (const [key, value] of Object.entries(motion)) {
    set(`--${KEBAB(key)}`, value);
  }

  const { focusRing, formField } = tokens.semantic;
  set('--focus-ring-width', focusRing.width);
  set('--focus-ring-color', focusRing.color);
  set('--focus-ring-offset', focusRing.offset);
  // The composite two-layer ring: an inner gap of --focus-ring-offset in the
  // page background, then the ring color.
  vars.set(
    '--focus-ring',
    '0 0 0 var(--focus-ring-offset) var(--white), ' +
      '0 0 0 calc(var(--focus-ring-offset) + var(--focus-ring-width)) var(--focus-ring-color)',
  );
  for (const [key, value] of Object.entries(formField)) {
    set(tmRefToVarName(`formField.${key}`), value);
  }

  for (const [component, group] of Object.entries(tokens.component)) {
    for (const [key, value] of Object.entries(group)) {
      set(`--${KEBAB(component)}-${KEBAB(key)}`, value);
    }
  }
  return vars;
}

function block(selector: string, vars: VarMap, indent: string): string {
  const lines = [...vars.entries()].map(([name, value]) => `${indent}  ${name}: ${value};`);
  return `${indent}${selector} {\n${lines.join('\n')}\n${indent}}`;
}

/**
 * Emits the full static stylesheet for a preset: shared + light under
 * `:root`, dark under `[data-theme=dark]`, both inside `@layer tm.base`.
 */
export function tmEmitCss(tokens: TmTokens): string {
  const rootVars = new Map([...sharedVars(tokens), ...schemeVars(tokens.semantic.colorScheme.light)]);
  const darkVars = schemeVars(tokens.semantic.colorScheme.dark);

  const langBlocks = Object.entries(tokens.semantic.leadingByLang).map(([lang, value]) =>
    block(`:lang(${lang})`, new Map([['--leading-ui', tmTokenValueToCss(value)]]), '  '),
  );

  return [
    '/* GENERATED by @tellma/core-ui-tokens — do not edit by hand. */',
    '',
    '/* Canonical layer order: first statement of EVERY emitted sheet, so any',
    '   load order establishes tm.theme > tm.base (spec 0002 §4). */',
    '@layer tm.base, tm.theme;',
    '',
    '@layer tm.base {',
    block(':root', rootVars, '  '),
    '',
    block('[data-theme=dark]', darkVars, '  '),
    '',
    '  /* Language-adaptive leading (§7). :lang() ties :root on specificity, so',
    '     these must come later in source; every listed language sets its own',
    '     value, so a marked island (lang="en" in an Arabic page) snaps back. */',
    ...langBlocks,
    '}',
    '',
  ].join('\n');
}

/**
 * The flat variable map of a scheme as emitted (light = shared + light;
 * dark = light map overlaid with the dark block) — the input the contrast
 * gate resolves. Exported for the validators and tests.
 */
export function tmEmittedSchemeVars(tokens: TmTokens, scheme: 'light' | 'dark'): Map<string, string> {
  const base = new Map([
    ...sharedVars(tokens),
    ...schemeVars(tokens.semantic.colorScheme.light),
  ]);
  if (scheme === 'light') {
    return base;
  }
  return new Map([...base, ...schemeVars(tokens.semantic.colorScheme.dark)]);
}
