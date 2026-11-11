// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmIconSource } from '@tellma/core-ui/icon';

import { tmWidget } from '../dashboards/tm-widget';
import { tmEntity } from '../entity/tm-entity-declaration';
import { tmCodedReport } from '../reports/tm-coded-report';
import { tmDeclarationKindOf } from './tm-declaration-registry';
import type { TmLazyComponent } from './tm-declaration-types';
import { tmFeature } from './tm-feature';
import { tmOverride } from './tm-override';
import { tmPage } from './tm-page';

/** A lazy component that is never loaded. */
const lazy: TmLazyComponent = () => Promise.resolve(class {});

describe('the declaration functions', () => {
  it('return frozen plain objects of their declared shape', () => {
    const page = tmPage('credit-review', { component: lazy, titleKey: 'Acme_CreditReview' });
    expect(page).toEqual({ component: lazy, titleKey: 'Acme_CreditReview', path: 'credit-review' });
    expect(Object.isFrozen(page)).toBe(true);
    expect(Object.getPrototypeOf(page)).toBe(Object.prototype);

    expect(tmEntity('acme.Customer')).toEqual({ name: 'acme.Customer' });
    expect(tmEntity('acme.Customer', { order: 3 })).toEqual({ order: 3, name: 'acme.Customer' });
    expect(tmCodedReport('acme-credit-exposure', { order: 1 })).toEqual({ order: 1, key: 'acme-credit-exposure' });
    expect(tmOverride('section:administration', { order: 10 })).toEqual({ key: 'section:administration', members: { order: 10 } });
    expect(Object.isFrozen(tmOverride('section:administration', { order: 10 }).members)).toBe(true);
  });

  it('let the key argument win over a key member in the definition', () => {
    const entity = tmEntity('acme.Customer', { name: 'acme.Other' });
    expect(entity.name).toBe('acme.Customer');
  });

  it('record the kind of each value they make, and of no copy of one', () => {
    const widget = tmWidget('acme.cash-position', {
      labelKey: 'W',
      icon: 'wallet',
      component: lazy,
      defaultSize: { w: 4, h: 4 },
      minSize: { w: 3, h: 3 },
    });
    expect(tmDeclarationKindOf(widget)).toBe('widget');
    expect(tmDeclarationKindOf(tmEntity('acme.Customer'))).toBe('entity');
    expect(tmDeclarationKindOf(tmPage('x', { component: lazy, titleKey: 'T' }))).toBe('page');
    expect(tmDeclarationKindOf(tmCodedReport('a-b', {}))).toBe('report');
    expect(tmDeclarationKindOf(tmOverride('page:x', {}))).toBe('override');
    expect(tmDeclarationKindOf({ ...widget })).toBeUndefined();
    expect(tmDeclarationKindOf(null)).toBeUndefined();
  });

  it('share one record with every other copy of the package in the realm', () => {
    // A second evaluation of this package reaches the same record through the
    // registered symbol; it is simulated here by reading and writing it directly.
    const shared = (globalThis as unknown as Record<symbol, WeakMap<object, string> | undefined>)[
      Symbol.for('tellma.core.declarationKinds')
    ];
    const entity = tmEntity('acme.Customer');
    expect(shared?.get(entity)).toBe('entity');

    const fromAnotherCopy = Object.freeze({ path: 'credit-review' });
    shared?.set(fromAnotherCopy, 'page');
    expect(tmDeclarationKindOf(fromAnotherCopy)).toBe('page');
  });
});

describe('tmFeature', () => {
  it('copies and freezes the definition, its lists and its icons', () => {
    const sections = [{ id: 'sales', labelKey: 'Nav_Sales', icon: 'users', order: 20 }];
    const icons: Readonly<Record<string, TmIconSource>> = { coins: [['circle', { cx: 12, cy: 12, r: 8 }]] };
    const feature = tmFeature({ key: 'acme', sections, icons });
    sections.push({ id: 'later', labelKey: 'L', icon: 'x', order: 1 });

    expect(feature.sections?.map((section) => section.id)).toEqual(['sales']);
    expect(Object.isFrozen(feature)).toBe(true);
    expect(Object.isFrozen(feature.sections)).toBe(true);
    expect(Object.isFrozen(feature.icons)).toBe(true);
    expect('pages' in feature).toBe(false);
  });
});
