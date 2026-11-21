// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmEntitiesChunk, TmEntityDeclaration, TmEntityMetadata, TmEntityOperation } from '@tellma/core';

import type { TmProblemFacts } from './tm-config-problem';

// The checks of merged entity declarations against the Entities chunk (spec
// 0029 §3): `operation-missing`, `entity-not-routable`, `unknown-action` and
// `unknown-path`. The declaration members are typed loosely until the entity
// model lands, so every member is read defensively: a member of an
// unexpected shape is skipped, never thrown on.

/** One problem an entity declaration has, before it is worded. */
export interface TmEntityFinding {
  /** What is wrong. */
  readonly code: 'operation-missing' | 'entity-not-routable' | 'unknown-action' | 'unknown-path';
  /** The declaration member at fault, whose origin is the feature at fault. */
  readonly member: string;
  /** The facts the message needs. */
  readonly facts: TmProblemFacts;
}

/** The operation each declaration member needs. */
const MEMBER_OPERATIONS: readonly (readonly [member: string, operation: TmEntityOperation])[] = [
  ['details', 'details'],
  ['search', 'query'],
  ['columns', 'query'],
  ['filters', 'query'],
  ['tabs', 'query'],
];

/** The tile members that hold paths. */
const TILE_PATH_MEMBERS = ['image', 'title', 'subtitle', 'topEnd', 'badge'] as const;

/**
 * Whether a value is a non-null object.
 *
 * @param value - The value.
 * @returns Whether it is an object.
 */
function isObject(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null;
}

/**
 * Resolves entity paths against the Entities chunk: every segment but the
 * last names a navigation, and the last names a property or a navigation,
 * matched ignoring case.
 */
export class TmPathResolver {
  /** Each entity's metadata, by name. */
  readonly #entities: ReadonlyMap<string, TmEntityMetadata>;

  /**
   * Indexes the Entities chunk.
   *
   * @param chunk - The Entities chunk.
   */
  constructor(chunk: TmEntitiesChunk) {
    this.#entities = new Map(chunk.entities.map((entity) => [entity.name, entity]));
  }

  /**
   * One entity's metadata.
   *
   * @param name - The entity's name.
   * @returns Its metadata, or `undefined` when the chunk lacks it.
   */
  entity(name: string): TmEntityMetadata | undefined {
    return this.#entities.get(name);
  }

  /**
   * Whether a path resolves from an entity.
   *
   * @param root - The entity the path starts from.
   * @param path - The path, such as `Parent.Manager.Name`.
   * @returns Whether every segment resolves.
   */
  resolves(root: string, path: string): boolean {
    const segments = path.split('.');
    let entity = this.#entities.get(root);
    for (const [index, segment] of segments.entries()) {
      if (entity === undefined || segment === '') {
        return false;
      }
      const name = segment.toLowerCase();
      const navigation = entity.navigations.find((candidate) => candidate.name.toLowerCase() === name);
      if (index === segments.length - 1) {
        return navigation !== undefined || entity.properties.some((property) => property.name.toLowerCase() === name);
      }
      entity = navigation === undefined ? undefined : this.#entities.get(navigation.target);
    }
    return false;
  }
}

/**
 * The paths of one declaration member, each with the member that holds it.
 *
 * @param declaration - The merged declaration.
 * @returns The member and path of every path the declaration names.
 */
function declaredPaths(declaration: TmEntityDeclaration): (readonly [member: string, path: string])[] {
  const paths: (readonly [string, string])[] = [];
  const orderPath = (order: unknown): string | null => (typeof order === 'string' ? order.replace(/:desc$/, '') : null);

  // --- columns: a path, a path with the paths its display reads, or a computed column ---
  const columns = declaration['columns'];
  if (Array.isArray(columns)) {
    for (const column of columns as unknown[]) {
      if (typeof column === 'string') {
        paths.push(['columns', column]);
      } else if (isObject(column) && typeof column['path'] === 'string') {
        paths.push(['columns', column['path']]);
        const select = column['select'];
        if (Array.isArray(select)) {
          for (const extra of select as unknown[]) {
            if (typeof extra === 'string') {
              paths.push(['columns', extra]);
            }
          }
        }
      }
    }
  }

  // --- orderBy, ascending or `:desc` ---
  const orderBy = orderPath(declaration['orderBy']);
  if (orderBy !== null) {
    paths.push(['orderBy', orderBy]);
  }

  // --- tile: its image and four corners, unless a component draws it ---
  const tile = declaration['tile'];
  if (isObject(tile) && tile['component'] === undefined) {
    for (const member of TILE_PATH_MEMBERS) {
      const path = tile[member];
      if (typeof path === 'string') {
        paths.push(['tile', path]);
      }
    }
  }

  // --- picker: its description lines and its order ---
  const picker = declaration['picker'];
  if (isObject(picker)) {
    const description = picker['description'];
    if (Array.isArray(description)) {
      for (const path of description as unknown[]) {
        if (typeof path === 'string') {
          paths.push(['picker', path]);
        }
      }
    }
    const pickerOrder = orderPath(picker['orderBy']);
    if (pickerOrder !== null) {
      paths.push(['picker', pickerOrder]);
    }
  }
  return paths;
}

/**
 * Checks one merged entity declaration against its metadata.
 *
 * @param declaration - The merged declaration.
 * @param resolver - The Entities chunk's paths.
 * @returns What is wrong, empty when nothing is; also empty when the chunk lacks the entity.
 */
export function tmEntityFindings(declaration: TmEntityDeclaration, resolver: TmPathResolver): TmEntityFinding[] {
  const metadata = resolver.entity(declaration.name);
  if (metadata === undefined) {
    return [];
  }
  const findings: TmEntityFinding[] = [];

  // --- Members that need an operation the entity lacks ---
  for (const [member, operation] of MEMBER_OPERATIONS) {
    if (declaration[member] !== undefined && !metadata.operations.includes(operation)) {
      findings.push({ code: 'operation-missing', member, facts: { member, operation } });
    }
  }

  // --- A launcher section on an entity the launcher cannot open ---
  if (declaration['section'] !== undefined) {
    if (metadata.segment === null) {
      findings.push({ code: 'entity-not-routable', member: 'section', facts: { reason: 'no-segment' } });
    } else if (!metadata.operations.includes('query')) {
      findings.push({ code: 'entity-not-routable', member: 'section', facts: { reason: 'no-query' } });
    }
  }

  // --- Actions the metadata does not list ---
  const actions = declaration['actions'];
  if (Array.isArray(actions)) {
    const known = new Set(metadata.actions.map((action) => action.name));
    for (const action of actions as unknown[]) {
      if (isObject(action) && typeof action['name'] === 'string' && !known.has(action['name'])) {
        findings.push({ code: 'unknown-action', member: 'actions', facts: { action: action['name'] } });
      }
    }
  }

  // --- Paths that do not resolve ---
  for (const [member, path] of declaredPaths(declaration)) {
    if (!resolver.resolves(declaration.name, path)) {
      findings.push({ code: 'unknown-path', member, facts: { member, path } });
    }
  }
  return findings;
}
