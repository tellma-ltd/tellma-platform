// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_BOOT_SCRIPT_FILE, TM_INDEX_HTML_PROBLEM, TM_INDEX_HTML_RULES } from '../build/index-html-rules.js';
import { tmAttribute, tmElements, tmFirstElement, tmParseDocument, type TmHtmlElement } from '../common/html.js';
import type { TmBuildProblem } from '../common/problems.js';

// The checks of `index.html` (spec 0024 §4.1): the source before the build,
// and the built file after it, where Angular has added its stylesheet,
// module scripts and preloads. Each failed rule of TM_INDEX_HTML_RULES is one
// `index-html-invalid` problem, in the rules' order.

/** Which file the check reads. */
export type TmIndexHtmlFile = 'source' | 'built';

/** Whether a script element is a classic, blocking script. */
function isClassicBlocking(script: TmHtmlElement): boolean {
  const type = (tmAttribute(script, 'type') ?? '').trim().toLowerCase();
  return (
    (type === '' || type === 'text/javascript') &&
    tmAttribute(script, 'async') === null &&
    tmAttribute(script, 'defer') === null
  );
}

/** Each rule's test, by the rule's id: true when the document passes. */
const TESTS: Readonly<Record<string, (head: TmHtmlElement, body: TmHtmlElement, all: readonly TmHtmlElement[]) => boolean>> = {
  'base-href': (head, _body, all) => {
    const base = all.find((element) => element.tagName === 'base' && tmAttribute(element, 'href') !== null);
    if (base === undefined || tmAttribute(base, 'href') !== '/' || !tmElements(head).includes(base)) {
      return false;
    }
    // No element before it may carry a URL, which would resolve against the page's own path.
    return all.slice(0, all.indexOf(base)).every((element) => tmAttribute(element, 'src') === null && tmAttribute(element, 'href') === null);
  },
  'boot-script-first': (head) => {
    const first = tmElements(head).find((element) => element.tagName === 'script');
    return first !== undefined && tmAttribute(first, 'src') === TM_BOOT_SCRIPT_FILE && isClassicBlocking(first);
  },
  'app-element': (_head, body) => tmFirstElement(body, 'tm-app') !== null,
  'manifest-link': (head) =>
    tmElements(head).some(
      (element) =>
        element.tagName === 'link' &&
        (tmAttribute(element, 'rel') ?? '').toLowerCase().split(/\s+/).includes('manifest') &&
        tmAttribute(element, 'href') === 'manifest.webmanifest',
    ),
  'color-scheme-meta': (head) =>
    tmElements(head).some(
      (element) =>
        element.tagName === 'meta' &&
        (tmAttribute(element, 'name') ?? '').toLowerCase() === 'color-scheme' &&
        (tmAttribute(element, 'content') ?? '').trim().split(/\s+/).join(' ') === 'light dark',
    ),
  'no-inline-script': (_head, _body, all) =>
    all.every((element) => element.tagName !== 'script' || (tmAttribute(element, 'src') ?? '').trim() !== ''),
  'no-event-handler-attribute': (_head, _body, all) =>
    all.every((element) => !element.attrs.some((attribute) => /^on/i.test(attribute.name))),
};

/**
 * Checks an `index.html` against the rules for the file.
 *
 * @param html - The file's text.
 * @param file - Whether it is the source or the built file.
 * @param key - The file's path as the problem names it.
 * @param feature - The distribution's slug, which owns the file.
 * @returns One problem per failed rule.
 */
export function tmCheckIndexHtml(html: string, file: TmIndexHtmlFile, key: string, feature: string): TmBuildProblem[] {
  const document = tmParseDocument(html);
  const htmlElement = tmFirstElement(document, 'html')!;
  // The parser always creates <head> and <body>, moving stray content into them.
  const head = tmFirstElement(htmlElement, 'head')!;
  const body = tmFirstElement(htmlElement, 'body')!;
  const all = tmElements(document);
  return TM_INDEX_HTML_RULES.filter((rule) => rule.scope === 'both' || rule.scope === file)
    .filter((rule) => !TESTS[rule.id]!(head, body, all))
    .map((rule) => ({ code: TM_INDEX_HTML_PROBLEM, severity: 'error', key, feature, message: rule.message }));
}
