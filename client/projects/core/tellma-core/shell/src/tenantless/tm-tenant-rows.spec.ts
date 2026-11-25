// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmTestMembership } from '../../../src/routes/tm-app.testing.util';
import { tmTenantCountBucket, tmTenantRows, tmTenantSearch, tmTenantUnavailable } from './tm-tenant-rows';

// The picker's rows as pure functions: the badges and the unavailable
// reasons, the live-then-sandbox order, and the search with its dimmed live
// parents.

/** The names of rows, in order. */
const names = (rows: readonly { readonly name: { readonly text: string } }[]): string[] => rows.map((row) => row.name.text);

describe('tmTenantRows', () => {
  it('lists live tenants by name, each followed by its sandboxes, then the unattached sandboxes, and no retired tenant', () => {
    const rows = tmTenantRows(
      [
        tmTestMembership(9, { name: 'Alpha Orphan', category: 'Sandbox', liveTenantId: 99 }),
        tmTestMembership(3, { name: 'Zeta' }),
        tmTestMembership(4, { name: 'Beta Sandbox', category: 'Sandbox', liveTenantId: 3 }),
        tmTestMembership(2, { name: 'Acme Sandbox', category: 'Sandbox', liveTenantId: 1 }),
        tmTestMembership(1, { name: 'Acme' }),
        tmTestMembership(5, { name: 'Old', state: 'Retired' }),
      ],
      'en',
      null,
    );
    expect(names(rows)).toEqual(['Acme', 'Acme Sandbox', 'Zeta', 'Beta Sandbox', 'Alpha Orphan']);
    expect(rows.map((row) => row.nested)).toEqual([false, true, false, true, false]);
  });

  it("sorts by the shown names in the active language's collation, the id breaking a tie", () => {
    const rows = tmTenantRows(
      [
        tmTestMembership(2, { name: 'b', name2: 'أ', languages: ['en', 'ar'] }),
        tmTestMembership(1, { name: 'a', name2: 'ب', languages: ['en', 'ar'] }),
        tmTestMembership(4, { name: 'c', name2: 'ت', languages: ['en', 'ar'] }),
        tmTestMembership(3, { name: 'd', name2: 'ت', languages: ['en', 'ar'] }),
      ],
      'ar',
      null,
    );
    expect(rows.map((row) => row.membership.id)).toEqual([2, 1, 3, 4]);
  });

  it('gives each row its badges in order and the reason its Connect is unavailable', () => {
    const rows = tmTenantRows(
      [
        tmTestMembership(1, { name: 'A', category: 'Sandbox', state: 'ReadOnly' }),
        tmTestMembership(2, { name: 'B', state: 'Frozen' }),
        tmTestMembership(3, { name: 'C', state: 'Suspended' }),
        tmTestMembership(4, { name: 'D', state: 'Provisioning' }),
        tmTestMembership(5, { name: 'E', state: 'Suspended', isActive: false }),
        tmTestMembership(6, { name: 'F' }),
      ],
      'en',
      6,
    );
    const byId = new Map(rows.map((row) => [row.membership.id, row]));
    expect([1, 2, 3, 4, 5, 6].map((id) => [byId.get(id)?.badges, byId.get(id)?.unavailable])).toEqual([
      [['sandbox', 'readOnly'], null],
      [['frozen'], null],
      [['suspended'], 'suspended'],
      [['provisioning'], 'provisioning'],
      [['suspended', 'inactive'], 'inactive'],
      [['lastUsed'], null],
    ]);
  });
});

describe('tmTenantUnavailable', () => {
  it('puts a removed membership before the tenant state', () => {
    expect(tmTenantUnavailable(tmTestMembership(1, { state: 'Provisioning', isActive: false }))).toBe('inactive');
    expect(tmTenantUnavailable(tmTestMembership(1, { state: 'Active' }))).toBeNull();
  });
});

describe('tmTenantSearch', () => {
  const rows = tmTenantRows(
    [
      tmTestMembership(1, { name: 'Acme', name2: 'الأكمي للتجارة', languages: ['en', 'ar'] }),
      tmTestMembership(2, { name: 'Training copy', category: 'Sandbox', liveTenantId: 1 }),
      tmTestMembership(17, { name: 'Café Zeta' }),
    ],
    'en',
    null,
  );

  it('shows every row while nothing is typed', () => {
    expect(names(tmTenantSearch(rows, '  ').shown.map(({ row }) => row))).toEqual(['Acme', 'Training copy', 'Café Zeta']);
  });

  it('matches any name through the search folding, and the id exactly', () => {
    expect(names(tmTenantSearch(rows, 'CAFE').matches)).toEqual(['Café Zeta']);
    expect(names(tmTenantSearch(rows, 'الاكمي').matches)).toEqual(['Acme']);
    expect(names(tmTenantSearch(rows, '١٧').matches)).toEqual(['Café Zeta']);
    expect(names(tmTenantSearch(rows, '1').matches)).toEqual(['Acme']);
    expect(tmTenantSearch(rows, 'nothing')).toEqual({ shown: [], matches: [] });
  });

  it("keeps a matching sandbox's live tenant as a dimmed row above it", () => {
    const { shown, matches } = tmTenantSearch(rows, 'training');
    expect(shown.map(({ row, dimmed }) => [row.name.text, dimmed])).toEqual([
      ['Acme', true],
      ['Training copy', false],
    ]);
    expect(names(matches)).toEqual(['Training copy']);
  });
});

describe('tmTenantCountBucket', () => {
  it('buckets the count as the telemetry reports it', () => {
    expect([0, 1, 2, 6, 7, 40].map(tmTenantCountBucket)).toEqual(['0', '1', '2-6', '2-6', '7+', '7+']);
  });
});
