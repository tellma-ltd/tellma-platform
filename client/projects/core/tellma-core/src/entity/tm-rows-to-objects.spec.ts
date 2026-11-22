// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import type { TmEntityDescriptor } from './tm-entity-descriptors';
import { tmEntityRig } from './tm-entity.testing.util';
import { tmRowsToObjects, type TmQueryRowSet, type TmQueryRowSetColumn } from './tm-rows-to-objects';

// A query's row set as wire objects: paths nest by the JSON names of the
// navigations they cross, and expression columns are left out.

/** A column of a row set. */
function column(name: string, path?: readonly string[]): TmQueryRowSetColumn {
  return { name, type: 'String', nullable: true, groupingKey: false, ...(path && { path }) };
}

/** Centers with their parents' codes and names, and a computed column. */
const CENTERS: TmQueryRowSet = {
  columns: [
    column('Id', ['Id']),
    column('Code', ['Code']),
    column('Parent.Id', ['Parent', 'Id']),
    column('Parent.Code', ['Parent', 'Code']),
    column('Parent.Manager.Name2', ['Parent', 'Manager', 'Name2']),
    column('Total'),
  ],
  rows: [
    [5, 'E-01', 1, 'R-1', 'فرح', '12.50'],
    [6, 'E-02', null, null, null, '0.00'],
  ],
};

describe('tmRowsToObjects', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('nests each path by the JSON names of its segments and skips expression columns', async () => {
    const { entities } = await tmEntityRig({ languages: 2 });

    expect(tmRowsToObjects(entities.get('gl.Center'), CENTERS)).toEqual([
      { id: 5, code: 'E-01', parent: { id: 1, code: 'R-1', manager: { name2: 'فرح' } } },
      { id: 6, code: 'E-02', parent: { id: null, code: null, manager: { name2: null } } },
    ]);
  });

  it('reads the wire names from the metadata, not the convention', async () => {
    const { entities } = await tmEntityRig();
    const center = entities.get('gl.Center');
    // A descriptor whose navigation's wire name breaks the convention.
    // The descriptor is frozen, so the override is defined on a derived object.
    const renamed = Object.create(center, {
      ɵjsonPath: {
        value: (path: readonly string[]) => (path[0] === 'Parent' ? ['up', ...path.slice(1).map((segment) => segment.toLowerCase())] : null),
      },
    }) as TmEntityDescriptor;

    expect(tmRowsToObjects(renamed, { columns: [column('Parent.Code', ['Parent', 'Code'])], rows: [['R-1']] })).toEqual([{ up: { code: 'R-1' } }]);
  });

  it('falls back to camelCase for a descriptor without the metadata', () => {
    const bare = { name: 'acme.Thing' } as TmEntityDescriptor;
    expect(tmRowsToObjects(bare, { columns: [column('Owner.FullName', ['Owner', 'FullName'])], rows: [['Farah']] })).toEqual([
      { owner: { fullName: 'Farah' } },
    ]);
  });
});
