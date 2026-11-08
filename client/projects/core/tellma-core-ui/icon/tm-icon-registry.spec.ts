// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { EnvironmentInjector, createEnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ChevronRight, Plus, X } from 'lucide';

import { TmIconRegistry, provideTellmaIcons, type TmIconSource } from './tm-icon-registry';

/**
 * The published shape of an `@lucide/icons` v1 icon object, restated so
 * the compiler proves such objects are accepted as they stand: a name, a
 * size, a node whose elements carry a framework `key` and may nest
 * children.
 */
// The package types attributes as Record<string, any>; restated verbatim.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type LucideIconsAttributes = Record<string, any>;
type LucideIconsNode =
  | [name: string, attributes: LucideIconsAttributes]
  | [name: string, attributes: LucideIconsAttributes, children: LucideIconsNode[]];
interface LucideIconsData {
  name?: string;
  node: LucideIconsNode[];
  aliases?: string[];
  size?: number;
}

const lucideIconsObject: LucideIconsData = {
  name: 'grouped',
  size: 24,
  node: [
    ['path', { d: 'M5 12h14', key: '1ays0h' }],
    ['g', { key: 'g1' }, [['circle', { cx: '12', cy: '12', r: '3', key: 'c1' }]]],
  ],
};

function registry(): TmIconRegistry {
  return TestBed.inject(TmIconRegistry);
}

describe('TmIconRegistry', () => {
  it('registers Lucide icon nodes and the object form, and answers get()', () => {
    registry().register({
      plus: Plus,
      'chevron-right': { node: ChevronRight, mirrorInRtl: true },
    });
    expect(registry().get('plus')).toEqual({ node: Plus, mirrorInRtl: false });
    expect(registry().get('chevron-right')).toEqual({ node: ChevronRight, mirrorInRtl: true });
    expect(registry().get('x')).toBeUndefined();
  });

  it('accepts @lucide/icons objects as they stand', () => {
    // Assignability is the point: no cast.
    const source: TmIconSource = lucideIconsObject;
    registry().register({ grouped: source });
    expect(registry().get('grouped')?.node).toBe(lucideIconsObject.node);
  });

  it('a later registration of a name replaces the earlier one', () => {
    registry().register({ close: Plus });
    registry().register({ close: X });
    expect(registry().get('close')?.node).toBe(X);
  });

  for (const attribute of [
    'onclick',
    'ONLOAD',
    'onMouseOver',
    'href',
    'HREF',
    'hreflang',
    'xlink:href',
    'XLink:Href',
  ]) {
    it(`refuses the "${attribute}" attribute`, () => {
      expect(() =>
        registry().register({ bad: [['path', { d: 'M0 0', [attribute]: 'x' }]] }),
      ).toThrow(/refused attribute/);
    });
  }

  it('refuses a refused attribute nested in a group', () => {
    expect(() =>
      registry().register({ bad: [['g', {}, [['path', { onclick: 'alert(1)' }]]]] }),
    ).toThrow(/refused attribute "onclick"/);
  });

  for (const tag of [
    'script',
    'foreignObject',
    'animate',
    'set',
    'use',
    'image',
    'style',
    'a',
    'svg',
  ]) {
    it(`refuses a <${tag}> element`, () => {
      expect(() => registry().register({ bad: [[tag, {}]] })).toThrow(/icons may draw only/);
    });
  }

  it('refuses data that is not icon data', () => {
    const malformed: unknown[] = [
      'M0 0',
      { path: 'M0 0' },
      [['path']],
      [['path', { d: 'M0 0' }, [], 'extra']],
      [['path', ['d', 'M0 0']]],
      [['path', { d: { toString: () => 'M0 0' } }]],
      [['path', { 'd"': 'M0 0' }]],
      [['g', {}, 'children']],
      { node: Plus, mirrorInRtl: 'yes' },
    ];
    for (const data of malformed) {
      expect(
        () => registry().register({ bad: data as TmIconSource }),
        JSON.stringify(data),
      ).toThrow(/TmIconRegistry/);
    }
  });

  it('skips attributes whose value is undefined', () => {
    expect(() =>
      registry().register({ ok: [['path', { d: 'M0 0', fill: undefined }]] }),
    ).not.toThrow();
  });

  it('refuses names that are not kebab-case, suggesting the spelling', () => {
    expect(() => registry().register({ HandCoins: Plus })).toThrow(/write 'hand-coins'/);
    for (const name of ['hand_coins', 'hand--coins', '-hand', 'hand coins', '']) {
      expect(() => registry().register({ [name]: Plus }), name).toThrow(/kebab-case/);
    }
  });

  it('registers nothing from a batch that holds one refused icon', () => {
    expect(() => registry().register({ plus: Plus, bad: [['path', { onclick: 'x' }]] })).toThrow();
    expect(registry().get('plus')).toBeUndefined();
  });

  it('provideTellmaIcons registers when the injector is created; a later call wins', () => {
    TestBed.configureTestingModule({
      providers: [
        provideTellmaIcons({ close: Plus, plus: Plus }),
        provideTellmaIcons({ close: X }),
      ],
    });
    expect(registry().get('plus')?.node).toBe(Plus);
    expect(registry().get('close')?.node).toBe(X);
  });

  it('a lazily created injector adds its icons to the root registry', () => {
    expect(registry().get('chevron-right')).toBeUndefined();
    const lazy = createEnvironmentInjector(
      [provideTellmaIcons({ 'chevron-right': { node: ChevronRight, mirrorInRtl: true } })],
      TestBed.inject(EnvironmentInjector),
    );
    expect(registry().get('chevron-right')?.mirrorInRtl).toBe(true);
    lazy.destroy();
  });
});
