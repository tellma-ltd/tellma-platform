// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmMembership } from '@tellma/core';

import { tmTestMembership } from '../../../src/routes/tm-app.testing.util';
import { tmTenantNames } from './tm-tenant-names';

// The name slot each language picks, and the other names in language order.

/** A tenant with three content languages and a name in each. */
const TRILINGUAL: TmMembership = tmTestMembership(1, {
  name: 'Acme',
  name2: 'أكمي',
  name3: 'Acmé',
  languages: ['en', 'ar-SA', 'fr'],
});

describe('tmTenantNames', () => {
  it("shows the name at the active language's position, with the others in language order", () => {
    expect(tmTenantNames(TRILINGUAL, 'en')).toEqual({
      name: { text: 'Acme', lang: 'en' },
      others: [
        { text: 'أكمي', lang: 'ar-SA' },
        { text: 'Acmé', lang: 'fr' },
      ],
    });
    expect(tmTenantNames(TRILINGUAL, 'fr').name).toEqual({ text: 'Acmé', lang: 'fr' });
  });

  it('matches the active language to a content language by language subtag', () => {
    expect(tmTenantNames(TRILINGUAL, 'ar').name).toEqual({ text: 'أكمي', lang: 'ar-SA' });
    expect(tmTenantNames(TRILINGUAL, 'EN-gb').name.text).toBe('Acme');
  });

  it('shows the first name without a match, and when the matching name is empty', () => {
    expect(tmTenantNames(TRILINGUAL, 'am').name.text).toBe('Acme');
    const blank = tmTestMembership(2, { name: 'Beta', name2: '  ', languages: ['en', 'ar'] });
    expect(tmTenantNames(blank, 'ar')).toEqual({ name: { text: 'Beta', lang: 'en' }, others: [] });
  });

  it('leaves the language unmarked for a name the tenant lists no language for', () => {
    expect(tmTenantNames(tmTestMembership(3, { name: 'Gamma', languages: [] }), 'en')).toEqual({
      name: { text: 'Gamma', lang: null },
      others: [],
    });
  });
});
