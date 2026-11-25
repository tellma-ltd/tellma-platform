// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmEntityDescriptor, TmJson, TmJsonObject } from '@tellma/core';

import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmDetailsCloneStrip, tmDetailsCreateModel, tmDetailsRefused, tmDetailsRouteId } from './tm-details-model';

// The record's model rules over the recorded entity model: a new record's
// values, the strip that turns a copy into a new record, the route's id, and
// a record's refusals in the `permissions` extra.

/**
 * A descriptor with more declaration members than the feature declares.
 *
 * @param descriptor - The descriptor.
 * @param members - The members.
 * @returns The descriptor, its declaration extended.
 */
function declaring(descriptor: TmEntityDescriptor, members: Record<string, unknown>): TmEntityDescriptor {
  return Object.create(descriptor, { declaration: { value: { ...descriptor.declaration, ...members } } }) as TmEntityDescriptor;
}

/** A customer as `customers/get` returns it, with a contact and an image. */
function customer(): TmJsonObject {
  return {
    id: 5,
    code: 'C-005',
    name: 'Al Noor Trading',
    customerType: 'Retail',
    creditLimit: '1500.00',
    imageId: 71,
    isActive: true,
    createdAt: '2026-11-01T08:00:00.0000000+00:00',
    createdById: 3,
    modifiedAt: '2026-11-02T08:00:00.0000000+00:00',
    modifiedById: 4,
    contacts: [
      { id: 11, customerId: 5, name: 'Huda', email: 'huda@example.com' },
      { id: 12, customerId: 5, name: 'Omar', email: null },
    ],
  };
}

