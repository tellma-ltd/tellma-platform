// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { untracked } from '@angular/core';
import type { LiveAnnouncer } from '@angular/cdk/a11y';

import type { TmUiTranslateFn } from '@tellma/core-ui';
import type { TmGridNotice, TmGridOpKind } from '@tellma/core-ui/grid-engine';

/**
 * The grid's live-region voice: localizes a library string key once
 * (untracked — announcements are one-shot, never reactive) and speaks it
 * through the CDK `LiveAnnouncer`. Also owns the mapping from the engine's
 * semantic notices (facts, no strings) to the localized announcement keys.
 */
export class ɵTmGridAnnouncements {
  constructor(
    private readonly announcer: LiveAnnouncer,
    private readonly translate: TmUiTranslateFn,
  ) {}

  /** Resolves the key (with params) to the active locale and announces it politely. */
  announce(key: string, params?: Record<string, unknown>): void {
    const text = untracked(this.translate(key, params));
    void this.announcer.announce(text, 'polite');
  }

  /** Localizes and announces one engine notice. */
  notice(notice: TmGridNotice): void {
    switch (notice.kind) {
      case 'copyRefusedMisaligned':
        this.announce('grid.announce.copyRefused');
        return;
      case 'pasteComplete':
        this.announce('grid.announce.pasted', {
          cells: notice.cells,
          errors: notice.errors,
          pending: notice.pending,
        });
        if (notice.rowsDropped > 0) {
          this.announce('grid.announce.pasteRowsDropped', { count: notice.rowsDropped });
        }
        return;
      case 'resolutionComplete':
        this.announce('grid.announce.resolved', { count: notice.resolved, errors: notice.errors });
        return;
      case 'undoApplied':
        this.announce('grid.announce.undone', {
          action: this.opLabel(notice.opKind),
          skipped: notice.skippedRows,
        });
        return;
      case 'redoApplied':
        this.announce('grid.announce.redone', {
          action: this.opLabel(notice.opKind),
          skipped: notice.skippedRows,
        });
        return;
      case 'undoSkippedMissingRows':
        this.announce('grid.announce.undoSkipped');
        return;
      case 'redoSkippedMissingRows':
        this.announce('grid.announce.redoSkipped');
        return;
      case 'moveIntoDescendantRejected':
        this.announce('grid.announce.moveRejected');
        return;
      case 'editorCancelledRowRemoved':
        this.announce('grid.announce.editorCancelledRowRemoved');
        return;
      case 'rowsInserted':
        this.announce('grid.announce.rowsInserted', { count: notice.count });
        return;
      case 'rowsDeleted':
        this.announce('grid.announce.rowsDeleted', { count: notice.count });
        return;
      case 'rowsMoved':
        this.announce('grid.announce.rowsMoved', { count: notice.count });
        return;
    }
  }

  /** The localized short label of a history op kind ('cell edit', 'paste', …). */
  private opLabel(op: TmGridOpKind): string {
    return untracked(this.translate(`grid.op.${op}`));
  }
}
