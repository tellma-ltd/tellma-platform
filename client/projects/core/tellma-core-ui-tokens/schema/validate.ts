// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmSchemeColors, TmTokens } from '../contract/tokens';
import { tmEmittedSchemeVars, tmRefToVarName, tmTokenValueToCss } from '../emit/emit-css';
import { tmContrastRatio, tmParseColor, TM_CONTRAST_THRESHOLDS, type TmRgba } from './contrast';

/**
 * The build-time token gates: missing-ref, WCAG contrast in both
 * schemes (honoring the justified-exceptions allowlist), exception hygiene
 * (mandatory reason), and the contrast-pair completeness lint. A preset that
 * fails any gate fails the build.
 */

export interface TmTokenValidationIssue {
  /** Which gate flagged the issue. */
  readonly gate: 'missing-ref' | 'contrast' | 'exception' | 'completeness';
  /** Human-readable description of the failure. */
  readonly message: string;
}

/** Resolves a `var(--x)`-referencing value to its literal within the map. */
export function tmResolveVar(vars: Map<string, string>, name: string): string | null {
  let value = vars.get(name);
  const seen = new Set<string>([name]);
  while (value !== undefined) {
    const match = /^var\((--[a-z0-9-]+)\)$/i.exec(value.trim());
    if (!match) {
      return value;
    }
    if (seen.has(match[1])) {
      return null; // cycle
    }
    seen.add(match[1]);
    value = vars.get(match[1]);
  }
  return null;
}

function resolveColor(vars: Map<string, string>, name: string): TmRgba | null {
  const literal = tmResolveVar(vars, name);
  return literal === null ? null : tmParseColor(literal);
}

/**
 * Ink classification of every scheme-color key, typed against the contract:
 * `true` = the token carries text/glyph/boundary ink and MUST appear as a
 * `fg` in contrastPairs or contrastExceptions. Object-valued keys (the
 * `status` triples) classify each member separately. Adding a key — or a
 * whole group, or a member to a triple — refuses to compile until it is
 * classified here, so the completeness lint grows with the contract by
 * construction; only the exclusions (decorative surfaces and page borders)
 * are a human decision.
 */
type TmInkClassification = {
  readonly [G in Exclude<keyof TmSchemeColors, 'colorScheme'>]: {
    readonly [K in keyof TmSchemeColors[G]]: TmSchemeColors[G][K] extends object
      ? { readonly [M in keyof TmSchemeColors[G][K]]: boolean }
      : boolean;
  };
};

const CARRIES_INK: TmInkClassification = {
  text: { strong: true, body: true, secondary: true, muted: true, onDark: true, link: true },
  surface: {
    page: false,
    subtle: false,
    sunken: false,
    card: false,
    inverse: false,
    hover: false,
    selected: false,
  },
  // Generic page borders/dividers are decorative de-emphasis; the FIELD
  // borders below are component boundaries and carry ink.
  border: { subtle: false, default: false, strong: false, divider: false },
  selection: { bg: false, text: true },
  action: {
    primary: false,
    primaryHover: false,
    primaryActive: false,
    onPrimary: true,
    accent: true,
  },
  // Each triple's fg carries text/icon ink; bg and border are surface tints.
  status: {
    success: { fg: true, bg: false, border: false },
    warning: { fg: true, bg: false, border: false },
    error: { fg: true, bg: false, border: false },
    info: { fg: true, bg: false, border: false },
  },
  field: {
    bg: false,
    bgDisabled: false,
    bgFilled: false,
    border: true,
    borderHover: true,
    borderFocus: true,
    borderInvalid: true,
    text: true,
    textDisabled: true,
    placeholder: true,
    icon: true,
  },
};

/** The emitted variable names the classification marks as ink-carrying. */
function requiredInkVars(): ReadonlySet<string> {
  const names = new Set<string>();
  for (const [group, keys] of Object.entries(CARRIES_INK)) {
    for (const [key, classification] of Object.entries(keys)) {
      if (typeof classification === 'object') {
        for (const [member, carriesInk] of Object.entries(classification)) {
          if (carriesInk) {
            names.add(tmRefToVarName(`${group}.${key}.${member}`));
          }
        }
      } else if (classification) {
        names.add(
          group === 'border' && key === 'divider'
            ? '--divider'
            : tmRefToVarName(`${group}.${key}`),
        );
      }
    }
  }
  // Semantic ink outside the scheme groups.
  names.add('--focus-ring-color');
  return names;
}

