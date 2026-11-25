// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TM_CLIENT_METADATA } from '@tellma/core';

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
    // Every reference stays inside the chunk: navigation targets, child entities and named enums.
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

  it('is what provideTellmaTesting provides as the loaded client metadata', () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
    expect(TestBed.inject(TM_CLIENT_METADATA)).toEqual(tmTestMetadata());
  });
});
