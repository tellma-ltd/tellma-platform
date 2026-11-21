// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { describe, expect, it } from 'vitest';

import { TM_INDEX_HTML_RULES } from '../build/index-html-rules.js';
import { tmCheckIndexHtml } from './index-html.js';

/** A source index.html that passes, with markers each case replaces. */
const SOURCE = `<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <base href="/" />
    <meta name="color-scheme" content="light dark" />
    <title>Acme</title>
    <link rel="manifest" href="manifest.webmanifest" />
    <script src="tm-boot.js"></script>
  </head>
  <body><tm-app></tm-app></body>
</html>`;

/** The messages of the rules a document fails. */
function failures(html: string, file: 'source' | 'built' = 'source'): string[] {
  return tmCheckIndexHtml(html, file, 'src/index.html', 'acme').map((problem) => {
    expect(problem).toMatchObject({ code: 'index-html-invalid', severity: 'error', key: 'src/index.html', feature: 'acme' });
    return TM_INDEX_HTML_RULES.find((rule) => rule.message === problem.message)!.id;
  });
}

describe('the index.html check', () => {
  it('passes the scaffolded file, and the built file with Angular\'s additions', () => {
    expect(failures(SOURCE)).toEqual([]);
    const built = SOURCE.replace('</head>', '<link rel="stylesheet" href="styles-ABCD1234.css"></head>').replace(
      '</body>',
      '<link rel="modulepreload" href="chunk-ABCD1234.js"><script src="main-ABCD1234.js" type="module"></script></body>',
    );
    expect(failures(built, 'built')).toEqual([]);
  });

  it('requires tm-boot.js as the first script in the head, classic and blocking', () => {
    expect(failures(SOURCE.replace('<script src="tm-boot.js"></script>', ''))).toEqual(['boot-script-first']);
    expect(failures(SOURCE.replace('<title>', '<script src="other.js"></script><title>'))).toEqual(['boot-script-first']);
    expect(failures(SOURCE.replace('src="tm-boot.js"', 'src="tm-boot.js" defer'))).toEqual(['boot-script-first']);
    expect(failures(SOURCE.replace('src="tm-boot.js"', 'src="tm-boot.js" async'))).toEqual(['boot-script-first']);
    expect(failures(SOURCE.replace('src="tm-boot.js"', 'src="tm-boot.js" type="module"'))).toEqual(['boot-script-first']);
  });

  it('requires <base href="/"> in the head before any element that loads a URL', () => {
    expect(failures(SOURCE.replace('<base href="/" />', ''))).toEqual(['base-href']);
    expect(failures(SOURCE.replace('<base href="/" />', '<base href="/app/" />'))).toEqual(['base-href']);
    // Too late: the manifest and the boot script already resolved against the page's path.
    expect(failures(SOURCE.replace('<base href="/" />', '').replace('</head>', '<base href="/"></head>'))).toEqual(['base-href']);
    expect(failures(SOURCE.replace('<meta charset="utf-8" />', '<link rel="icon" href="favicon.svg" /><meta charset="utf-8" />'))).toEqual(['base-href']);
    // A base with only a target sets no URL; the first one with an href counts.
    expect(failures(SOURCE.replace('<base href="/" />', '<base target="_self" /><base href="/" />'))).toEqual([]);
  });

  it('requires the app element, the manifest link and the color-scheme meta', () => {
    expect(failures(SOURCE.replace('<tm-app></tm-app>', '<acme-app></acme-app>'))).toEqual(['app-element']);
    expect(failures(SOURCE.replace('rel="manifest"', 'rel="icon"'))).toEqual(['manifest-link']);
    expect(failures(SOURCE.replace('content="light dark"', 'content="light"'))).toEqual(['color-scheme-meta']);
  });

  it('refuses an inline script anywhere', () => {
    expect(failures(SOURCE.replace('<tm-app></tm-app>', '<tm-app></tm-app><script>alert(1)</script>'))).toEqual(['no-inline-script']);
    expect(failures(SOURCE.replace('</body>', '<template><script>alert(1)</script></template></body>'))).toEqual([
      'no-inline-script',
    ]);
  });

  it('refuses an event-handler attribute in the built file only', () => {
    const handler = SOURCE.replace('<body>', '<body onload="start()">');
    expect(failures(handler)).toEqual([]);
    expect(failures(handler, 'built')).toEqual(['no-event-handler-attribute']);
    expect(failures(SOURCE.replace('<link rel="manifest"', '<link media="print" ONLOAD="x" rel="manifest"'), 'built')).toEqual([
      'no-event-handler-attribute',
    ]);
  });

  it('reports every failed rule, in the rules\' order', () => {
    expect(failures('<!doctype html><html><head><script>1</script></head><body></body></html>')).toEqual([
      'base-href',
      'boot-script-first',
      'app-element',
      'manifest-link',
      'color-scheme-meta',
      'no-inline-script',
    ]);
  });
});
