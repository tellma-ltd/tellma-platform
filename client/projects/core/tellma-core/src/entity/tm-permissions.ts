// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, Injector, type Signal } from '@angular/core';

import { TmLocalization } from '../l10n/tm-localization';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import { TmEntities } from './tm-entities';
import type { TmEntityOperation, TmEntitySummary } from './tm-entity-metadata';

// Client permissions (spec 0029 §7): what the user may do, from the cached
// access summary and the securables section, so buttons, entries and offered
// paths never invite a refusal. The server stays the authority. The tenant's
// state gates the writes here too, so every write action explains a
// read-only or frozen tenant as it explains a missing grant.

/** An operation an entity offers, its activation, or one of its actions by name. */
export type TmPermissionOperation = TmEntityOperation | 'activate' | `action:${string}`;

/** What one operation needs: its securable action (`null` for any member) and whether it writes. */
interface TmRequirement {
  /** The securable action; `null` when any member may run it. */
  readonly action: string | null;
  /** Whether the operation writes, so the tenant's state may refuse it. */
  readonly write: boolean;
  /** Whether a read-only tenant still runs it. */
  readonly readOnly: boolean;
}

/** The securable action each built-in operation needs, and whether it writes. */
const OPERATIONS: Readonly<Record<string, readonly [action: string, write: boolean]>> = {
  query: ['Read', false],
  details: ['Read', false],
  export: ['Read', false],
  save: ['Save', true],
  import: ['Save', true],
  delete: ['Delete', true],
  activate: ['Activate', true],
};

