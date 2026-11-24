// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { FieldTree } from '@angular/forms/signals';
import type { TmEntities } from '@tellma/core';

// Server error paths onto a form (spec 0031 §8.5 steps 1-2, spec 0016 §7.3):
// a 422 item's `path` is a camelCase index grammar of `name` and `[digits]`
// segments, such as `entities[0].lines[1].amount` or `arguments.creditLimit`.
// The splitter turns it back into segments; the walker follows them through a
// Signal Forms tree by the wire's names, and, given the entity the tree holds,
// through each child collection's entity, so the caller learns which field
// the error names, the entity whose property it is, and the PascalCase path a
// page registered the field under. A segment that does not resolve stops the
// walk at the deepest field reached. The details page's save errors and an
// action dialog's argument errors share both.

/** One segment of an error path: a member's wire name, or an index into a list. */
export type TmErrorPathSegment = string | number;

/** Where an error path leads in a form. */
export interface TmErrorPathTarget {
  /** The field the path names, or, when a segment did not resolve, the deepest field reached. */
  readonly field: FieldTree<unknown>;
  /** Whether every segment resolved, so {@link TmErrorPathTarget.field} is the field the path names. */
  readonly resolved: boolean;
  /**
   * The segments walked, members in PascalCase when an entity guided the
   * walk (`['Lines', 1, 'Amount']`, as a page registers its fields), else
   * as the path spells them.
   */
  readonly path: readonly TmErrorPathSegment[];
  /** The entity whose member the last segment walked names, such as `acme.SalesInvoiceLine`; absent without an entity. */
  readonly entity?: string;
}

/** The options of {@link tmWalkErrorPath}: the entity the form holds, read through the registry. */
export interface TmErrorPathWalkOptions {
  /** The entity the form's root holds, such as `acme.SalesInvoice`. */
  readonly entity: string;
  /** The entity registry, loaded, whose descriptors name each member and each collection's entity. */
  readonly entities: TmEntities;
}

/** One token of the grammar: a member name, or an index in brackets. */
const TOKEN = /([A-Za-z_$][\w$]*)|\[(\d+)\]/y;

/**
 * Splits a server error path into its member and index segments, by the
 * grammar the server writes: members joined by `.`, indices in brackets.
 *
 * @param path - The path, such as `entities[0].lines[1].amount`.
 * @returns The segments, such as `['entities', 0, 'lines', 1, 'amount']`; `null` for an empty or malformed path.
 *
 * @example
 * ```ts
 * tmSplitErrorPath('arguments.creditLimit'); // ['arguments', 'creditLimit']
 * tmSplitErrorPath('ids[2]'); // ['ids', 2]
 * ```
 */
export function tmSplitErrorPath(path: string): readonly TmErrorPathSegment[] | null {
  const segments: TmErrorPathSegment[] = [];
  let at = 0;
  while (at < path.length) {
    // A member after the first needs its dot; an index never takes one.
    const dotted = segments.length > 0 && path[at] === '.';
    if (dotted) {
      at++;
    }
    TOKEN.lastIndex = at;
    const match = TOKEN.exec(path);
    if (match === null || (match[1] !== undefined && segments.length > 0 && !dotted) || (match[2] !== undefined && dotted)) {
      return null;
    }
    segments.push(match[1] ?? Number(match[2]));
    at = TOKEN.lastIndex;
  }
  return segments.length === 0 ? null : segments;
}

/**
 * Follows error path segments through a Signal Forms tree, from its root,
 * by the wire's member names and list indices. With an entity, each member
 * must be one of the entity's properties, navigations or child collections,
 * a collection's index step moves to its child entity, and the path walked
 * names members in PascalCase. The walk stops at the first segment the tree
 * (or the entity) does not hold; the target is then the deepest field
 * reached, unresolved.
 *
 * @param root - The form's root field.
 * @param segments - The segments, relative to the root, such as `['lines', 1, 'amount']`.
 * @param options - The entity the root holds, when its descriptors should guide the walk.
 * @returns Where the path leads.
 *
 * @example
 * ```ts
 * const target = tmWalkErrorPath(form, ['lines', 1, 'amount'], { entity: 'acme.SalesInvoice', entities });
 * // target.path: ['Lines', 1, 'Amount']; target.entity: 'acme.SalesInvoiceLine'
 * ```
 */
export function tmWalkErrorPath(
  root: FieldTree<unknown>,
  segments: readonly TmErrorPathSegment[],
  options?: TmErrorPathWalkOptions,
): TmErrorPathTarget {
  let field = root;
  let entity = options?.entity;
  // The child entity a collection's next index step moves to, once its member was walked.
  let collectionOf: string | undefined;
  const path: TmErrorPathSegment[] = [];
  const target = (resolved: boolean): TmErrorPathTarget => ({ field, resolved, path, ...(entity !== undefined && { entity }) });
  for (const segment of segments) {
    // --- The member's name under the entity, when one guides the walk ---
    let name: TmErrorPathSegment = segment;
    if (options !== undefined && entity !== undefined) {
      if (typeof segment === 'number') {
        if (collectionOf === undefined) {
          return target(false);
        }
      } else {
        const descriptor = options.entities.get(entity);
        const member =
          descriptor.properties.find((property) => property.json === segment) ??
          descriptor.navigations.find((navigation) => navigation.json === segment);
        const child = member === undefined ? descriptor.children.find((collection) => collection.json === segment) : undefined;
        if (member === undefined && child === undefined) {
          return target(false);
        }
        name = (member ?? child)!.name;
        collectionOf = child?.entity;
      }
    }

    // --- The field's child by the wire's key: an index into a list, or a member of anything else ---
    if ((typeof segment === 'number') !== Array.isArray(field().value())) {
      return target(false);
    }
    const next = (field as unknown as Readonly<Record<string, FieldTree<unknown> | undefined>>)[String(segment)];
    if (next === undefined) {
      return target(false);
    }
    field = next;
    path.push(name);
    if (typeof segment === 'number' && collectionOf !== undefined) {
      // A collection's row: the following members are its child entity's.
      entity = collectionOf;
      collectionOf = undefined;
    }
  }
  return target(true);
}