describe('the details model rules', () => {
  let rig: TmEntityRig;

  beforeEach(async () => {
    rig = await tmEntityRig();
  });

  describe('a new record', () => {
    it('starts with every child collection empty and every non-nullable flag the client writes false', () => {
      // A role's IsPublic is the client's to write; its IsActive is the server's, so a new record leaves it out.
      expect(tmDetailsCreateModel(rig.entities.get('core.Role'))).toEqual({ permissions: [], isPublic: false });
      expect(tmDetailsCreateModel(rig.entities.get('acme.Customer'))).toEqual({ contacts: [] });
    });

    it("merges the declaration's defaults over them", () => {
      const role = declaring(rig.entities.get('core.Role'), { defaults: () => ({ isPublic: true, code: 'R' }) });

      expect(tmDetailsCreateModel(role)).toEqual({ permissions: [], isPublic: true, code: 'R' });
    });

    it('is a fresh object each time, so no edit reaches the next new record', () => {
      const descriptor = rig.entities.get('acme.Customer');

      expect(tmDetailsCreateModel(descriptor)).not.toBe(tmDetailsCreateModel(descriptor));
      expect(tmDetailsCreateModel(descriptor)['contacts']).not.toBe(tmDetailsCreateModel(descriptor)['contacts']);
    });
  });

  describe('the clone strip', () => {
    it('drops the key and what the server writes, nulls the blobs, and keeps what the user wrote', () => {
      const copy = tmDetailsCloneStrip(customer(), rig.entities.get('acme.Customer'), (name) => rig.entities.get(name));

      expect(copy).toEqual({
        code: 'C-005',
        name: 'Al Noor Trading',
        customerType: 'Retail',
        creditLimit: '1500.00',
        imageId: null,
        contacts: [
          { id: -1, name: 'Huda', email: 'huda@example.com' },
          { id: -2, name: 'Omar', email: null },
        ],
      });
    });

    it('copies, so the source stays as it was', () => {
      const source = customer();
      const before = structuredClone(source);
      const copy = tmDetailsCloneStrip(source, rig.entities.get('acme.Customer'), (name) => rig.entities.get(name));

      expect(source).toEqual(before);
      expect(copy['contacts']).not.toBe(source['contacts']);
    });

    it("gives the declaration's clone the stripped copy and the untouched source, and takes what it returns", () => {
      const clone = vi.fn((copy: TmJsonObject, source: TmJsonObject) => ({ ...copy, code: null, name: `${String(source['name'])} (copy)` }));
      const descriptor = declaring(rig.entities.get('acme.Customer'), { clone });

      const copy = tmDetailsCloneStrip(customer(), descriptor, (name) => rig.entities.get(name));

      expect(clone).toHaveBeenCalledWith(expect.objectContaining({ code: 'C-005', imageId: null }), customer());
      expect(copy).toMatchObject({ code: null, name: 'Al Noor Trading (copy)' });
    });

    it('numbers child rows across every collection of the record, as new rows are numbered', () => {
      const user: TmJsonObject = {
        id: 9,
        kind: 'Individual',
        name: 'Farah',
        imageId: 4,
        signatureId: 5,
        state: 'Joined',
        roleMemberships: [
          { id: 1, userId: 9, roleId: 2 },
          { id: 2, userId: 9, roleId: 3 },
        ],
      };

      const copy = tmDetailsCloneStrip(user, rig.entities.get('core.User'), (name) => rig.entities.get(name));

      // Kind is write-once and stays; State is the database's and goes.
      expect(copy).toEqual({
        kind: 'Individual',
        name: 'Farah',
        imageId: null,
        signatureId: null,
        roleMemberships: [
          { id: -1, roleId: 2 },
          { id: -2, roleId: 3 },
        ],
      });
    });
  });

  describe('the clone strip, through a grandchild collection', () => {
    it("strips a child's own collection the same way, numbering every row in reading order", () => {
      const customerDescriptor = rig.entities.get('acme.Customer');
      const [contacts] = customerDescriptor.children;
      const contact = rig.entities.get(contacts!.entity);
      // The recorded model has no grandchild, so the contact gains a collection of the role's permission rows.
      const [permissions] = rig.entities.get('core.Role').children;
      const notes = { ...permissions!, json: 'notes', parentKey: 'ContactId' };
      const withNotes = Object.create(contact, { children: { value: [notes] } }) as TmEntityDescriptor;
      const resolve = (name: string): TmEntityDescriptor => (name === contact.name ? withNotes : rig.entities.get(name));
      const source: TmJsonObject = {
        ...customer(),
        contacts: [
          { id: 11, customerId: 5, name: 'Huda', notes: [{ id: 21, contactId: 11, text: 'Calls in the morning' }] },
          { id: 12, customerId: 5, name: 'Omar', notes: [{ id: 22, contactId: 12, text: 'Prefers email' }, { id: 23, contactId: 12, text: 'Arabic' }] },
        ],
      };

      const copy = tmDetailsCloneStrip(source, customerDescriptor, resolve);

      expect(copy['contacts']).toEqual([
        { id: -1, name: 'Huda', notes: [{ id: -2, text: 'Calls in the morning' }] },
        { id: -3, name: 'Omar', notes: [{ id: -4, text: 'Prefers email' }, { id: -5, text: 'Arabic' }] },
      ]);
    });
  });

  describe("the route's id", () => {
    it('reads a positive integer, and nothing else', () => {
      expect(tmDetailsRouteId('5', 'int')).toBe(5);
      expect(tmDetailsRouteId('5', 'long')).toBe(5);
      for (const text of ['new', '0', '05', '-5', '5.0', '', undefined, null]) {
        expect(tmDetailsRouteId(text, 'int')).toBeNull();
      }
    });

    it("keeps a long key's id past the safe integers exact as its digits", () => {
      expect(tmDetailsRouteId('9007199254740993', 'long')).toBe('9007199254740993');
      expect(tmDetailsRouteId('9007199254740993', 'int')).toBeNull();
    });
  });

  describe("a record's refusals", () => {
    it("reads the record's row of the permissions extra", () => {
      const extras: Readonly<Record<string, TmJson>> = {
        permissions: [
          { id: 4, refused: { delete: 'outside-filter' } },
          { id: 5, refused: { save: 'Acme_Rule_Closed' } },
        ],
      };

      expect(tmDetailsRefused(extras, 5)).toEqual({ save: 'Acme_Rule_Closed' });
      expect(tmDetailsRefused(extras, 6)).toEqual({});
      expect(tmDetailsRefused({}, 5)).toEqual({});
      expect(tmDetailsRefused(extras, null)).toEqual({});
    });
  });
});
