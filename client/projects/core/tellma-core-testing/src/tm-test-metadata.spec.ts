// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TM_CLIENT_METADATA, type TmEnumValueMetadata, type TmPropertyMetadata } from '@tellma/core';

import { provideTellmaTesting } from './provide-tellma-testing';
import { tmTestEntities } from './tm-test-entities';
import { tmTestEntitiesChunk, tmTestMetadata } from './tm-test-metadata';

describe('the test client metadata', () => {
  it('has Core and Entities chunks that agree', () => {
    const metadata = tmTestMetadata();
    const chunk = tmTestEntitiesChunk();
    const names = chunk.entities.map((entity) => entity.name);

    expect(metadata.entities.map((summary) => summary.name)).toEqual(names);
    expect([...names].sort()).toEqual(names);
    // Every reference stays inside the chunk: navigation targets, child entities, and the
    // entities and named enums that properties and action fields name.
    const enums = chunk.enums.map((item) => item.name);
    for (const entity of chunk.entities) {
      for (const navigation of entity.navigations) {
        expect(names).toContain(navigation.target);
        expect(entity.properties.map((property) => property.name)).toContain(navigation.foreignKey);
      }
      for (const child of entity.children) {
        expect(names).toContain(child.entity);
      }
      for (const property of entity.properties) {
        if (property.enum !== undefined) {
          expect(enums).toContain(property.enum);
        }
      }
      for (const field of entity.actions.flatMap((action) => action.fields ?? [])) {
        if (field.value.enum !== undefined) {
          expect(enums).toContain(field.value.enum);
        }
        if (field.value.target !== undefined) {
          expect(names).toContain(field.value.target);
        }
      }
    }
    expect(metadata.formatVersion).toBe(1);
  });

  it("carries the platform's entities and enums exactly as the server emits them", () => {
    const recorded = tmTestEntities();
    const platform = <T extends { readonly name: string }>(items: readonly T[]): T[] => items.filter((item) => item.name.startsWith('core.'));

    expect(platform(tmTestMetadata().entities)).toEqual(platform(recorded.summaries));
    expect(platform(tmTestEntitiesChunk().entities)).toEqual(platform(recorded.chunk.entities));
    expect(tmTestEntitiesChunk().enums).toEqual(platform(recorded.chunk.enums));
    expect(platform(tmTestMetadata().entities).map((summary) => summary.name)).toEqual(expect.arrayContaining(['core.Blob', 'core.Role', 'core.User']));
  });

  it('replaces the members a patch sets, and returns a fresh value each time', () => {
    expect(tmTestMetadata({ calendars: ['gc', 'uq'] }).calendars).toEqual(['gc', 'uq']);
    expect(tmTestEntitiesChunk({ enums: [{ name: 'acme.Tier', values: [] }] }).enums.length).toBe(1);
    expect(tmTestMetadata()).not.toBe(tmTestMetadata());
  });

  it('returns an independent deep copy on every call, apart from the recording', () => {
    const named = <T extends { readonly name: string }>(items: readonly T[], name: string): T => {
      const item = items.find((candidate) => candidate.name === name);
      if (item === undefined) {
        throw new Error(`The test metadata lacks ${name}.`);
      }
      return item;
    };
    const recorded = tmTestEntities();
    const first = tmTestEntitiesChunk();
    const customer = structuredClone(named(first.entities, 'acme.Customer'));

    // Patch nested members in place, as a careless spec might: a platform entity's property list
    // and navigation, a platform enum's values, a platform summary, and a test entity's properties.
    const user = named(first.entities, 'core.User');
    const [property] = user.properties;
    const [enumeration] = first.enums;
    if (property === undefined || enumeration === undefined) {
      throw new Error('The test metadata lacks a core.User property or an enum.');
    }
    (user.properties as TmPropertyMetadata[]).push({ ...property, name: 'Leaked' });
    (user.navigations[0] as { labelKey: string }).labelKey = 'Leaked';
    (enumeration.values as TmEnumValueMetadata[]).length = 0;
    (named(tmTestMetadata().entities, 'core.User') as { labelKey: string }).labelKey = 'Leaked';
    (named(first.entities, 'acme.Customer').properties as TmPropertyMetadata[]).length = 0;

    // A later call, and the recording itself, never see those patches.
    const second = tmTestEntitiesChunk();
    expect(named(second.entities, 'core.User')).toEqual(named(recorded.chunk.entities, 'core.User'));
    expect(second.enums).toEqual(recorded.chunk.enums.filter((item) => item.name.startsWith('core.')));
    expect(named(second.entities, 'acme.Customer')).toEqual(customer);
    expect(named(tmTestMetadata().entities, 'core.User')).toEqual(named(recorded.summaries, 'core.User'));
    expect(named(tmTestEntities().chunk.entities, 'core.User')).toEqual(named(recorded.chunk.entities, 'core.User'));
  });

  it('is what provideTellmaTesting provides as the loaded client metadata', () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
    expect(TestBed.inject(TM_CLIENT_METADATA)).toEqual(tmTestMetadata());
  });
});