/**
 * What the user may do in the routed tenant: grants from the access
 * summary, entity operations gated by the tenant's state, the reasons a
 * refused action shows, and the paths a query may read.
 *
 * @example
 * ```ts
 * const permissions = inject(TmPermissions);
 * const canEdit = permissions.allows('acme.Customer', 'save');
 * const why = permissions.disabledReason('acme.Customer', 'save'); // "Needs “Customers: Create and edit”…"
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmPermissions {
  readonly #injector = inject(Injector);
  readonly #entities = inject(TmEntities);
  readonly #l10n = inject(TmLocalization);
  readonly #metadata = inject(TmClientMetadataStore);

  /** The answers made so far, by member and arguments, so a repeated question shares its signal. */
  readonly #answers = new Map<string, Signal<unknown>>();

  /** The routed tenant's launch documents, resolved at first use. */
  get #docs(): TmLaunchCache['docs'] {
    return this.#injector.get(TmLaunchCache).docs;
  }

  /** The user's grants: whether each listed securable is filtered, by resource and action; `null` before the first `me`. */
  readonly #grants = computed(() => {
    const access = this.#docs.me()?.access;
    return access && {
      isSystem: access.isSystem,
      filters: new Map(access.securables.map((securable) => [`${securable.resource}\n${securable.action}`, securable.hasFilter])),
    };
  });

  /**
   * One answer, made once per member and arguments.
   *
   * @param key - The member and its arguments.
   * @param answer - How the answer is computed.
   * @returns The answer's signal.
   */
  #once<T>(key: string, answer: () => T): Signal<T> {
    let signal = this.#answers.get(key);
    if (signal === undefined) {
      this.#answers.set(key, (signal = computed(answer)));
    }
    return signal as Signal<T>;
  }

  /**
   * Whether the user holds a permission: the system user holds every one,
   * anyone else a listed one. Every answer is `false` before the first `me`.
   *
   * @param resource - The securable resource, such as `acme.Customer`.
   * @param action - The action, such as `Read`.
   * @returns Whether it is granted.
   */
  can(resource: string, action: string): Signal<boolean> {
    return this.#once(`c\n${resource}\n${action}`, () => {
      const grants = this.#grants();
      return grants !== null && grants !== undefined && (grants.isSystem || grants.filters.has(`${resource}\n${action}`));
    });
  }

  /**
   * Whether a permission the user holds reaches only some rows. The system
   * user's never is.
   *
   * @param resource - The securable resource.
   * @param action - The action.
   * @returns Whether the grant carries a filter.
   */
  filtered(resource: string, action: string): Signal<boolean> {
    return this.#once(`f\n${resource}\n${action}`, () => {
      const grants = this.#grants();
      return grants?.isSystem === false && grants.filters.get(`${resource}\n${action}`) === true;
    });
  }

  /**
   * What an operation of an entity needs, or `null` when the entity does
   * not offer it: `details` also needs a details page, `activate` an
   * activatable stack that saves, and an action is known once the Entities
   * chunk is loaded.
   *
   * @param summary - The entity's summary.
   * @param operation - The operation.
   * @returns The requirement, or `null`.
   */
  #requirement(summary: TmEntitySummary, operation: TmPermissionOperation): TmRequirement | null {
    if (operation.startsWith('action:')) {
      const name = operation.slice(7);
      const action = this.#entities.ɵmodel()?.metadata(summary.name)?.actions.find((candidate) => candidate.name === name);
      return action ? { action: action.action, write: action.mutation, readOnly: action.allowWhenReadOnly === true } : null;
    }
    const [action, write] = OPERATIONS[operation] ?? [];
    const offered =
      operation === 'activate'
        ? summary.capabilities.includes('activatable') && summary.operations.includes('save')
        : summary.operations.includes(operation as TmEntityOperation) &&
          (operation !== 'details' || this.#entities.ɵhasDetailsPage(summary));
    return offered && action !== undefined ? { action, write: write === true, readOnly: false } : null;
  }

  /**
   * The tenant's state when it refuses a requirement: `ReadOnly` refuses the
   * writes not allowed when read-only, `Frozen` every write.
   *
   * @param requirement - The requirement.
   * @returns The refusing state, or `null` when the state admits it.
   */
  #refusal(requirement: TmRequirement): 'ReadOnly' | 'Frozen' | null {
    const state = this.#docs.tenant()?.state;
    return requirement.write && (state === 'Frozen' || (state === 'ReadOnly' && !requirement.readOnly)) ? state : null;
  }

  /**
   * Whether the user may perform an operation of an entity: the entity
   * offers it, its securable is granted, and the tenant's state admits it.
   * An action (`action:<name>`) is `false` until the Entities chunk loads.
   *
   * @param entity - The entity's name.
   * @param operation - The operation, `activate`, or `action:<name>`.
   * @returns Whether it is allowed.
   */
  allows(entity: string, operation: TmPermissionOperation): Signal<boolean> {
    return this.#once(`a\n${entity}\n${operation}`, () => {
      const summary = this.#entities.summary(entity);
      const requirement = summary && this.#requirement(summary, operation);
      return (
        !!requirement &&
        this.#refusal(requirement) === null &&
        (requirement.action === null || this.can(summary.name, requirement.action)())
      );
    });
  }

  /**
   * Why an operation the entity offers is refused: the tenant's state, which
   * wins, or the missing grant, named in the Access tab's words. `null`
   * when it is allowed, and when the entity does not offer it, since such
   * an action is hidden.
   *
   * @param entity - The entity's name.
   * @param operation - The operation, `activate`, or `action:<name>`.
   * @returns The reason, or `null`.
   */
  disabledReason(entity: string, operation: TmPermissionOperation): Signal<string | null> {
    return this.#once(`r\n${entity}\n${operation}`, () => {
      const summary = this.#entities.summary(entity);
      const requirement = summary && this.#requirement(summary, operation);
      if (!requirement) {
        return null;
      }
      const refusal = this.#refusal(requirement);
      if (refusal !== null) {
        const settings = this.#docs.settings();
        const tenant = settings?.names[this.#l10n.contentSlot() - 1] ?? settings?.names[0] ?? '';
        return this.#l10n.ɵtext(refusal === 'Frozen' ? 'tm.entity.tenantFrozen' : 'tm.entity.tenantReadOnly', { tenant })();
      }
      if (requirement.action === null || this.can(summary.name, requirement.action)()) {
        return null;
      }
      return this.#l10n.ɵtext('tm.entity.permissionNeeded', { permission: this.label(summary.name, requirement.action)() })();
    });
  }

  /**
   * Whether a permission is sensitive, so using it asks for a recent
   * sign-in: a deployment fact from the securables section.
   *
   * @param resource - The securable resource.
   * @param action - The action.
   * @returns Whether it is sensitive.
   */
  sensitive(resource: string, action: string): boolean {
    return this.#metadata.peek()?.securables.some((item) => item.resource === resource && item.action === action && item.isSensitive) ?? false;
  }

  /**
   * A permission's name in the Access tab's words, such as "Customers:
   * Create and edit", or a resource's alone, such as "Customers"; a
   * settings securable reads "Settings: General". A pair the securables
   * section does not list shows its raw names.
   *
   * @param resource - The securable resource.
   * @param action - The action; omitted, the resource alone.
   * @returns The label.
   */
  label(resource: string, action?: string): Signal<string> {
    return this.#once(`l\n${resource}\n${action ?? ''}`, () => {
      const text = this.#l10n.ɵtext.bind(this.#l10n);
      const securables = this.#metadata.peek()?.securables ?? [];
      const listed = securables.find((item) => item.resource === resource && (action === undefined || item.action === action));
      if (listed === undefined) {
        return action === undefined ? resource : text('tm.entity.permission', { resource, action })();
      }
      // A settings category reads through Core's "Settings: {category}".
      let name = text(listed.resourceLabelKey, undefined, listed.resourceLabelFallback)();
      if (resource.startsWith('core.Settings.')) {
        name = text('Securable_Settings', { category: name }, `Settings: ${name}`)();
      }
      return action === undefined
        ? name
        : text('tm.entity.permission', { resource: name, action: text(listed.actionLabelKey, undefined, listed.actionLabelFallback)() })();
    });
  }

  /**
   * Whether a query from an entity may read a path, by the server's
   * traversal rule: the root's `Read` is granted, and through each
   * navigation whose target the user reads with a filter or not at all,
   * every column read is in the target's related projection. Blobs always
   * count as restricted. `false` until the Entities chunk loads.
   *
   * @param root - The entity the query reads, such as `gl.Center`.
   * @param key - The path, such as `Parent.Code`.
   * @returns Whether the path is reachable.
   */
  reachable(root: string, key: string): Signal<boolean> {
    return computed(() => {
      const model = this.#entities.ɵmodel();
      const name = this.#entities.summary(root)?.name;
      return (
        !!model &&
        name !== undefined &&
        this.can(name, 'Read')() &&
        model.reachable(name, key, this.#entities.ɵshape(), (entity) => !this.can(entity, 'Read')() || this.filtered(entity, 'Read')())
      );
    });
  }
}