/**
 * Runs every gate over a preset and returns all issues found; an empty
 * array means the preset passes.
 */
export function tmValidateTokens(tokens: TmTokens): TmTokenValidationIssue[] {
  const issues: TmTokenValidationIssue[] = [];
  const schemes = ['light', 'dark'] as const;
  const varMaps = new Map(schemes.map((s) => [s, tmEmittedSchemeVars(tokens, s)]));

  // Gate 1 — missing refs: every emitted var() must resolve within its scheme.
  for (const scheme of schemes) {
    const vars = varMaps.get(scheme)!;
    for (const [name, value] of vars) {
      for (const match of value.matchAll(/var\((--[a-z0-9-]+)/gi)) {
        if (!vars.has(match[1])) {
          issues.push({
            gate: 'missing-ref',
            message: `[${scheme}] ${name} references missing token ${match[1]}`,
          });
        }
      }
    }
  }

  // Gate 1 also covers the :lang() leading rules, which live outside the
  // scheme blocks: each value must resolve against the emitted variables.
  for (const [lang, value] of Object.entries(tokens.semantic.leadingByLang)) {
    const css = tmTokenValueToCss(value);
    const ref = /^var\((--[a-z0-9-]+)\)$/i.exec(css);
    if (ref && !varMaps.get('light')!.has(ref[1])) {
      issues.push({
        gate: 'missing-ref',
        message: `leadingByLang.${lang} references missing token ${ref[1]}`,
      });
    }
  }

  // Gate 2 — exception hygiene: reason is mandatory and non-empty.
  for (const ex of tokens.contrastExceptions) {
    if (!ex.reason || ex.reason.trim().length === 0) {
      issues.push({
        gate: 'exception',
        message: `contrastException ${ex.fg} on ${ex.bg} has an empty reason — every below-AA pair must be justified`,
      });
    }
  }

  // Gate 3 — WCAG contrast, both schemes, fixed AA thresholds. Exceptions
  // match per scheme (and per kind when they declare one), so a pair that
  // only fails in one scheme stays gated in the other.
  for (const scheme of schemes) {
    const vars = varMaps.get(scheme)!;
    const canvas = resolveColor(vars, '--surface-page') ?? undefined;
    for (const pair of tokens.contrastPairs) {
      const excepted = tokens.contrastExceptions.some(
        (e) =>
          e.fg === pair.fg &&
          e.bg === pair.bg &&
          (e.scheme === undefined || e.scheme === scheme) &&
          (e.kind === undefined || e.kind === pair.kind),
      );
      if (excepted) {
        continue;
      }
      const fg = resolveColor(vars, pair.fg);
      const bg = resolveColor(vars, pair.bg);
      if (!fg || !bg) {
        issues.push({
          gate: 'contrast',
          message: `[${scheme}] cannot resolve ${!fg ? pair.fg : pair.bg} to a color`,
        });
        continue;
      }
      const ratio = tmContrastRatio(fg, bg, canvas);
      const threshold = TM_CONTRAST_THRESHOLDS[pair.kind];
      if (ratio < threshold) {
        issues.push({
          gate: 'contrast',
          message:
            `[${scheme}] ${pair.fg} on ${pair.bg} = ${ratio.toFixed(2)}:1, below the AA ` +
            `${pair.kind} minimum ${threshold}:1 (add a justified contrastException or fix the color)`,
        });
      }
    }
  }

  // Gate 4 — completeness: every ink-carrying token appears as a declared fg.
  const declared = new Set([
    ...tokens.contrastPairs.map((p) => p.fg),
    ...tokens.contrastExceptions.map((e) => e.fg),
  ]);
  for (const name of requiredInkVars()) {
    if (!declared.has(name)) {
      issues.push({
        gate: 'completeness',
        message: `${name} carries foreground ink but is not declared in contrastPairs/contrastExceptions`,
      });
    }
  }

  return issues;
}
