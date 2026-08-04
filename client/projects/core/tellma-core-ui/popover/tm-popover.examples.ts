// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tm-popover` usage — dependency-free template objects consumed
 * by the docs extractor and compiled against the live API by the examples
 * spec.
 */

/** A trigger button toggling a popover with lazy template content. */
export const TriggerAndContent = {
  template: `
    <button tmButton variant="secondary" [tmPopoverTriggerFor]="filters">Filters</button>
    <tm-popover #filters aria-label="Filters">
      <ng-template tmPopoverContent>
        <p>Only lines posted this month.</p>
        <button tmButton variant="ghost" (click)="filters.close()">Done</button>
      </ng-template>
    </tm-popover>
  `,
};

/** Placement is logical: block/inline sides mirror automatically in RTL. */
export const Placement = {
  template: `
    <button tmButton variant="secondary" [tmPopoverTriggerFor]="help">Help</button>
    <tm-popover #help aria-label="Help" position="inline-end" align="center">
      <ng-template tmPopoverContent>
        <p>Opens beside the trigger, flipping when there is no room.</p>
      </ng-template>
    </tm-popover>
  `,
};
