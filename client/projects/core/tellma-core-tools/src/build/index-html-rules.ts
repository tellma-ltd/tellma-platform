// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The rules a distribution's `index.html` obeys (spec 0024 §4.1), as data.
// The build checks the source file before it builds and the built file after
// (spec 0024 §2.6); every failure is the build problem `index-html-invalid`.
// The boot script must run first and alone: it decides the language, the
// theme and the session before the first paint, and the Content Security
// Policy (`script-src 'self'`) admits no inline script.

/** The build problem code of every failed `index.html` rule. */
export const TM_INDEX_HTML_PROBLEM = 'index-html-invalid';

/** Which `index.html` a rule reads: the source, the built file, or both. */
export type TmIndexHtmlScope = 'source' | 'built' | 'both';

/** One rule of a distribution's `index.html`. */
export interface TmIndexHtmlRule {
  /** The rule's stable identifier. */
  readonly id: string;
  /** Which file the rule reads. */
  readonly scope: TmIndexHtmlScope;
  /** What the rule requires, as the build reports a failure. */
  readonly message: string;
}

/** The generated boot script's file name, which `index.html` loads first. */
export const TM_BOOT_SCRIPT_FILE = 'tm-boot.js';

/** The rules of a distribution's `index.html`, in the order the build reports them. */
export const TM_INDEX_HTML_RULES: readonly TmIndexHtmlRule[] = Object.freeze([
  {
    id: 'boot-script-first',
    scope: 'both',
    message: `The first script in <head> must be <script src="${TM_BOOT_SCRIPT_FILE}">, a classic script with no async or defer.`,
  },
  {
    id: 'app-element',
    scope: 'both',
    message: 'The body must hold the <tm-app> element.',
  },
  {
    id: 'manifest-link',
    scope: 'both',
    message: 'The head must link the web app manifest: <link rel="manifest" href="manifest.webmanifest">.',
  },
  {
    id: 'color-scheme-meta',
    scope: 'both',
    message: 'The head must declare <meta name="color-scheme" content="light dark">.',
  },
  {
    id: 'no-inline-script',
    scope: 'both',
    message: 'No script may be inline: every <script> needs a src.',
  },
  {
    id: 'no-event-handler-attribute',
    scope: 'built',
    message: 'No element may carry an event handler attribute (on*), which the Content Security Policy refuses.',
  },
] satisfies readonly TmIndexHtmlRule[]);
