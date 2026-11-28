// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { describe, expect, it } from 'vitest';

import { tmCheckHashedNames } from './hashed-names.js';

describe('the hashed-name check', () => {
  it('passes hashed JavaScript, CSS, fonts and metadata, and the no-cache scripts', () => {
    expect(
      tmCheckHashedNames(
        [
          'main-NFPUBR6O.js',
          'chunk-TZITKNPL.js',
          'styles-MFAZ6P23.css',
          'media/noto-sans-latin-wght-normal-EBRFDBOD.woff2',
          'tm-metadata/core-2D3KMMD15A9K.json',
          'tm-metadata/strings-ar-RTF019H82SGZ.json',
          'ngsw-worker.js',
          'safety-worker.js',
          'tm-boot.js',
          'index.html',
          'ngsw.json',
          'manifest.webmanifest',
          'icons/icon-192.png',
          'brand/mark-on-dark.svg',
        ],
        'acme',
      ),
    ).toEqual([]);
  });

  it('reports each served-immutable file without a hash', () => {
    const problems = tmCheckHashedNames(
      ['extra.js', 'chunk-Ci8p4wIT.js', 'styles-ABC123.css', 'media/font.woff2', 'tm-metadata/core.json', 'lib/a-ABCDEFGH.mjs', 'nested/tm-boot.js'],
      'acme',
    );
    expect(problems.map((problem) => problem.key)).toEqual([
      'extra.js',
      'chunk-Ci8p4wIT.js',
      'styles-ABC123.css',
      'media/font.woff2',
      'tm-metadata/core.json',
      'nested/tm-boot.js',
    ]);
    expect(problems[0]).toMatchObject({ code: 'unhashed-asset', severity: 'error', feature: 'acme' });
  });
});
