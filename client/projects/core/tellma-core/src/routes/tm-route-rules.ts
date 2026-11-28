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

/** The sections of the My settings hub, each a child route of `me`. */
export const TM_ME_SECTIONS: readonly string[] = ['profile', 'region', 'display', 'notifications'];

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
    return this.#entities.ɵhasDetailsPage(summary);
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
