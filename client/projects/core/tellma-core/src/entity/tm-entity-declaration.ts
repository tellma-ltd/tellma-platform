// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';
import type { SchemaPathTree } from '@angular/forms/signals';

import type { TmLazyComponent, TmSecurableRef } from '../declarations/tm-declaration-types';
import { tmMarkDeclaration } from '../declarations/tm-declaration-registry';
import type { TmId, TmJsonObject } from '../json/tm-json';
import type { TmNavContext } from '../nav/tm-nav-declarations';
import type { TmSearchContext } from '../search/tm-search-types';
import type { TmTenantUrls } from '../urls/tm-urls';
import type { TmDataType } from './tm-entity-metadata';
import type { TmCheckedOrder, TmCheckedPath, TmEntityType, TmPath } from './tm-entity-types';

// The entity declaration (spec 0029 §3): what a feature says about an entity
// that the metadata cannot know, such as its launcher entry, its grids'
// default columns, its details layout and its hatches. The merge rule treats
// it as one unit keyed by the entity's name. Every type here is type-only:
// `tmEntity` itself is the only code. The search page's own declaration
// types (spec 0030 §2.3-§2.5) are exported from here too, because a
// declaration names them before the search entry point loads.

// -----------------------------------------------------------------------------
// The search page's declaration types (spec 0030 §2.3-§2.5)
// -----------------------------------------------------------------------------

/**
 * A row of a select plan: its id, and any selected term by path, as the
 * wire sent it.
 *
 * @example
 * ```ts
 * const link = (row: TmPlanRow) => `/customers/${String(row.id)}`;
 * ```
 */
export interface TmPlanRow {
  /** The row's id. */
  readonly id: TmId;
  /**
   * A selected term's value.
   *
   * @param path - The term's path, such as `Parent.Code`.
   * @returns The value as the wire sent it.
   */
  get(path: string): unknown;
}

/**
 * How an entity's tiles show a row: an image and four text corners, each a
 * path, or a component that draws the whole tile body.
 *
 * @example
 * ```ts
 * const tile: TmTileDeclaration<'acme.Customer'> = { image: 'Image', title: 'Name', subtitle: 'Code', topEnd: 'CreditLimit' };
 * ```
 */
export type TmTileDeclaration<N extends string> =
  | {
      /** The thumbnail: an image navigation or its key. */
      readonly image?: TmPath<N>;
      /** The top-start text, by default the display name. */
      readonly title?: TmPath<N>;
      /** The bottom-start text. */
      readonly subtitle?: TmPath<N>;
      /** The top-end text, numeric for number types. */
      readonly topEnd?: TmPath<N>;
      /** The bottom-end badge. */
      readonly badge?: TmPath<N>;
    }
  | {
      /** A search cell component that draws the tile body. */
      readonly component: TmLazyComponent;
    };

/**
 * What a custom filter, one of its options or a tab applies: a Queryex leaf
 * or one of the stack's named filters, exactly one of the two.
 */
export type TmFilterSource =
  | { readonly queryex: string; readonly named?: never }
  | { readonly named: string; readonly queryex?: never };

/**
 * A declared predicate the user turns on in the search page's Filters
 * popup: a switch (`x.{key}=1`) or a radio group of options
 * (`x.{key}={value}`).
 *
 * @example
 * ```ts
 * const credit: TmCustomFilterDeclaration = {
 *   key: 'credit', labelKey: 'Acme_Customer_Credit',
 *   options: [{ value: 'high', labelKey: 'Acme_Customer_Credit_High', queryex: 'CreditLimit >= 500000' }],
 * };
 * ```
 */
export type TmCustomFilterDeclaration =
  | ({ readonly key: string; readonly labelKey: string } & TmFilterSource)
  | {
      /** The filter's URL key, matching `^[a-z][a-z0-9-]*$`. */
      readonly key: string;
      /** The string key of the filter's label. */
      readonly labelKey: string;
      /** The filter's options, one chosen at a time. */
      readonly options: readonly ({ readonly value: string; readonly labelKey: string } & TmFilterSource)[];
    };

/**
 * The confirmation a destructive action asks before it runs: the string
 * keys of its title and message, and the message's arguments.
 */
export interface TmActionConfirmation {
  /** The string key of the confirmation's title. */
  readonly titleKey: string;
  /** The string key of the confirmation's message. */
  readonly messageKey: string;
  /** The message's arguments. */
  readonly args?: Signal<TmJsonObject>;
}

