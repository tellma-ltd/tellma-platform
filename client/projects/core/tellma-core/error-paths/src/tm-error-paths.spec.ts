// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form, type FieldTree } from '@angular/forms/signals';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSplitErrorPath, tmWalkErrorPath } from './tm-error-paths';

// The server's error paths onto a form: the splitter over the paths the
// server writes and the ones it never writes, and the walk through a
// customer's form, guided by the entity (into a child collection's rows),
// stopping at an unknown segment, standing at the root, and unguided over an
// action's arguments.

/**
 * A form over a model, in an injection context.
 *
 * @param model - The model.
 * @returns The form's root field.
 */
function formOf<T>(model: T): FieldTree<T> {
  return TestBed.runInInjectionContext(() => form(signal(model)));
}

describe('tmSplitErrorPath', () => {
  it('splits members and indices, as the server writes them', () => {
    expect(tmSplitErrorPath('entities[0].lines[1].amount')).toEqual(['entities', 0, 'lines', 1, 'amount']);
    expect(tmSplitErrorPath('entities[3].roleMemberships[1].roleId')).toEqual(['entities', 3, 'roleMemberships', 1, 'roleId']);
    expect(tmSplitErrorPath('arguments.creditLimit')).toEqual(['arguments', 'creditLimit']);
    expect(tmSplitErrorPath('ids[2]')).toEqual(['ids', 2]);
    expect(tmSplitErrorPath('id')).toEqual(['id']);
    expect(tmSplitErrorPath('body')).toEqual(['body']);
    expect(tmSplitErrorPath('filters[12]')).toEqual(['filters', 12]);
  });

  it('refuses an empty or malformed path', () => {
    const malformed = ['', '.name', 'name.', 'a..b', 'a[x]', 'a.[1]', 'a[1]b', 'a[1', 'a b', 'lines[-1]', '1abc'];
    expect(malformed.filter((path) => tmSplitErrorPath(path) !== null)).toEqual([]);
  });
});

describe('tmWalkErrorPath', () => {
  /** A customer's working copy with two contacts. */
  const customer = (): Record<string, unknown> => ({
    code: 'C1',
    creditLimit: '1500',
    contacts: [
      { name: 'Huda', email: 'huda@acme.test' },
      { name: 'Omar', email: '' },
    ],
    // A member the model holds that is no member of the entity.
    draftNote: 'local',
  });

  it('walks into a child collection’s row, naming its entity and the PascalCase path', async () => {
    const { entities } = await tmEntityRig();
    const root = formOf(customer());
    const segments = tmSplitErrorPath('entities[0].contacts[1].email')!.slice(2);
    const target = tmWalkErrorPath(root as FieldTree<unknown>, segments, { entity: 'acme.Customer', entities });
    expect(target.resolved).toBe(true);
    expect(target.path).toEqual(['Contacts', 1, 'Email']);
    expect(target.entity).toBe('acme.CustomerContact');
    expect(target.field).toBe((root as unknown as { contacts: Record<string, Record<string, FieldTree<unknown>>> }).contacts['1']!['email']!);
    expect(target.field().value()).toBe('');
  });

  it('stops at the deepest field reached when a segment does not resolve', async () => {
    const { entities } = await tmEntityRig();
    const root = formOf(customer()) as FieldTree<unknown>;
    const contacts = (root as unknown as Record<string, FieldTree<unknown>>)['contacts']!;
    const options = { entity: 'acme.Customer', entities };

    // A row the list does not hold.
    const missingRow = tmWalkErrorPath(root, ['contacts', 5, 'email'], options);
    expect(missingRow).toEqual({ field: contacts, resolved: false, path: ['Contacts'], entity: 'acme.Customer' });

    // A member the entity does not have, though the model holds it.
    const unknownMember = tmWalkErrorPath(root, ['draftNote'], options);
    expect(unknownMember).toEqual({ field: root, resolved: false, path: [], entity: 'acme.Customer' });

    // A member of the child entity the row does not hold.
    const childMember = tmWalkErrorPath(root, ['contacts', 0, 'customerId'], options);
    expect(childMember.resolved).toBe(false);
    expect(childMember.path).toEqual(['Contacts', 0]);
    expect(childMember.entity).toBe('acme.CustomerContact');

    // An index into a scalar, and a member of a list.
    expect(tmWalkErrorPath(root, ['code', 0], options).path).toEqual(['Code']);
    expect(tmWalkErrorPath(root, ['contacts', 'length']).resolved).toBe(false);
  });

  it('stands at the root for a path with no segments', async () => {
    const { entities } = await tmEntityRig();
    const root = formOf(customer()) as FieldTree<unknown>;
    expect(tmWalkErrorPath(root, [], { entity: 'acme.Customer', entities })).toEqual({ field: root, resolved: true, path: [], entity: 'acme.Customer' });
    expect(tmWalkErrorPath(root, [])).toEqual({ field: root, resolved: true, path: [] });
  });

  it('walks an action’s arguments by their wire names, with no entity', () => {
    const root = formOf({ creditLimit: '250000', reviewers: [7, 9] }) as FieldTree<unknown>;
    const fields = root as unknown as Record<string, Record<string, FieldTree<unknown>>>;

    const limit = tmWalkErrorPath(root, tmSplitErrorPath('arguments.creditLimit')!.slice(1));
    expect(limit).toEqual({ field: fields['creditLimit'] as unknown as FieldTree<unknown>, resolved: true, path: ['creditLimit'] });

    const reviewer = tmWalkErrorPath(root, tmSplitErrorPath('arguments.reviewers[1]')!.slice(1));
    expect(reviewer.resolved).toBe(true);
    expect(reviewer.field).toBe(fields['reviewers']!['1']!);
    expect(reviewer.field().value()).toBe(9);

    const unknown = tmWalkErrorPath(root, ['priority']);
    expect(unknown).toEqual({ field: root, resolved: false, path: [] });
  });
});
