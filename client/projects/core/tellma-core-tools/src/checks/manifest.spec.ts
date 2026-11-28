// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { FIXTURE_APP_DIR, fakeEmitData } from '../../fixtures/fake-emit.js';
import type { TmEmittedMetadata } from '../emit/emitted-metadata.js';
import { tmCheckManifest, tmLocalizedNames, type TmLocalizedNames } from './manifest.js';
import { tmPngSize } from './png.js';

const publicDir = join(FIXTURE_APP_DIR, 'public');
const NO_NAMES: TmLocalizedNames = { name_localized: {}, short_name_localized: {} };

/** The fixture's manifest, which passes. */
const MANIFEST = {
  id: '/',
  start_url: '/',
  scope: '/',
  name: 'Acme',
  short_name: 'Acme',
  display: 'standalone',
  dir: 'auto',
  lang: 'en',
  background_color: '#001722',
  theme_color: '#001722',
  icons: [
    { src: 'icons/icon-192.png', sizes: '192x192', type: 'image/png' },
    { src: 'icons/icon-512.png', sizes: '512x512', type: 'image/png' },
    { src: 'icons/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
  ],
};

/** The messages of a manifest's problems. */
function messages(manifest: unknown, names: TmLocalizedNames = NO_NAMES): string[] {
  const text = typeof manifest === 'string' || manifest === null ? manifest : JSON.stringify(manifest);
  return tmCheckManifest(text, publicDir, names, 'public/manifest.webmanifest', 'acme').map((problem) => {
    expect(problem).toMatchObject({ code: 'manifest-invalid', severity: 'error', key: 'public/manifest.webmanifest', feature: 'acme' });
    return problem.message;
  });
}

describe('the manifest check', () => {
  it('passes the scaffolded manifest', () => {
    expect(messages(MANIFEST)).toEqual([]);
  });

  it('requires every member', () => {
    for (const member of ['id', 'start_url', 'scope', 'name', 'short_name', 'display', 'dir', 'lang', 'background_color', 'theme_color', 'icons']) {
      const manifest: Record<string, unknown> = { ...MANIFEST };
      delete manifest[member];
      expect(messages(manifest), member).toEqual([`The member '${member}' is missing.`]);
    }
    expect(messages(null)).toEqual(['The web app manifest public/manifest.webmanifest is missing.']);
    expect(messages('{ not json')).toEqual(['The manifest is not a JSON object.']);
    expect(messages('[]')).toEqual(['The manifest is not a JSON object.']);
  });

  it('limits short names, localized ones included, to 12 characters', () => {
    expect(messages({ ...MANIFEST, short_name: 'Acme Accounts' })).toEqual(["short_name 'Acme Accounts' is longer than 12 characters."]);
    // Twelve characters a reader sees, though more UTF-16 units.
    expect(messages({ ...MANIFEST, short_name: 'Acme 👩‍👩‍👧 Books' })).toEqual([]);
    expect(messages(MANIFEST, { name_localized: {}, short_name_localized: { ar: 'تطبيق أكمي للمحاسبة' } })).toEqual([
      "The ar short name 'تطبيق أكمي للمحاسبة' (Distribution_ShortName) is longer than 12 characters.",
    ]);
  });

  it('refuses prefer_related_applications', () => {
    expect(messages({ ...MANIFEST, prefer_related_applications: true })).toEqual([
      'prefer_related_applications is true, which stops browsers offering to install the app.',
    ]);
    expect(messages({ ...MANIFEST, prefer_related_applications: false })).toEqual([]);
  });

  it('checks each PNG icon against its stated size', () => {
    const icons = (icon: Record<string, unknown>): unknown => ({ ...MANIFEST, icons: [icon] });
    expect(messages(icons({ src: 'icons/icon-192.png', sizes: '512x512', type: 'image/png' }))).toEqual([
      "icons[0]: icons/icon-192.png is 192x192, but its sizes say '512x512'.",
    ]);
    expect(messages(icons({ src: '/icons/icon-512.png', sizes: '192x192 512x512' }))).toEqual([]);
    expect(messages(icons({ src: 'icons/missing.png', sizes: '192x192' }))).toEqual(['icons[0]: icons/missing.png does not exist in public/.']);
    expect(messages(icons({ src: 'icons/favicon.svg', sizes: 'any', type: 'image/png' }))).toEqual([
      'icons[0]: icons/favicon.svg is not a PNG.',
    ]);
    expect(messages(icons({ src: 'icons/favicon.svg', sizes: 'any', type: 'image/svg+xml' }))).toEqual([]);
    expect(messages(icons({ sizes: '192x192' }))).toEqual(['icons[0] needs a src and sizes.']);
    expect(messages({ ...MANIFEST, icons: 'icons/icon-192.png' })).toEqual(["The member 'icons' is not an array."]);
  });

  it('reads a PNG size from the IHDR chunk', () => {
    const header = Buffer.from('89504e470d0a1a0a0000000d49484452000000c0000001000806000000', 'hex');
    expect(tmPngSize(header)).toEqual({ width: 192, height: 256 });
    expect(tmPngSize(header.subarray(0, 20))).toBeNull();
    expect(tmPngSize(Buffer.from('GIF89a................'))).toBeNull();
  });

  it('localizes the names from the offered languages\' string packs only', () => {
    const data = fakeEmitData();
    const metadata = {
      core: { name: 'core.json', value: data.core },
      strings: new Map([
        ['en', { name: 'en', value: { Distribution_DisplayName: 'Fixture Books' } }],
        ['ar', { name: 'ar', value: data.strings['ar']! }],
        ['fr', { name: 'fr', value: { Distribution_DisplayName: 'Livres' } }],
      ]),
    } as unknown as TmEmittedMetadata;
    expect(tmLocalizedNames(metadata)).toEqual({
      name_localized: { en: 'Fixture Books', ar: 'فكسشر' },
      short_name_localized: { ar: 'فكسشر' },
    });
  });
});