/**
 * A page action a declaration adds to its entity's search page: a button,
 * a "More" menu item, or an item of the selection bar's Actions menu.
 *
 * @example
 * ```ts
 * const readAll: TmSearchPageActionDeclaration = {
 *   id: 'attention.notifications.read-all', labelKey: 'Notifications_ReadAll',
 *   run: async () => { await markAllRead(); },
 * };
 * ```
 */
export interface TmSearchPageActionDeclaration {
  /** The action's id, such as `attention.notifications.read-all`. */
  readonly id: string;
  /** The string key of the action's label. */
  readonly labelKey: string;
  /** A registered icon name. */
  readonly icon?: string;
  /** Where the action shows; `primary` by default. Ignored when `selection` is set. */
  readonly group?: 'primary' | 'menu';
  /** The action's place within its group. */
  readonly order?: number;
  /** Whether the action destroys data; such an action needs `confirm`. */
  readonly destructive?: boolean;
  /** The confirmation a destructive action asks. */
  readonly confirm?: TmActionConfirmation;
  /** The permission without which the action is not shown. */
  readonly requires?: TmSecurableRef;
  /** Whether the action runs over the selected rows, from the selection bar. */
  readonly selection?: boolean;
  /**
   * Runs the action in the search host's injection context; it refreshes
   * what it changes.
   *
   * @param search - The search page's context.
   * @param ids - The selected rows' ids when `selection` is set, else none.
   * @returns A promise that settles once the action is done.
   */
  run(search: TmSearchContext, ids: readonly TmId[]): Promise<void>;
}

// -----------------------------------------------------------------------------
// The entity declaration's members
// -----------------------------------------------------------------------------

/**
 * One column of an entity's grids: a path, a path with a custom cell, or a
 * computed column. An annotated column checks only its path's first segment;
 * a column written in a `tmEntity` declaration checks every segment, and the
 * build's config check reports a path that does not resolve either way.
 *
 * @example
 * ```ts
 * // Checked segment by segment, since tmEntity reads the literals:
 * tmEntity('acme.Customer', { columns: ['Code', 'Currency.Code', { path: 'CreditLimit', display: () => import('./credit-cell') }] });
 * // Checked on the first segment only, since the annotation widens the literals:
 * const columns: readonly TmColumnInput<'acme.Customer'>[] = ['Code', 'Currency.Code'];
 * ```
 */
export type TmColumnInput<N extends string> =
  | TmPath<N>
  | {
      /** The column's path. */
      readonly path: TmPath<N>;
      /** A search cell component that renders the column's cells. */
      readonly display?: TmLazyComponent;
      /** The column's width in pixels. */
      readonly width?: number;
      /** The paths the custom cell reads beside the column's own. */
      readonly select?: readonly TmPath<N>[];
      /** A Queryex filter on the picker of an editable navigation cell. */
      readonly filter?: string;
    }
  | {
      /** The Queryex expression the column shows. */
      readonly expression: string;
      /** The string key of the column's label. */
      readonly labelKey: string;
      /** The expression's type, when known before the response types it. */
      readonly type?: TmDataType;
    };

/**
 * An order: a path, ascending, or a path with `:desc`. Like `TmPath`, an
 * annotation checks the path's first segment; `tmEntity` checks every one.
 */
export type TmOrderInput<N extends string> = TmPath<N> | `${TmPath<N>}:desc`;

/**
 * An entity's details page, beyond the lazy layout alone.
 *
 * @example
 * ```ts
 * const details: TmDetailsDeclaration<'acme.Customer'> = { page: () => import('./customer-details'), header: 'compact' };
 * ```
 */
export interface TmDetailsDeclaration<N extends string> {
  /** The details layout. */
  readonly page: TmLazyComponent;
  /** A separate layout for create mode. */
  readonly create?: TmLazyComponent;
  /** `page`: the page is its own editor. */
  readonly edit?: 'page';
  /** `page`: the page registers its own refresh action. */
  readonly refresh?: 'page';
  /** The header's form; `standard` by default. */
  readonly header?: 'standard' | 'compact';
  /**
   * Where a create lands.
   *
   * @param entity - The created record.
   * @param urls - The tenant's URLs.
   * @returns The URL, which replaces the create page's.
   */
  readonly createdUrl?: (entity: TmEntityType<N>, urls: TmTenantUrls) => string;
}

/**
 * How the platform offers one of an entity's actions.
 *
 * @example
 * ```ts
 * const action: TmEntityActionDeclaration<'acme.Customer'> = { name: 'set-credit-limit', icon: 'banknote', button: true };
 * ```
 */
