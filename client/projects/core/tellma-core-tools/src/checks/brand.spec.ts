// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { TM_BRAND_FILES, tmBrandSvgFaults, tmCheckBrandFiles, tmResolveBrandFiles } from './brand.js';

const SVG = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 32"><rect width="120" height="32"/></svg>';

describe('a brand SVG', () => {
  it('passes with a viewBox and no script', () => {
    expect(tmBrandSvgFaults(SVG)).toEqual([]);
    expect(tmBrandSvgFaults(`<?xml version="1.0"?>\n${SVG.replace('0 0 120 32', '-1.5,0,1e2,32')}`)).toEqual([]);
  });

  it('needs a viewBox with a positive size', () => {
    expect(tmBrandSvgFaults(SVG.replace(' viewBox="0 0 120 32"', ''))).toEqual(['Its <svg> element has no valid viewBox, so it cannot scale.']);
    expect(tmBrandSvgFaults(SVG.replace('0 0 120 32', '0 0 0 32'))).toEqual(['Its <svg> element has no valid viewBox, so it cannot scale.']);
    expect(tmBrandSvgFaults('<p>not an image</p>')).toEqual(['It holds no <svg> element.']);
  });

  it('refuses scripts, event handlers and javascript: URLs', () => {
    expect(tmBrandSvgFaults(SVG.replace('<rect', '<script>alert(1)</script><rect'))).toEqual(['It holds a <script> element.']);
    expect(tmBrandSvgFaults(SVG.replace('<rect', '<rect onclick="go()"'))).toEqual(["It carries the event-handler attribute 'onclick'."]);
    expect(tmBrandSvgFaults(SVG.replace('<svg ', '<svg onload="go()" '))).toEqual(["It carries the event-handler attribute 'onload'."]);
    expect(
      tmBrandSvgFaults(SVG.replace('<rect', '<a xmlns:xlink="http://www.w3.org/1999/xlink" xlink:href=" javascript:go()"><rect').replace('</svg>', '</a></svg>')),
    ).toEqual(["Its 'href' is a javascript: URL."]);
  });

  it('reads them as an XML parser does when the file opens on its own', () => {
    // A script element under a namespace prefix.
    expect(tmBrandSvgFaults(SVG.replace('<rect', '<s:script xmlns:s="http://www.w3.org/2000/svg">alert(1)</s:script><rect'))).toEqual([
      'It holds a <script> element.',
    ]);
    // A javascript: URL with a tab inside, which the URL parser removes.
    expect(tmBrandSvgFaults(SVG.replace('<rect', '<a href="java&#9;script:go()"><rect').replace('</svg>', '</a></svg>'))).toEqual([
      "Its 'href' is a javascript: URL.",
    ]);
    expect(tmBrandSvgFaults(SVG.replace('<rect', '<a href="&#1;javascript:go()"><rect').replace('</svg>', '</a></svg>'))).toEqual([
      "Its 'href' is a javascript: URL.",
    ]);
    // Animations that set a link's href, which no href attribute shows.
    const link = (animation: string): string => SVG.replace('<rect', `<a href="#">${animation}<rect`).replace('</svg>', '</a></svg>');
    expect(tmBrandSvgFaults(link('<set attributeName="href" to="javascript:go()"/>'))).toEqual(["Its <set> element animates a link's href."]);
    expect(tmBrandSvgFaults(link('<animate attributeName="xlink:href" values="javascript:go()"/>'))).toEqual([
      "Its <animate> element animates a link's href.",
    ]);
    expect(tmBrandSvgFaults(link('<animate attributeName="opacity" values="0;1"/>'))).toEqual([]);
  });
});

describe('the brand files', () => {
  let dir: string | undefined;
  afterEach(() => {
    if (dir !== undefined) {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it("take the distribution's file over the default, and report a name neither has", () => {
    dir = mkdtempSync(join(tmpdir(), 'tm-brand-'));
    const publicDir = join(dir, 'public');
    const defaults = join(dir, 'defaults');
    mkdirSync(join(publicDir, 'brand'), { recursive: true });
    mkdirSync(defaults);
    writeFileSync(join(publicDir, 'brand', 'mark-on-dark.svg'), SVG.replace('<rect', '<rect onclick="x"'));
    writeFileSync(join(defaults, 'mark-on-dark.svg'), SVG);
    writeFileSync(join(defaults, 'wordmark-on-dark.svg'), SVG);
    writeFileSync(join(defaults, 'wordmark-on-light.svg'), SVG);

    const files = tmResolveBrandFiles(publicDir, defaults);
    expect(files.map((file) => file.name)).toEqual(TM_BRAND_FILES);
    expect(files.find((file) => file.name === 'mark-on-dark.svg')).toEqual({
      name: 'mark-on-dark.svg',
      source: join(publicDir, 'brand', 'mark-on-dark.svg'),
      override: true,
    });
    const problems = tmCheckBrandFiles(files, (file) => file.name, 'acme');
    expect(problems).toEqual([
      { code: 'brand-file-invalid', severity: 'error', key: 'mark-on-dark.svg', feature: 'acme', message: "It carries the event-handler attribute 'onclick'." },
      {
        code: 'brand-file-invalid',
        severity: 'error',
        key: 'mark-on-light.svg',
        feature: 'acme',
        message: "Neither public/brand/ nor @tellma/core's brand/ provides mark-on-light.svg.",
      },
    ]);
    expect(tmResolveBrandFiles(publicDir, null).filter((file) => file.source === null)).toHaveLength(3);
  });
});
