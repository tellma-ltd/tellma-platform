import type { TmTokens } from '../contract/tokens';
import { tmEmittedSchemeVars } from '../emit/emit-css';
import { tmContrastRatio, tmParseColor, TM_CONTRAST_THRESHOLDS, type TmRgba } from './contrast';

/**
 * The build-time token gates (§4, DoD 9): missing-ref, WCAG contrast in both
 * schemes (honoring the justified-exceptions allowlist), exception hygiene
 * (mandatory reason), and the contrast-pair completeness lint. A preset that
 * fails any gate fails the build.
 */

export interface TmTokenValidationIssue {
  readonly gate: 'missing-ref' | 'contrast' | 'exception' | 'completeness';
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
 * Variables that carry text/glyph/boundary ink and therefore MUST appear as
 * a `fg` in contrastPairs or contrastExceptions — the completeness lint that
 * keeps the pair list growing with the contract (§4). Generic page borders
 * and dividers are decorative and deliberately excluded; field borders are
 * component boundaries and included.
 */
// NOTE: --text-xs/sm/base/lg are font SIZES (brand naming), not colors — the
// text COLOR roles are enumerated explicitly.
const COMPLETENESS_FG = /^--(text-(strong|body|secondary|muted|link|on-dark)|field-(text|text-disabled|placeholder|icon|border|border-hover|border-focus|border-invalid)|color-on-primary|accent|focus-ring-color|success|warning|error|info)$/;

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

  // Gate 2 — exception hygiene: reason is mandatory and non-empty.
  for (const ex of tokens.contrastExceptions) {
    if (!ex.reason || ex.reason.trim().length === 0) {
      issues.push({
        gate: 'exception',
        message: `contrastException ${ex.fg} on ${ex.bg} has an empty reason — every below-AA pair must be justified`,
      });
    }
  }

  // Gate 3 — WCAG contrast, both schemes, fixed AA thresholds.
  const excepted = new Set(tokens.contrastExceptions.map((e) => `${e.fg}|${e.bg}`));
  for (const scheme of schemes) {
    const vars = varMaps.get(scheme)!;
    const canvas = resolveColor(vars, '--surface-page') ?? undefined;
    for (const pair of tokens.contrastPairs) {
      if (excepted.has(`${pair.fg}|${pair.bg}`)) {
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
  const lightVars = varMaps.get('light')!;
  for (const name of lightVars.keys()) {
    if (COMPLETENESS_FG.test(name) && !declared.has(name)) {
      issues.push({
        gate: 'completeness',
        message: `${name} carries foreground ink but is not declared in contrastPairs/contrastExceptions`,
      });
    }
  }

  return issues;
}
