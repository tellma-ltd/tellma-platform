// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The dangling-var gate: every `var(--…)` a library stylesheet reads must be
 * a variable the token layer actually emits.
 *
 * This is the one failure in the whole styling stack that nothing else
 * catches. A name that misses is not a fallback — CSS drops the entire
 * declaration at computed-value time — so a mis-spelled token does not
 * error, it just silently stops applying, and the component renders with
 * `padding: 0` or no height and looks merely a bit off. Neither stylelint
 * nor the compiler has any idea the name was supposed to mean something.
 *
 * The trap that keeps producing them: the emitter kebab-cases a token key,
 * and two ADJACENT capitals do not split — `cellPaddingXPlain` becomes
 * `--grid-cell-padding-xplain`, not `-x-plain`.
 */
import { readFileSync } from 'node:fs';
import { basename } from 'node:path';

/** A `var()` read found in a stylesheet. */
export interface TmCssVarRef {
  /** The custom-property name, including the leading `--`. */
  readonly name: string;
  /** The file it was read in. */
  readonly file: string;
  /** Whether the read supplied a fallback, which makes a miss survivable. */
  readonly hasFallback: boolean;
}

/**
 * Names a stylesheet may read without the token layer emitting them:
 *
 * - `--tm-*` are component-PRIVATE locals, declared by a component sheet —
 *   usually the reading one, but not always: the size-ladder locals are
 *   declared by tm-form-field's sheet and read by the global input sheet
 *   and the pickers'. So a `--tm-` read passes only when SOME scanned
 *   sheet declares the name (never unconditionally: a typo'd `--tm-` read
 *   is exactly the silent-drop failure this gate exists to catch).
 * - the rest are written onto the element by a component at runtime, as an
 *   inline style, so no static sheet declares them.
 */
const RUNTIME_SET = new Set(['--grid-template', '--grid-level']);

/**
 * Every custom property the sheet declares. Anchored on a block opener or a
 * statement end rather than the line start, so two declarations sharing a
 * line both count.
 */
export function tmDeclaredVars(css: string): Set<string> {
  const declared = new Set<string>();
  for (const match of css.matchAll(/(?:^|[{;])\s*(--[\w-]+)\s*:/gm)) {
    declared.add(match[1]);
  }
  return declared;
}

/** Every `var()` read in a stylesheet, with whether it carried a fallback. */
export function tmVarRefs(css: string, file: string): TmCssVarRef[] {
  const refs: TmCssVarRef[] = [];
  for (const match of css.matchAll(/var\(\s*(--[\w-]+)\s*(,)?/g)) {
    refs.push({ name: match[1], file, hasFallback: match[2] === ',' });
  }
  return refs;
}

/**
 * Checks each stylesheet's reads against the emitted sheet. Returns one
 * message per dangling reference; empty means clean.
 */
export function tmCheckCssRefs(emittedCss: string, files: readonly string[]): string[] {
  const declared = tmDeclaredVars(emittedCss);
  const problems: string[] = [];
  const seen = new Set<string>();

  // First pass: the reference set for the private `--tm-` prefix is the
  // UNION of every scanned sheet's declarations, because those locals are
  // legitimately declared in one sheet and read in another (see
  // RUNTIME_SET's comment). Non-private names stay same-sheet-only — a
  // cross-file read of one of those is a coupling the gate should surface.
  const contents = new Map<string, string>();
  const tmLocals = new Set<string>();
  for (const file of files) {
    const css = readFileSync(file, 'utf8');
    contents.set(file, css);
    for (const name of tmDeclaredVars(css)) {
      if (name.startsWith('--tm-')) {
        tmLocals.add(name);
      }
    }
  }

  for (const [file, css] of contents) {
    // A sheet's own locals count as declared for the rest of that sheet.
    const local = tmDeclaredVars(css);
    for (const ref of tmVarRefs(css, file)) {
      if (
        ref.hasFallback ||
        declared.has(ref.name) ||
        local.has(ref.name) ||
        tmLocals.has(ref.name) ||
        RUNTIME_SET.has(ref.name)
      ) {
        continue;
      }
      const key = `${ref.name}@${file}`;
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      problems.push(
        ref.name.startsWith('--tm-')
          ? `${basename(file)} reads ${ref.name}, which no scanned stylesheet declares`
          : `${basename(file)} reads ${ref.name}, which no token emits`,
      );
    }
  }
  return problems;
}
