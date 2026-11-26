// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type FieldTree, form, required } from '@angular/forms/signals';
import type { TmEntityDescriptor, TmJsonObject, TmPropertyDescriptor } from '@tellma/core';

import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import type { TmDetailsMode } from './tm-details-context';
import { tmDetailsComplete } from './tm-details-model';
import { tmDetailsPlatformSchema, type TmDetailsSchemaOptions, type TmDetailsServerError } from './tm-details-schema';

// The platform's validation rules over the recorded customer model: what
// is required and what is not, the maximum length, the digits a precision
// leaves, the read-only members, a pending upload, the recorded server
// errors, and the rules every child row takes.

/** A customer, wire-shaped, as far as a distribution's rule reads it. */
interface TmTestCustomer {
  /** The notes. */
  readonly notes: string | null;
  /** The customer's type. */
  readonly customerType: string | null;
}

/** A customer's form, as the kit's record builds it: the root's state, and a node per member. */
interface TmTestForm {
  /** The root's node. */
  readonly root: FieldTree<unknown>;
  /**
   * A member's node.
   *
   * @param path - Json names and row indices.
   * @returns The node.
   */
  at(...path: readonly (string | number)[]): FieldTree<unknown>;
}

/**
 * A form's nodes, by path.
 *
 * @param root - The form.
 * @returns Its nodes.
 */
function nodes(root: FieldTree<unknown>): TmTestForm {
  return {
    root,
    at: (...path) => path.reduce<FieldTree<unknown>>((node, segment) => (node as unknown as Readonly<Record<string | number, FieldTree<unknown>>>)[segment]!, root),
  };
}

/**
 * A descriptor whose properties are rewritten.
 *
 * @param descriptor - The descriptor.
 * @param rewrite - Rewrites one property.
 * @returns The descriptor, its properties rewritten.
 */
function rewriting(descriptor: TmEntityDescriptor, rewrite: (property: TmPropertyDescriptor) => TmPropertyDescriptor): TmEntityDescriptor {
  return Object.create(descriptor, { properties: { value: descriptor.properties.map(rewrite) } }) as TmEntityDescriptor;
}

/** The error kinds a node shows. */
function kinds(node: FieldTree<unknown>): string[] {
  return node()
    .errors()
    .map((error) => error.kind);
}

