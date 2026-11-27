// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { resource, type Signal, signal } from '@angular/core';
import {
  applyEach,
  type FieldTree,
  maxLength,
  readonly,
  required,
  type SchemaPath,
  type SchemaPathTree,
  validate,
  validateAsync,
  validateTree,
  type ValidationError,
} from '@angular/forms/signals';
import type { TmEntityDescriptor, TmPropertyDescriptor } from '@tellma/core';

import type { TmDetailsMode } from './tm-details-context';

// The platform schema (spec 0031 §7.1): what the metadata knows, as Signal
// Forms rules over the wire-shaped record and, through applyEach, over
// every row of its child collections. Required, maximum length and digits
// on the properties the client writes; read-only on the ones it does not
// write, and on write-once ones outside create mode; pending while an
// upload for the path runs; and one tree validator that shows each recorded
// server or source error while its node still holds the value it was
// recorded with. The server stays the authority for everything else.

/** A recorded error the platform schema shows on its node: a server's or a source's, from a failed save. */
export interface TmDetailsServerError {
  /** The node's path from the record: json names and row indices, such as `['lines', 1, 'amount']`; empty for the record itself. */
  readonly segments: readonly (string | number)[];
  /** The node's value when the error was recorded; the error shows while the node holds it. */
  readonly value: unknown;
  /** The error, with its message. */
  readonly error: ValidationError.WithoutFieldTree;
}

/** What the platform schema reads beside the descriptor. */
export interface TmDetailsSchemaOptions {
  /** The record's mode, which decides whether a write-once property is read-only; read-only outside create mode when omitted. */
  readonly mode?: Signal<TmDetailsMode>;
  /** The uploads in flight by field path, such as `imageId` or `attachments.0.fileId`; none when omitted. */
  readonly uploads?: { readonly running: Signal<ReadonlySet<string>> };
  /** The recorded server and source errors; none when omitted. */
  readonly errors?: Signal<readonly TmDetailsServerError[]>;
  /** The descriptor of a child collection's entity, by name; a collection whose entity it does not give takes no rules. */
  readonly child?: (entity: string) => TmEntityDescriptor | undefined;
}

/** The ownerships the server, the database or a derivation writes. */
const SERVER_WRITTEN: ReadonlySet<string> = new Set(['server-owned', 'derived', 'database-owned']);

/** No uploads in flight. */
const NO_UPLOADS: { readonly running: Signal<ReadonlySet<string>> } = { running: signal<ReadonlySet<string>>(new Set()).asReadonly() };

/**
 * Applies the platform's rules to a details record's form: on every
 * property the client writes, `required` unless it is nullable, a flag, a
 * multilingual twin or filled by the server, `maxLength` and the integer
 * digits its precision leaves; `readonly` on the properties the server, the
 * database or a derivation writes, and on write-once ones outside create
 * mode; pending while an upload for the path runs; and the recorded server
 * errors, each on its node while the node keeps the value it had. Every row
 * of a child collection takes its entity's rules. Call it first in a
 * schema function, so a distribution's rules add to it.
 *
 * @typeParam T - The record's type.
 * @param p - The record's schema path.
 * @param descriptor - The record's entity descriptor.
 * @param options - The mode, the uploads, the recorded errors and the child entities.
 *
 * @example
 * ```ts
 * const tree = form(model, (p) => {
 *   tmDetailsPlatformSchema(p, descriptor, { mode, child: (entity) => entities.get(entity) });
 *   required(p.creditLimit, { when: ({ valueOf }) => valueOf(p.customerType) === 'Corporate' });
 * });
 * ```
 */
export function tmDetailsPlatformSchema<T>(p: SchemaPathTree<T>, descriptor: TmEntityDescriptor, options: TmDetailsSchemaOptions = {}): void {
  entityRules(p as unknown as SchemaPathTree<unknown>, descriptor, options, null);

  // --- The recorded errors, each on the node its path reaches (spec 0031 §8.5) ---
  const errors = options.errors;
  if (errors !== undefined) {
    validateTree(p as unknown as SchemaPath<unknown>, ({ fieldTree }) => {
      const shown: ValidationError.WithOptionalFieldTree[] = [];
      for (const recorded of errors()) {
        const node = ɵtmDetailsNodeAt(fieldTree as FieldTree<unknown>, recorded.segments);
        if (Object.is(node().value(), recorded.value)) {
          shown.push({ ...recorded.error, fieldTree: node });
        }
      }
      return shown;
    });
  }
}

/**
 * The deepest node a path reaches in a form: the walk stops at the first
 * segment the tree does not hold, so an error on a member no form renders
 * lands on its nearest rendered ancestor.
 *
 * @param tree - The form, or a node of it.
 * @param segments - Json names and row indices.
 * @returns The node.
 */
