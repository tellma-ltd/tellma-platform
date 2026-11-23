// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { tmEntityDocs, tmEntityRig } from './tm-entity.testing.util';

// The entity registry over the server's recorded model: descriptors gated by
// the tenant's languages and labelled in the user's, named enums, and paths
// read against the model, before and after the Entities chunk loads.

describe('TmEntities', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('descriptors', () => {
    it.each([
      [1, ['Name'], ['Id', 'Name', 'Code', 'ImageId']],
      [2, ['Name', 'Name2'], ['Id', 'Name', 'Name2', 'Code', 'ImageId']],
      [3, ['Name', 'Name2', 'Name3'], ['Id', 'Name', 'Name2', 'Name3', 'Code', 'ImageId']],
    ] as const)('admit only the twins of the tenant’s %i languages', async (languages, twins, related) => {
      const { entities } = await tmEntityRig({ languages });
      const customer = entities.get('acme.Customer');

      expect(customer.slots).toBe(languages);
      expect(customer.properties.filter((property) => property.multilingual).map((property) => property.name)).toEqual(twins);
      expect(customer.relatedSelect).toEqual(related);
      expect(customer.property('Name3') === undefined).toBe(languages < 3);
    });

    it('label a twin with its language’s symbol once the tenant has two languages', async () => {
      const one = await tmEntityRig({ languages: 1 });
      expect(one.entities.get('acme.Customer').property('Name')?.label()).toBe('Name');
      TestBed.resetTestingModule();

      const three = await tmEntityRig({ languages: 3 });
      const customer = three.entities.get('acme.Customer');
      expect(['Name', 'Name2', 'Name3'].map((name) => customer.property(name)?.label())).toEqual(['Name (En)', 'Name (ع)', 'Name (Fr)']);
    });

    it('label members in the user’s language', async () => {
      const { entities } = await tmEntityRig({ languages: 2, language: 'ar' });
      const customer = entities.get('acme.Customer');

      expect(customer.pluralLabel()).toBe('العملاء');
      expect(customer.property('Name2')?.label()).toBe('الاسم (ع)');
      // A key no Arabic string holds falls back to English.
      expect(customer.label()).toBe('Customer');
    });

    it('give each twin its language’s direction, following the tenant’s settings', async () => {
      const rig = await tmEntityRig({ languages: 2 });
      const customer = rig.entities.get('acme.Customer');
      expect(customer.property('Name')?.contentDir).toBe('ltr');
      expect(customer.property('Name2')?.contentDir).toBe('rtl');
      expect(customer.property('Code')?.contentDir).toBeUndefined();

      // The tenant reorders its languages: Arabic first.
      const docs = tmEntityDocs({ languages: 2 });
      const [english, arabic] = docs.settings.languages;
      await rig.move({ ...docs, settings: { ...docs.settings, languages: [arabic!, english!] } });
      expect(customer.property('Name')?.contentDir).toBe('rtl');
      expect(customer.property('Name')?.label()).toBe('Name (ع)');
    });

    it('carry the merged declaration and each action’s entry in it', async () => {
      const { entities } = await tmEntityRig();
      const customer = entities.get('ACME.customer');

      expect(customer.name).toBe('acme.Customer');
      expect(customer.declaration['details']).toBeDefined();
      expect(customer.actions.find((action) => action.name === 'set-credit-limit')?.declaration).toEqual({ name: 'set-credit-limit', button: true });
      expect(customer.actions.find((action) => action.name === 'statement')?.declaration).toBeUndefined();
      expect(entities.get('core.Job').declaration).toEqual({ name: 'core.Job' });
      expect(customer.operations.has('details')).toBe(true);
      expect(customer.capabilities.has('activatable')).toBe(true);
      expect(customer.navigation('currency')?.target).toBe('acme.Currency');
      expect(customer.child('Contacts')?.entity).toBe('acme.CustomerContact');
    });

    it('are built once per entity and shape', async () => {
      const { entities } = await tmEntityRig({ languages: 2 });
      expect(entities.get('acme.Customer')).toBe(entities.get('acme.customer'));
    });

    it('refuse an entity the metadata does not list', async () => {
      const { entities } = await tmEntityRig();
      expect(() => entities.get('acme.Ghost')).toThrow("The metadata lists no entity 'acme.Ghost'.");
    });
  });

  describe('before the Entities chunk loads', () => {
    it('throw in development', async () => {
      const { entities } = await tmEntityRig({ load: false });

      expect(entities.loaded()).toBe(false);
      expect(() => entities.get('acme.Customer')).toThrow(/TmEntities\.get needs the Entities chunk/);
      expect(() => entities.enum('acme.CustomerType')).toThrow(/TmEntities\.enum needs the Entities chunk/);
      expect(() => entities.resolve('acme.Customer', 'Code')).toThrow(/TmEntities\.resolve needs the Entities chunk/);
      // The summary members need no load.
      expect(entities.bySegment('customers')?.name).toBe('acme.Customer');
    });

    it('give a production app a descriptor of the summary alone', async () => {
      const { entities } = await tmEntityRig({ load: false, devMode: false, languages: 2 });
      const customer = entities.get('acme.Customer');

      expect(customer.properties).toEqual([]);
      expect(customer.operations.has('save')).toBe(true);
      expect(customer.slots).toBe(2);
      expect(customer.label()).toBe('Customer');
      expect(customer.property('Code')).toBeUndefined();
      expect(entities.enum('acme.CustomerType')).toBeUndefined();
      expect(entities.resolve('acme.Customer', 'Code')).toBeNull();

      // The load replaces it with the full descriptor.
      await entities.load();
      expect(entities.loaded()).toBe(true);
      expect(entities.get('acme.Customer').property('Code')?.type).toBe('string');
    });

    it('time the load once', async () => {
      const { entities, telemetry } = await tmEntityRig();
      await entities.load();
      expect(telemetry.recent().filter((item) => item.name === 'tm.entity.metadata-loaded')).toHaveLength(1);
    });
  });

  describe('enums', () => {
    it('label values by key, and a value set by its aliases', async () => {
      const { entities } = await tmEntityRig();
      const type = entities.enum('acme.CustomerType');
      const zones = entities.enum('core.TimeZone');
      const [alias, canonical] = Object.entries(zones?.aliases ?? {})[0] ?? [];

      expect(type?.label('Retail')()).toBe('Retail');
      expect(type?.label('unknown')()).toBe('unknown');
      expect(alias).toBeDefined();
      expect(zones?.label(alias!)()).toBe(zones?.label(canonical!)());
      expect(entities.enum('acme.Nothing')).toBeUndefined();
    });
  });

  describe('resolve', () => {
    it('reads a scalar, a twin, a group anchor and a navigation', async () => {
      const { entities } = await tmEntityRig({ languages: 2 });

      const code = entities.resolve('acme.Customer', 'code');
      expect(code?.end).toMatchObject({ kind: 'value', property: { name: 'Code' } });
      expect(code?.label()).toBe('Code');

      const twin = entities.resolve('acme.Customer', 'Name2');
      expect(twin?.end).toMatchObject({ kind: 'value', property: { name: 'Name2' } });
      expect(twin?.label()).toBe('Name (ع)');

      const group = entities.resolve('acme.Customer', 'Name');
      expect(group?.end.kind).toBe('text');
      expect(group?.end.kind === 'text' && group.end.slots.map((slot) => slot.name)).toEqual(['Name', 'Name2']);
      expect(group?.label()).toBe('Name');

      const currency = entities.resolve('acme.Customer', 'Currency');
      expect(currency?.end).toMatchObject({ kind: 'navigation', navigation: { target: 'acme.Currency' } });
      expect(currency?.fkPath).toBe('CurrencyId');
    });

    it('reads a path through navigations, with a slash label and the foreign key’s path', async () => {
      const { entities } = await tmEntityRig();

      const parent = entities.resolve('gl.Center', 'Parent.Parent');
      expect(parent?.steps.map((step) => step.name)).toEqual(['Parent']);
      expect(parent?.entity).toBe('gl.Center');
      expect(parent?.fkPath).toBe('Parent.ParentId');
      expect(parent?.label()).toBe('Parent / Parent');

      const code = entities.resolve('acme.Customer', 'Currency.Code');
      expect(code?.root).toBe('acme.Customer');
      expect(code?.entity).toBe('acme.Currency');
      expect(code?.label()).toBe('Currency / Code');
    });

    it('reads a blob navigation as an image or a file end', async () => {
      const { entities } = await tmEntityRig();

      const image = entities.resolve('acme.Customer', 'Image');
      expect(image?.end.kind).toBe('image');
      expect(image?.end.kind === 'image' && image.end.policy.image).not.toBeNull();
      expect(image?.fkPath).toBe('ImageId');

      const file = entities.resolve('core.OutgoingEmailAttachment', 'File');
      expect(file?.end.kind).toBe('file');
      expect(file?.end.kind === 'file' && file.end.policy.image).toBeNull();
    });

    it.each([
      ['acme.Customer', 'Nope'],
      ['acme.Customer', 'Code.Length'],
      ['acme.Customer', 'Currency.Nope'],
      ['acme.Customer', 'Name3'],
      ['acme.Customer', ''],
      ['acme.Ghost', 'Code'],
    ])('returns null for %s %s, which does not resolve', async (root, key) => {
      const { entities } = await tmEntityRig({ languages: 2 });
      expect(entities.resolve(root, key)).toBeNull();
    });
  });
});