describe("the details page's platform schema", () => {
  let rig: TmEntityRig;

  beforeEach(async () => {
    // Two content languages, so the name's second slot is a member of the tenant's shape.
    rig = await tmEntityRig({ languages: 2 });
  });

  /**
   * A customer's form under the platform schema, completed as the record completes it.
   *
   * @param entity - The customer.
   * @param options - The schema's options; the customer entity's children by default.
   * @param descriptor - The customer's descriptor.
   * @returns The form and its model.
   */
  function customerForm(
    entity: TmJsonObject,
    options: TmDetailsSchemaOptions = {},
    descriptor = rig.entities.get('acme.Customer'),
  ): { readonly tree: TmTestForm; readonly model: WritableSignal<TmJsonObject> } {
    const child = (name: string): TmEntityDescriptor => rig.entities.get(name);
    const model = signal<TmJsonObject>(tmDetailsComplete(entity, descriptor, child));
    const tree = TestBed.runInInjectionContext(() => form(model as WritableSignal<unknown>, (p) => tmDetailsPlatformSchema(p, descriptor, { child, ...options })));
    return { tree: nodes(tree), model };
  }

  describe('required', () => {
    it('requires what is not nullable, and leaves a nullable member, a twin and a defaulted member optional', () => {
      const { tree } = customerForm({});

      expect(kinds(tree.at('code'))).toContain('required');
      expect(kinds(tree.at('name'))).toContain('required');
      expect(kinds(tree.at('customerType'))).toContain('required');
      expect(kinds(tree.at('name2'))).toEqual([]);
      expect(kinds(tree.at('currencyId'))).toEqual([]);
      expect(kinds(tree.at('rank'))).toEqual([]);
    });

    it('never requires a flag, whose false is a value', () => {
      const descriptor = rewriting(rig.entities.get('acme.Customer'), (property) =>
        property.name === 'IsActive' ? { ...property, ownership: 'editable' } : property,
      );
      const { tree } = customerForm({ isActive: false }, {}, descriptor);

      expect(kinds(tree.at('isActive'))).toEqual([]);
    });

    it('requires a member the read omitted, which the working copy holds as null', () => {
      const { tree, model } = customerForm({ id: 5, name: 'Al Noor' });

      expect(model()['code']).toBeNull();
      expect(kinds(tree.at('code'))).toEqual(['required']);
      expect(kinds(tree.at('name'))).toEqual([]);
    });
  });

  describe('the maximum length and the digits', () => {
    it('refuses text longer than the maximum length', () => {
      const { tree } = customerForm({ code: 'C'.repeat(51) });

      expect(kinds(tree.at('code'))).toEqual(['maxLength']);

      tree.at('code')().value.set('C'.repeat(50));
      expect(kinds(tree.at('code'))).toEqual([]);
    });

    it('refuses more digits before the decimal point than the precision leaves, counting none of the leading zeros', () => {
      const { tree } = customerForm({ creditLimit: '1234567890123456.5' });

      expect(tree.at('creditLimit')().errors()).toEqual([expect.objectContaining({ kind: 'precision', maxDigits: 15 })]);

      tree.at('creditLimit')().value.set('-000123456789012345.5000');
      expect(kinds(tree.at('creditLimit'))).toEqual([]);

      tree.at('creditLimit')().value.set(1_000_000_000_000_000);
      expect(kinds(tree.at('creditLimit'))).toEqual(['precision']);
    });
  });

  describe('read-only', () => {
    it('makes what the server, the database or a derivation writes read-only, with no other rule', () => {
      const { tree } = customerForm({ isActive: true, createdAt: '2026-11-01T08:00:00.0000000+00:00' });

      expect(tree.at('isActive')().readonly()).toBe(true);
      expect(tree.at('createdAt')().readonly()).toBe(true);
      expect(kinds(tree.at('createdAt'))).toEqual([]);
      expect(tree.at('code')().readonly()).toBe(false);
    });

    it('makes a write-once member read-only outside create mode', () => {
      const descriptor = rewriting(rig.entities.get('acme.Customer'), (property) =>
        property.name === 'Code' ? { ...property, ownership: 'write-once' } : property,
      );
      const mode = signal<TmDetailsMode>('create');
      const { tree } = customerForm({ code: 'C-1' }, { mode }, descriptor);

      expect(tree.at('code')().readonly()).toBe(false);

      mode.set('edit');
      expect(tree.at('code')().readonly()).toBe(true);
    });
  });

  describe('a pending upload', () => {
    it('keeps a blob member pending while its upload runs, and settles it when the upload leaves', () => {
      const descriptor = rig.entities.get('acme.Customer');
      const running = signal<ReadonlySet<string>>(new Set());
      const { tree } = customerForm({ code: 'C-1', name: 'Al Noor', customerType: 'Retail', creditLimit: '1' }, { uploads: { running } }, descriptor);
      const image = tree.at('imageId');

      expect(descriptor.property('ImageId')?.blob).toBeDefined();
      expect(image().pending()).toBe(false);

      running.set(new Set(['imageId']));
      TestBed.tick();
      expect(image().pending()).toBe(true);
      expect(tree.root().pending()).toBe(true);

      running.set(new Set());
      TestBed.tick();
      expect(image().pending()).toBe(false);
    });
  });

  describe('the recorded errors', () => {
    it("shows each on the node its path reaches while the node keeps the value it had, and on the nearest node a path's end lacks", () => {
      const errors = signal<readonly TmDetailsServerError[]>([
        { segments: ['code'], value: 'C-1', error: { kind: 'server', message: 'The code is taken.' } },
        { segments: ['contacts', 0, 'phone'], value: undefined, error: { kind: 'server', message: 'The phone is missing.' } },
      ]);
      const { tree } = customerForm({ code: 'C-1', contacts: [{ id: 1, name: 'Huda' }] }, { errors });
      const contact = tree.at('contacts', 0);

      expect(tree.at('code')().errors()).toEqual([expect.objectContaining({ kind: 'server', message: 'The code is taken.' })]);
      expect(contact().errors()).toEqual([]);

      tree.at('code')().value.set('C-2');
      expect(kinds(tree.at('code'))).toEqual([]);

      tree.at('code')().value.set('C-1');
      expect(kinds(tree.at('code'))).toEqual(['server']);
    });
  });

  describe('the child rows', () => {
    it("gives every row its entity's rules, and the key to the parent none, since the server writes it", () => {
      const { tree } = customerForm({ contacts: [{ id: 1, name: '' }] });

      expect(kinds(tree.at('contacts', 0, 'name'))).toEqual(['required']);
      expect(kinds(tree.at('contacts', 0, 'customerId'))).toEqual([]);
      expect(kinds(tree.at('contacts', 0, 'email'))).toEqual([]);
    });
  });

  describe("the distribution's schema", () => {
    it("adds its rules after the platform's", () => {
      const descriptor = rig.entities.get('acme.Customer');
      const model = signal(tmDetailsComplete({ customerType: 'Corporate' }, descriptor, () => undefined) as unknown as TmTestCustomer);
      const tree = TestBed.runInInjectionContext(() =>
        form(model, (p) => {
          tmDetailsPlatformSchema(p, descriptor);
          required(p.notes, { when: ({ valueOf }) => valueOf(p.customerType) === 'Corporate' });
        }),
      );

      expect(kinds(nodes(tree).at('notes'))).toEqual(['required']);
      expect(kinds(nodes(tree).at('code'))).toEqual(['required']);
    });
  });
});