export interface TmEntityActionDeclaration<N extends string> {
  /** The action's name, as the metadata lists it. */
  readonly name: string;
  /** A dialog that replaces the generated one. */
  readonly dialog?: TmLazyComponent;
  /** A registered icon name. */
  readonly icon?: string;
  /** Whether the action shows as a button rather than in the Actions menu. */
  readonly button?: boolean;
  /**
   * Whether the action is offered for a record.
   *
   * @param entity - The record.
   * @param context - The signed-in user.
   * @returns Whether it shows.
   */
  readonly visible?: (entity: TmEntityType<N>, context: { readonly userId: number }) => boolean;
  /** The confirmation a destructive action asks, in place of the default. */
  readonly confirm?: TmActionConfirmation;
  /** `false`: never offered over a search page's selection. */
  readonly bulk?: false;
}

/**
 * How the entity's pickers search and show rows.
 *
 * @example
 * ```ts
 * const picker: TmPickerDeclaration<'acme.Customer'> = { description: ['Code', 'CustomerType'], create: false };
 * ```
 */
export interface TmPickerDeclaration<N extends string> {
  /** A Queryex predicate every search applies. */
  readonly filter?: string;
  /** The paths of a row's second line, or `false` for none. */
  readonly description?: readonly TmPath<N>[] | false;
  /** The order of the results; the display name's by default. */
  readonly orderBy?: TmOrderInput<N>;
  /** Whether the picker offers "Create…"; `true` by default. */
  readonly create?: boolean;
}

/** A declared tab of an entity's search page: a view of the entity, at most one of a leaf and a named filter. */
export interface TmTabDeclaration {
  /** The tab's URL key, matching `^[a-z][a-z0-9-]*$`. */
  readonly key: string;
  /** The string key of the tab's label. */
  readonly labelKey: string;
  /** The Queryex leaf the tab applies. */
  readonly queryex?: string;
  /** The stack's named filter the tab applies. */
  readonly named?: string;
}

/**
 * What a feature declares about one entity. Every member is optional; a
 * declaration with no member still names the entity. Rows are wire
 * camelCase objects, typed by the entity's name once the distribution's
 * emitted declarations register it; paths are PascalCase. A path in an
 * annotated input checks its first segment only; passed to `tmEntity` as a
 * literal, every segment is checked.
 *
 * @example
 * ```ts
 * const input: TmEntityDeclarationInput<'acme.Customer'> = {
 *   section: 'sales', order: 10, icon: 'users',
 *   details: () => import('./customer-details'),
 * };
 * ```
 */
export interface TmEntityDeclarationInput<N extends string = string> {
  /** The launcher section of the entity's entry; absent, the entry is found by search alone. */
  readonly section?: string;
  /** The entry's place within its section. */
  readonly order?: number;
  /** A registered icon name for the entry and the entity's pages. */
  readonly icon?: string;
  /** The string key of the entry's comma-separated search synonyms. */
  readonly synonymsKey?: string;
  /** Hides the launcher entry when it returns `false`. */
  readonly visibleWhen?: (context: TmNavContext) => boolean;
  /** The search page's default columns. */
  readonly columns?: readonly TmColumnInput<N>[];
  /** The search page's default order; else the metadata's. */
  readonly orderBy?: TmOrderInput<N>;
  /** How the tile view shows a row. */
  readonly tile?: TmTileDeclaration<N>;
  /** The custom filters the Filters popup offers. */
  readonly filters?: readonly TmCustomFilterDeclaration[];
  /** The search page's tabs. */
  readonly tabs?: readonly TmTabDeclaration[];
  /**
   * Where a search row leads, instead of the details page.
   *
   * @param row - The row.
   * @returns The URL, or `null` for no link.
   */
  readonly rowLink?: (row: TmPlanRow) => string | null;
  /** The page actions the search page adds. */
  readonly pageActions?: readonly TmSearchPageActionDeclaration[];
  /** A component that replaces the search page's results region. */
  readonly search?: TmLazyComponent;
  /** The details page: its layout, or the layout with its options. */
  readonly details?: TmLazyComponent | TmDetailsDeclaration<N>;
  /**
   * A new record's values.
   *
   * @returns The values.
   */
  readonly defaults?: () => Partial<TmEntityType<N>>;
  /**
   * Adjusts a copy of a record into a new record.
   *
   * @param copy - The copy.
   * @param source - The copied record.
   * @returns The new record.
   */
  readonly clone?: (copy: TmEntityType<N>, source: TmEntityType<N>) => TmEntityType<N>;
  /**
   * The details form's validation, as a Signal Forms schema function.
   *
   * @param path - The form's schema path tree.
   */
  readonly validate?: (path: SchemaPathTree<TmEntityType<N>>) => void;
  /**
   * Adjusts a record's values before it is saved; every collection keeps
   * its length and order.
   *
   * @param entity - The record.
   * @returns The record to save.
   */
  readonly beforeSave?: (entity: TmEntityType<N>) => TmEntityType<N> | Promise<TmEntityType<N>>;
  /** How the entity's actions are offered. */
  readonly actions?: readonly TmEntityActionDeclaration<N>[];
  /** How the entity's pickers search and show rows. */
  readonly picker?: TmPickerDeclaration<N>;
  /** Reserved: the string key of the entity's help. */
  readonly helpKey?: string;
}

