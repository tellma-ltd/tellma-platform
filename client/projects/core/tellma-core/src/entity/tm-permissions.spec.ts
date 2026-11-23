// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { tmTestEntities } from '@tellma/core-testing';

import type { TmEntitiesChunk } from '../metadata/tm-client-metadata';
import type { TmAccessSummary } from '../state/launch/tm-launch-docs';
import type { TmPermissionOperation } from './tm-permissions';
import { tmEntityDocs, tmEntityRig, type TmEntityRigOptions } from './tm-entity.testing.util';

// Client permissions over the server's recorded model: grants from the access
// summary, the operations each entity offers, the tenant's state, the
// Access tab's names, and the traversal rule a query's paths obey.

/** One listed securable. */
function grant(resource: string, action: string, hasFilter = false): TmAccessSummary['securables'][number] {
  return { resource, action, hasFilter };
}

/** The grants of a user who keeps customers: every customer securable but the credit limit. */
const KEEPER = ['Read', 'Save', 'Delete', 'Activate'].map((action) => grant('acme.Customer', action));

/** Starts the app for a user with the given grants. */
function rig(securables: TmAccessSummary['securables'], options: TmEntityRigOptions = {}): ReturnType<typeof tmEntityRig> {
  return tmEntityRig({ ...options, access: { securables, ...options.access } });
}

