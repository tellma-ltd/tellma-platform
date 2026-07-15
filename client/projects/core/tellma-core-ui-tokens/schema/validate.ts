// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmTokens } from '../contract/tokens';
import { tmEmittedSchemeVars, tmTokenValueToCss } from '../emit/emit-css';

/**
 * The build-time token gate: missing-ref — every emitted `var()` reference
 * must resolve within its scheme (and the `:lang()` leading rules against
 * the emitted variables). A preset that fails the gate fails the build.
 * Color-contrast accessibility is covered by the axe browser battery, not
 * by token validation.
 */

export interface TmTokenValidationIssue {
  /** Which gate flagged the issue. */
  readonly gate: 'missing-ref';
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

/**
 * Runs the gate over a preset and returns all issues found; an empty array
 * means the preset passes.
 */
export function tmValidateTokens(tokens: TmTokens): TmTokenValidationIssue[] {
  const issues: TmTokenValidationIssue[] = [];
  const schemes = ['light', 'dark'] as const;
  const varMaps = new Map(schemes.map((s) => [s, tmEmittedSchemeVars(tokens, s)]));

  // Every emitted var() must resolve within its scheme.
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

  // The :lang() leading rules live outside the scheme blocks: each value
  // must resolve against the emitted variables too.
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

  return issues;
}