/**
 * One entity's declaration: its name, and the members a feature set. It is
 * frozen, and the merge rule patches it member by member.
 */
export interface TmEntityDeclaration {
  /** The entity's name, such as `acme.Customer`. */
  readonly name: string;
  /** The members the feature set. */
  readonly [member: string]: unknown;
}

// --- The literal paths of a declaration, checked segment by segment ---

/** Each path of a list checked from an entity; other members pass. */
type TmCheckedPaths<N extends string, A> = { readonly [I in keyof A]: A[I] extends string ? TmCheckedPath<N, A[I]> : A[I] };

/** A column checked from an entity: a path, or a column's path and its select. */
type TmCheckedColumn<N extends string, C> = C extends string
  ? TmCheckedPath<N, C>
  : { readonly [M in keyof C]: M extends 'path' ? (C[M] extends string ? TmCheckedPath<N, C[M]> : C[M]) : M extends 'select' ? TmCheckedPaths<N, C[M]> : C[M] };

/** Each column of a list checked from an entity. */
type TmCheckedColumns<N extends string, A> = { readonly [I in keyof A]: TmCheckedColumn<N, A[I]> };

/** An order checked from an entity, when it is text. */
type TmCheckedOrderMember<N extends string, O> = O extends string ? TmCheckedOrder<N, O> : O;

/** A picker's description and order checked from an entity. */
type TmCheckedPicker<N extends string, P> = {
  readonly [M in keyof P]: M extends 'description' ? TmCheckedPaths<N, P[M]> : M extends 'orderBy' ? TmCheckedOrderMember<N, P[M]> : P[M];
};

/**
 * A declaration whose literal paths are checked from its entity, segment by
 * segment: the columns with their selects, the order, the tile's slots and
 * the picker's description and order. Every other member passes as it is.
 */
type TmCheckedDeclaration<N extends string, D> = {
  readonly [K in keyof D]: K extends 'columns'
    ? TmCheckedColumns<N, D[K]>
    : K extends 'orderBy'
      ? TmCheckedOrderMember<N, D[K]>
      : K extends 'tile'
        ? TmCheckedPaths<N, D[K]>
        : K extends 'picker'
          ? TmCheckedPicker<N, D[K]>
          : D[K];
};

/**
 * Declares an entity: its launcher entry, its default grids, its details
 * layout and its hatches. Listed in a feature's `entities`, it adds the
 * entity; listed in a later feature's `overrides`, it patches the earlier
 * declaration member by member, `picker` and `actions` whole. The name
 * types the declaration's paths and rows, so it takes no type argument.
 *
 * @param name - The entity's name, as the metadata spells it: `acme.Customer`.
 * @param declaration - The members to set; once the emitted declarations register the entity, each literal path it names is checked, segment by segment.
 * @returns The frozen declaration.
 *
 * @example
 * ```ts
 * export const customers = tmEntity('acme.Customer', {
 *   section: 'sales', order: 10, icon: 'users',
 *   details: () => import('./customer-details'),
 *   picker: { description: ['Code', 'CustomerType'] },
 * });
 * ```
 */
export function tmEntity<const N extends string, const D extends TmEntityDeclarationInput<N> = TmEntityDeclarationInput<N>>(
  name: N,
  declaration?: D extends TmCheckedDeclaration<N, D> ? D : TmCheckedDeclaration<N, D>,
): TmEntityDeclaration {
  return tmMarkDeclaration(Object.freeze({ ...declaration, name }), 'entity');
}