describe('TmPermissions', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('grants', () => {
    it('hold a listed securable and say when it is filtered', async () => {
      const { permissions } = await rig([grant('acme.Customer', 'Read', true), grant('gl.Center', 'Read')]);

      expect(permissions.can('acme.Customer', 'Read')()).toBe(true);
      expect(permissions.can('acme.Customer', 'Save')()).toBe(false);
      expect(permissions.filtered('acme.Customer', 'Read')()).toBe(true);
      expect(permissions.filtered('gl.Center', 'Read')()).toBe(false);
      expect(permissions.filtered('gl.Center', 'Save')()).toBe(false);
    });

    it('give the system user every securable, never filtered', async () => {
      const { permissions } = await rig([], { access: { isSystem: true } });

      expect(permissions.can('anything.AtAll', 'Whatever')()).toBe(true);
      expect(permissions.filtered('acme.Customer', 'Read')()).toBe(false);
      expect(permissions.allows('acme.Customer', 'delete')()).toBe(true);
    });

    it('follow the access summary as it moves', async () => {
      const rigged = await rig([]);
      const read = rigged.permissions.can('acme.Customer', 'Read');
      expect(read()).toBe(false);

      const docs = tmEntityDocs();
      await rigged.move({ ...docs, me: { ...docs.me, access: { ...docs.me.access, tag: 'A2', securables: [grant('acme.Customer', 'Read')] } } });
      expect(read()).toBe(true);
    });

    it('share one signal per question', async () => {
      const { permissions } = await rig([]);
      expect(permissions.can('acme.Customer', 'Read')).toBe(permissions.can('acme.Customer', 'Read'));
      expect(permissions.allows('acme.Customer', 'save')).toBe(permissions.allows('acme.Customer', 'save'));
    });
  });

  describe('allows', () => {
    it.each<[TmPermissionOperation, boolean, boolean]>([
      // operation, for the keeper, for a reader
      ['query', true, true],
      ['details', true, true],
      ['export', true, true],
      ['save', true, false],
      ['import', true, false],
      ['delete', true, false],
      ['activate', true, false],
      ['action:set-credit-limit', false, false],
      ['action:statement', true, true],
      ['action:ping', true, true],
      ['action:nope', false, false],
    ])('%s needs its securable', async (operation, keeper, reader) => {
      const kept = await rig(KEEPER);
      expect(kept.permissions.allows('acme.Customer', operation)()).toBe(keeper);
      TestBed.resetTestingModule();

      const read = await rig([grant('acme.Customer', 'Read')]);
      expect(read.permissions.allows('acme.Customer', operation)()).toBe(reader);
    });

    it('needs the action’s own securable for an action', async () => {
      const { permissions } = await rig([...KEEPER, grant('acme.Customer', 'SetCreditLimit')]);
      expect(permissions.allows('acme.Customer', 'action:set-credit-limit')()).toBe(true);
    });

    it('refuses details for an entity with no details page, whatever the grants', async () => {
      const { permissions } = await rig([], { access: { isSystem: true } });

      // acme.Currency offers no details; core.Job offers them, but no feature declares a page.
      expect(permissions.allows('acme.Currency', 'details')()).toBe(false);
      expect(permissions.allows('core.Job', 'details')()).toBe(false);
      expect(permissions.allows('core.Job', 'query')()).toBe(true);
      expect(permissions.allows('gl.Center', 'details')()).toBe(true);
    });

    it('refuses an operation the entity does not offer, and an unknown entity', async () => {
      const { permissions } = await rig([], { access: { isSystem: true } });

      expect(permissions.allows('acme.Currency', 'save')()).toBe(false);
      // core.Job is not activatable, and acme.CustomerContact saves only through its owner.
      expect(permissions.allows('core.Job', 'activate')()).toBe(false);
      expect(permissions.allows('acme.CustomerContact', 'save')()).toBe(false);
      expect(permissions.allows('acme.Ghost', 'query')()).toBe(false);
    });

    it('knows an action once the Entities chunk loads', async () => {
      const { entities, permissions } = await rig(KEEPER, { load: false });
      const statement = permissions.allows('acme.Customer', 'action:statement');
      expect(statement()).toBe(false);

      await entities.load();
      expect(statement()).toBe(true);
    });
  });

  describe('the tenant’s state', () => {
    const grants = [...KEEPER, grant('acme.Customer', 'SetCreditLimit'), grant('acme.Customer', 'Update')];

    it.each<[TmPermissionOperation, boolean, boolean]>([
      // operation, under ReadOnly, under Frozen
      ['query', true, true],
      ['details', true, true],
      ['export', true, true],
      ['save', false, false],
      ['import', false, false],
      ['delete', false, false],
      ['activate', false, false],
      // Writes allowed when read-only, a read action and a member-only read.
      ['action:set-credit-limit', true, false],
      ['action:statement', true, true],
      ['action:ping', true, true],
      // A mutating action not allowed when read-only.
      ['action:ranks/recompute', false, false],
    ])('gates %s', async (operation, readOnly, frozen) => {
      const read = await rig(grants, { state: 'ReadOnly' });
      expect(read.permissions.allows('acme.Customer', operation)()).toBe(readOnly);
      TestBed.resetTestingModule();

      const freeze = await rig(grants, { state: 'Frozen' });
      expect(freeze.permissions.allows('acme.Customer', operation)()).toBe(frozen);
    });

    it('never changes can', async () => {
      const { permissions } = await rig(grants, { state: 'Frozen' });
      expect(permissions.can('acme.Customer', 'Save')()).toBe(true);
    });
  });

  describe('disabledReason', () => {
    it('names the missing grant in the Access tab’s words', async () => {
      const { permissions } = await rig([grant('acme.Customer', 'Read')]);

      expect(permissions.disabledReason('acme.Customer', 'save')()).toBe('Needs “Customers: Create and edit”. Ask your administrator to grant it.');
      expect(permissions.disabledReason('acme.Customer', 'delete')()).toBe('Needs “Customers: Delete”. Ask your administrator to grant it.');
      expect(permissions.disabledReason('acme.Customer', 'action:set-credit-limit')()).toBe(
        'Needs “Customers: Set credit limit”. Ask your administrator to grant it.',
      );
      // An unlisted pair shows its raw names.
      expect(permissions.disabledReason('acme.Customer', 'activate')()).toBe('Needs “acme.Customer: Activate”. Ask your administrator to grant it.');
    });

    it('is null when the operation is allowed or not offered', async () => {
      const { permissions } = await rig([grant('acme.Customer', 'Read')]);

      expect(permissions.disabledReason('acme.Customer', 'query')()).toBeNull();
      expect(permissions.disabledReason('acme.Customer', 'action:ping')()).toBeNull();
      expect(permissions.disabledReason('acme.Currency', 'save')()).toBeNull();
      expect(permissions.disabledReason('acme.Customer', 'action:nope')()).toBeNull();
    });

    it('names a read-only or frozen tenant, which wins over a missing grant', async () => {
      const read = await rig([grant('acme.Customer', 'Read')], { state: 'ReadOnly' });
      expect(read.permissions.disabledReason('acme.Customer', 'save')()).toBe('Acme Trading is read-only.');
      expect(read.permissions.disabledReason('acme.Customer', 'query')()).toBeNull();
      TestBed.resetTestingModule();

      const freeze = await rig(KEEPER, { state: 'Frozen', languages: 2, language: 'ar' });
      // The tenant's name in the user's content language; the message in English, which Arabic falls back to here.
      expect(freeze.permissions.disabledReason('acme.Customer', 'delete')()).toBe('أكمي للتجارة is under maintenance.');
    });
  });

  describe('label', () => {
    it('names an entity’s securables, alone and with an action', async () => {
      const { permissions } = await rig([]);

      expect(permissions.label('acme.Customer')()).toBe('Customers');
      expect(permissions.label('acme.Customer', 'Read')()).toBe('Customers: Read');
      expect(permissions.label('acme.Customer', 'SetCreditLimit')()).toBe('Customers: Set credit limit');
    });

    it('names a settings category through Settings', async () => {
      const { permissions } = await rig([]);

      expect(permissions.label('core.Settings.General')()).toBe('Settings: General');
      expect(permissions.label('core.Settings.General', 'Read')()).toBe('Settings: General: Read');
    });

    it('shows a pair the securables section does not list by its raw names', async () => {
      const { permissions } = await rig([]);

      expect(permissions.label('acme.Approval', 'Read')()).toBe('acme.Approval: Read');
      expect(permissions.label('acme.Approval')()).toBe('acme.Approval');
    });

    it('follows the language', async () => {
      const { permissions } = await rig([], { language: 'ar' });
      expect(permissions.label('acme.Customer')()).toBe('العملاء');
    });
  });

  describe('sensitive', () => {
    it('reads the securables section', async () => {
      const { permissions } = await rig([]);

      expect(permissions.sensitive('acme.Customer', 'SetCreditLimit')).toBe(true);
      expect(permissions.sensitive('acme.Customer', 'Read')).toBe(false);
      expect(permissions.sensitive('acme.Approval', 'Read')).toBe(false);
    });
  });

  describe('reachable', () => {
    it.each([
      // Under a filtered Read on gl.Center, through Parent only its projection: Id and Code.
      ['Code', true],
      ['Name', true],
      ['Parent', true],
      ['Parent.Code', true],
      ['Parent.Id', true],
      ['Parent.Name', false],
      ['Parent.CreatedAt', false],
      // A navigation reads its foreign key, which the projection lacks.
      ['Parent.Manager', false],
      // A second step reads the first's foreign key, which the projection lacks too.
      ['Parent.Parent.Code', false],
      ['Nope', false],
    ])('from a filtered gl.Center: %s is %s', async (key, reachable) => {
      const { permissions } = await rig([grant('gl.Center', 'Read', true)]);
      expect(permissions.reachable('gl.Center', key)()).toBe(reachable);
    });

    it('lets an unfiltered grant read every column, and limits an absent one to the projection', async () => {
      const { permissions } = await rig([grant('gl.Center', 'Read')]);

      expect(permissions.reachable('gl.Center', 'Parent.CreatedAt')()).toBe(true);
      // core.User is not granted: its projection only.
      expect(permissions.reachable('gl.Center', 'Manager.Name')()).toBe(true);
      expect(permissions.reachable('gl.Center', 'Manager.Email')()).toBe(false);
    });

    it.each([
      // core.User projects ImageId, so Image passes there; core.Blob then judges FileName itself.
      ['Manager.Image.FileName', true],
      ['Manager.Image.State', false],
      ['Manager.Image', true],
      // core.User projects neither Signature nor SignatureId.
      ['Manager.Signature.FileName', false],
      ['Parent.Manager.Image.FileName', true],
      ['Parent.Manager.Name', true],
      // Past core.User, ModifiedBy is not projected, whatever follows it.
      ['Manager.ModifiedBy.Name', false],
    ])('walks segment by segment from an unfiltered gl.Center with no core.User grant: %s is %s', async (key, reachable) => {
      const { permissions } = await rig([grant('gl.Center', 'Read')]);
      expect(permissions.reachable('gl.Center', key)()).toBe(reachable);
    });

    it('projects nothing of a read-only root, not even its key', async () => {
      // No recorded entity navigates into core.UserStamp, so one is added to gl.Center.
      const { chunk } = tmTestEntities();
      const stamp: TmEntitiesChunk = {
        ...chunk,
        entities: chunk.entities.map((entity) =>
          entity.name === 'gl.Center'
            ? { ...entity, navigations: [...entity.navigations, { name: 'Stamp', json: 'stamp', foreignKey: 'ManagerId', target: 'core.UserStamp', kind: 'reference', nullable: true, labelKey: 'Gl_Center_ManagerId' }] }
            : entity,
        ),
      };

      const restricted = await rig([grant('gl.Center', 'Read')], { chunk: stamp });
      expect(restricted.permissions.reachable('gl.Center', 'Stamp.UserId')()).toBe(false);
      expect(restricted.permissions.reachable('gl.Center', 'Stamp')()).toBe(true);
      TestBed.resetTestingModule();

      const granted = await rig([grant('gl.Center', 'Read'), grant('core.UserStamp', 'Read')], { chunk: stamp });
      expect(granted.permissions.reachable('gl.Center', 'Stamp.LastActiveAt')()).toBe(true);
    });

    it('needs the root’s Read', async () => {
      const { permissions } = await rig([grant('core.User', 'Read')]);
      expect(permissions.reachable('gl.Center', 'Code')()).toBe(false);
    });

    it('counts blobs as restricted, even for the system user', async () => {
      const { permissions } = await rig([], { access: { isSystem: true } });

      expect(permissions.reachable('acme.Customer', 'Image')()).toBe(true);
      expect(permissions.reachable('acme.Customer', 'Image.FileName')()).toBe(true);
      expect(permissions.reachable('acme.Customer', 'Image.State')()).toBe(false);
      expect(permissions.reachable('acme.Customer', 'Currency.DecimalPlaces')()).toBe(true);
    });

    describe('from a child root', () => {
      it('limits the owner to its projection under the child’s own grant', async () => {
        const { permissions } = await rig([grant('acme.CustomerContact', 'Read', true)]);

        expect(permissions.reachable('acme.CustomerContact', 'Customer.Code')()).toBe(true);
        expect(permissions.reachable('acme.CustomerContact', 'Customer.CreditLimit')()).toBe(false);
      });

      it('reads every owner column under the owner’s unfiltered Read', async () => {
        const { permissions } = await rig([grant('acme.CustomerContact', 'Read'), grant('acme.Customer', 'Read')]);

        expect(permissions.reachable('acme.CustomerContact', 'Customer.CreditLimit')()).toBe(true);
        expect(permissions.reachable('acme.CustomerContact', 'Customer.Currency.Code')()).toBe(true);
        // The owner's own references are judged by their targets' grants.
        expect(permissions.reachable('acme.CustomerContact', 'Customer.Currency.DecimalPlaces')()).toBe(false);
      });

      it('refuses past the owner’s projection when the summary cannot tell the child’s own grant from the owner’s', async () => {
        // A filtered owner composes into the child's entry, so a filtered child
        // may hold a grant of its own or only the owner's: the client fails closed.
        const { permissions } = await rig([grant('acme.CustomerContact', 'Read', true), grant('acme.Customer', 'Read', true)]);

        expect(permissions.reachable('acme.CustomerContact', 'Customer.Code')()).toBe(true);
        expect(permissions.reachable('acme.CustomerContact', 'Customer.CreditLimit')()).toBe(false);
        expect(permissions.reachable('acme.CustomerContact', 'Customer.Currency.Code')()).toBe(false);
        // From the owner itself, its own filtered grant limits nothing at the root.
        expect(permissions.reachable('acme.Customer', 'CreditLimit')()).toBe(true);
      });
    });

    it('is false until the Entities chunk loads', async () => {
      const { entities, permissions } = await rig([grant('gl.Center', 'Read')], { load: false });
      const code = permissions.reachable('gl.Center', 'Code');
      expect(code()).toBe(false);

      await entities.load();
      expect(code()).toBe(true);
    });
  });
});
