// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import type { TmJsonObject } from '@tellma/core';
import { type TmFakeDetails, tmFakeDetails } from '@tellma/core-testing';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';

// One record's state through the fake context a layout test uses: dirty by
// reference (a write copies the objects on its path, never the base), the
// form over the working copy, and the refusals of the `permissions` extra
// narrowing the abilities.

/** A customer, wire-shaped, as far as the tests read it. */
interface TmTestCustomer {
  /** The id; absent on a new customer. */
  readonly id?: number;
  /** The code. */
  readonly code?: string;
  /** The name; absent from a new customer until written. */
  readonly name: string;
  /** The contacts. */
  readonly contacts?: readonly { readonly id: number; readonly name: string }[];
}

/** A customer as `customers/get` returns it. */
const CUSTOMER: TmTestCustomer = { id: 5, code: 'C-005', name: 'Al Noor Trading', contacts: [{ id: 11, name: 'Huda' }] };

/**
 * A fake context over the customer, in view mode.
 *
 * @param extras - The read's extras.
 * @returns The context.
 */
function details(extras: TmJsonObject = {}): TmFakeDetails<TmTestCustomer> {
  return TestBed.runInInjectionContext(() => tmFakeDetails<TmTestCustomer>({ entity: 'acme.Customer', record: { entity: CUSTOMER, extras } }));
}

describe("a details record's state", () => {
  beforeEach(async () => {
    await tmEntityRig();
  });

  describe('dirty by reference', () => {
    it('is clean in edit mode until a write, with the server copy itself as both base and working copy', () => {
      const record = details();
      record.edit();

      expect(record.model()).toBe(record.entity());
      expect(record.model()).toEqual(expect.objectContaining({ id: 5, code: 'C-005', name: 'Al Noor Trading' }));
      expect(record.dirty()).toBe(false);
    });

    it("turns dirty on the form's write, which copies the objects on its path and leaves the base alone", () => {
      const record = details();
      record.edit();
      const server = record.entity()!;
      record.form.name().value.set('Al Noor');

      expect(record.dirty()).toBe(true);
      expect(record.model()).not.toBe(server);
      expect(record.model().contacts).toBe(server.contacts);
      expect(server.name).toBe('Al Noor Trading');
      expect(CUSTOMER.name).toBe('Al Noor Trading');
    });

    it('stays dirty when a write puts the old value back: the working copy is another object', () => {
      const record = details();
      record.edit();
      record.form.name().value.set('Al Noor');
      record.form.name().value.set('Al Noor Trading');

      expect(record.dirty()).toBe(true);
    });

    it('stays clean when update() returns the working copy itself, and is clean again on Cancel', async () => {
      const record = details();
      record.edit();
      record.update((model) => model);
      expect(record.dirty()).toBe(false);

      record.update((model) => ({ ...model, code: 'C-006' }));
      expect(record.dirty()).toBe(true);
      await record.cancel();
      expect([record.mode(), record.dirty(), record.model()]).toEqual(['view', false, expect.objectContaining({ id: 5, code: 'C-005', name: 'Al Noor Trading' })]);
    });

    it('is never dirty in view mode', () => {
      const record = details();

      expect(record.dirty()).toBe(false);
      expect(() => record.update((model) => ({ ...model, code: 'C-006' }))).toThrow(/edit and create mode/);
    });
  });

  it("narrows Edit, Delete and Activate by the record's refusals", () => {
    const record = details({ permissions: [{ id: 5, refused: { save: 'outside-filter', delete: 'Acme_Rule_Closed' } }] });

    expect(record.refused()).toEqual({ save: 'outside-filter', delete: 'Acme_Rule_Closed' });
    expect([record.can.edit(), record.can.delete(), record.can.activate(), record.can.create()]).toEqual([false, false, true, true]);
  });

  it('starts an edit-mode fake over the server copy, clean, with the copy as its base and working copy', () => {
    const record = TestBed.runInInjectionContext(() =>
      tmFakeDetails<TmTestCustomer>({ entity: 'acme.Customer', mode: 'edit', record: { entity: CUSTOMER } }),
    );

    expect([record.mode(), record.status(), record.dirty()]).toEqual(['edit', 'ready', false]);
    expect(record.entity()).toEqual(expect.objectContaining({ id: 5, code: 'C-005', name: 'Al Noor Trading' }));
    expect(record.model()).toBe(record.entity());
    record.form.name().value.set('Al Noor');
    expect(record.dirty()).toBe(true);
  });

  it('starts a new record from the create defaults, clean, with its base the start', () => {
    const record = TestBed.runInInjectionContext(() => tmFakeDetails<TmTestCustomer>({ entity: 'acme.Customer', mode: 'create' }));

    expect([record.mode(), record.id(), record.status(), record.dirty()]).toEqual(['create', null, 'ready', false]);
    expect(record.model()).toEqual(expect.objectContaining({ contacts: [], code: null, name: null }));
    record.update((model) => ({ ...model, name: 'New' }));
    expect(record.dirty()).toBe(true);
  });
});
