// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector } from '@angular/core';

import { TM_APP_DECLARATIONS } from '../app/tm-app-declarations';
import { TmEntities } from '../entity/tm-entities';
import type { TmEntitySummary } from '../entity/tm-entity-metadata';
import type { TmCodedReportMetadata, TmCodedReportPatch } from '../reports/tm-coded-report';

// The match-time rules of the route grammar (spec 0024 §9.1, §9.2), shared by
// the router's `canMatch` guards and `TmUrls.describe`, so a stored URL is
// classified exactly as the router would route it.

/** A positive integer without leading zeros: a tenant or a record id. */
const POSITIVE = /^[1-9]\d*$/;

/** The sections of the My settings hub, each a child route of `me`. */
export const TM_ME_SECTIONS: readonly string[] = ['profile', 'region', 'display', 'notifications'];

/**
 * The tenant a path segment names: a positive safe integer without leading
 * zeros, so `017` and `0` name none.
 *
 * @param segment - The path segment.
 * @returns The tenant's id, or `null`.
 */
export function tmTenantSegment(segment: string | undefined): number | null {
  const id = Number(segment);
  return segment !== undefined && POSITIVE.test(segment) && Number.isSafeInteger(id) ? id : null;
}

/**
 * Whether a path segment is a record id: a positive integer without leading
 * zeros. A `long` key's id may exceed the safe integers, so it stays text.
 *
 * @param segment - The path segment.
 * @returns Whether it is an id.
 */
export function tmIsIdSegment(segment: string | undefined): boolean {
  return segment !== undefined && POSITIVE.test(segment);
}

/**
 * The route grammar's lookups at match time, once the Core chunk is loaded:
 * which segments are stacks, lists and details pages, and which keys are
 * coded reports.
 */
@Injectable({ providedIn: 'root' })
export class TmRouteRules {
  readonly #injector = inject(Injector);
  readonly #entities = inject(TmEntities);

  /**
   * The stack an entity segment routes to: a search page.
   *
   * @param segment - The path segment.
   * @returns The stack's summary, or `undefined` when the segment is no stack.
   */
  stack(segment: string): TmEntitySummary | undefined {
    const summary = this.#entities.bySegment(segment);
    return summary?.kind === 'stack' ? summary : undefined;
  }

  /**
   * The entity a drill-through list's segment shows: a stack or a child.
   *
   * @param segment - The path segment.
   * @returns The entity's summary, or `undefined`.
   */
  list(segment: string): TmEntitySummary | undefined {
    const summary = this.#entities.bySegment(segment);
    return summary?.kind === 'stack' || summary?.kind === 'child' ? summary : undefined;
  }

  /**
   * The stack an entity segment routes to when the entity has a details page.
   *
   * @param segment - The path segment.
   * @returns The stack's summary, or `undefined`.
   */
  details(segment: string): TmEntitySummary | undefined {
    const summary = this.stack(segment);
    return summary && this.hasDetailsPage(summary) ? summary : undefined;
  }

  /**
   * Whether an entity has a details page: its API offers `details`, and its
   * merged declaration has a `details` member, which the server cannot see.
   *
   * @param summary - The entity's summary.
   * @returns Whether it has one.
   */
  hasDetailsPage(summary: TmEntitySummary): boolean {
    return summary.operations.includes('details') && this.#entities.declaration(summary.name)?.['details'] !== undefined;
  }

  /**
   * The coded report a key names, as the declarations merged it.
   *
   * @param key - The report's key.
   * @returns The report, or `undefined`.
   */
  report(key: string): (TmCodedReportMetadata & TmCodedReportPatch) | undefined {
    return this.#injector.get(TM_APP_DECLARATIONS, null)?.reports.find((report) => report.key === key);
  }
}