export function ɵtmDetailsNodeAt(tree: FieldTree<unknown>, segments: readonly (string | number)[]): FieldTree<unknown> {
  let node = tree;
  for (const segment of segments) {
    const next = (node as unknown as Readonly<Record<string | number, FieldTree<unknown> | undefined>>)[segment];
    if (next === undefined) {
      break;
    }
    node = next;
  }
  return node;
}

/**
 * Applies one entity's rules at a path: its properties', then each child
 * collection's rows'. A child row's key to its parent takes none: the
 * server writes it on save.
 *
 * @param p - The entity's schema path: the record, or a child row.
 * @param descriptor - The entity's descriptor.
 * @param options - The schema's options.
 * @param parentKey - A child row's property that names its parent, or `null` on the record.
 */
function entityRules(p: SchemaPathTree<unknown>, descriptor: TmEntityDescriptor, options: TmDetailsSchemaOptions, parentKey: string | null): void {
  // A schema path tree names a child path under every member name.
  const paths = p as unknown as Readonly<Record<string, SchemaPath<unknown>>>;
  for (const property of descriptor.properties) {
    const path = paths[property.json];
    if (path !== undefined && property.name !== parentKey) {
      propertyRules(path, property, options);
    }
  }
  for (const child of descriptor.children) {
    const rows = paths[child.json];
    const entity = options.child?.(child.entity);
    if (rows !== undefined && entity !== undefined) {
      applyEach(rows as SchemaPath<readonly unknown[]>, (row) => entityRules(row as SchemaPathTree<unknown>, entity, options, child.parentKey));
    }
  }
}

/**
 * Applies one property's rules.
 *
 * @param path - The property's schema path.
 * @param property - The property.
 * @param options - The schema's options.
 */
function propertyRules(path: SchemaPath<unknown>, property: TmPropertyDescriptor, options: TmDetailsSchemaOptions): void {
  // --- Read-only: what the client never writes, and a write-once value once created ---
  if (SERVER_WRITTEN.has(property.ownership)) {
    readonly(path);
    return;
  }
  if (property.ownership === 'write-once') {
    const mode = options.mode;
    readonly(path, () => mode?.() !== 'create');
  }

  // --- Required: a flag's false counts as empty, a twin may stay empty, the server fills a defaulted one ---
  const twin = (property.multilingual?.slot ?? 1) > 1;
  if (!property.nullable && property.type !== 'bool' && !twin && property.defaulted !== true) {
    required(path);
  }

  // --- Maximum length ---
  if (property.maxLength !== undefined) {
    maxLength(path as SchemaPath<string>, property.maxLength);
  }

  // --- Digits: at most precision − scale before the decimal point; the editor caps the decimals ---
  if (property.precision !== undefined) {
    const maxDigits = property.precision - (property.scale ?? 0);
    validate(path, ({ value }) =>
      integerDigits(value()) > maxDigits ? ({ kind: 'precision', maxDigits } as ValidationError.WithoutFieldTree) : undefined,
    );
  }

  // --- Pending while an upload for the path runs (spec 0031 §6.3) ---
  if (property.blob !== undefined) {
    const uploads = options.uploads ?? NO_UPLOADS;
    validateAsync(path, {
      params: ({ pathKeys }) => {
        const key = pathKeys().join('.');
        return uploads.running().has(key) ? key : undefined;
      },
      factory: (params) =>
        // Never settles: the field is pending until the upload leaves the running set, which idles the resource.
        resource({
          params: () => params(),
          loader: ({ abortSignal }) =>
            new Promise<undefined>((_resolve, reject) => abortSignal.addEventListener('abort', () => reject(abortSignal.reason as Error))),
        }),
      onSuccess: () => undefined,
      onError: () => undefined,
    });
  }
}

/**
 * The integer digits of a number or of a decimal's text: `1032.5` and
 * `"-1032.5000"` have four; anything that is not a number has none.
 *
 * @param value - The value.
 * @returns The digits before the decimal point, leading zeros dropped.
 */
function integerDigits(value: unknown): number {
  if (typeof value === 'number') {
    const whole = Math.trunc(Math.abs(value));
    return Number.isFinite(whole) && whole > 0 ? BigInt(whole).toString().length : 0;
  }
  if (typeof value !== 'string') {
    return 0;
  }
  const match = /^\s*[+-]?(\d*)(?:\.\d*)?\s*$/.exec(value);
  return match === null ? 0 : (match[1] ?? '').replace(/^0+/, '').length;
}
