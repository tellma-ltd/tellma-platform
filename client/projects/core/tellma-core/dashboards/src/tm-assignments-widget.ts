// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, effect, inject, untracked, type Signal } from '@angular/core';
import { TmApi, TmLocalization, TmTelemetry, type TmEntityType } from '@tellma/core';
import { TmAttention } from '@tellma/core/attention';
import { TmAssignmentList, type TmAssignmentActivation, type TmAssignmentParty } from '@tellma/core/attention-rows';
import { TmEmptyState } from '@tellma/core-ui/empty-state';

import { tmPrepareDashboards } from './tm-dashboard-setup';
import { TM_HOME_SURFACE } from './tm-dashboard-tile';
import { tmHomePosition } from './tm-home-telemetry';
import { injectTmWidget } from './tm-widget-context';

// The body of `core.assignments-to-me` and `core.assignments-by-me` (spec
// 0035 §9.2, spec 0028 §7.4): the newest open assignments waiting on the
// user, or handed by the user to others, at most ten, from the stored
// assignments document. The list follows the document live, so a row
// arriving or leaving changes only this list; only a forced refresh reads
// the document again. Before the document exists the list shows five
// skeleton rows, or, offline, says the rows show once the app reconnects.
// The widget type is the party: one body serves both types.

/** The most rows a home list shows. */
const MAX_ROWS = 10;

/** What the body shows. */
type TmAssignmentsView = 'rows' | 'empty' | 'offline' | 'loading';

/** The assignments waiting on the user, or handed to others, as a dashboard tile's body. Internal. */
@Component({
  selector: 'tm-assignments-widget',
  imports: [TmAssignmentList, TmEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (view()) {
      @case ('rows') {
        <!-- The newest rows, each its target's link. -->
        <tm-assignment-list
          variant="home"
          [party]="party"
          [items]="items()"
          [related]="related()"
          [label]="title()"
          (activated)="opened($event)"
        />
      }
      @case ('empty') {
        <!-- Nothing open. -->
        <tm-empty-state size="panel" [icon]="party === 'to-me' ? 'inbox' : 'send'" [heading]="empty()" />
      }
      @case ('offline') {
        <!-- Offline, and no document yet. -->
        <tm-empty-state size="panel" icon="wifi-off" [heading]="offline()" />
      }
      @default {
        <!-- No document yet: five skeleton rows of the rows' geometry. -->
        <tm-assignment-list variant="home" [party]="party" [loading]="true" />
      }
    }
  `,
  styles: `
    :host {
      display: block;
      block-size: 100%;
    }
  `,
})
export class ɵTmAssignmentsWidget {
  readonly #widget = injectTmWidget();
  readonly #attention = inject(TmAttention);
  readonly #api = inject(TmApi);
  readonly #l10n = inject(TmLocalization);
  readonly #telemetry = inject(TmTelemetry);
  readonly #home = inject(TM_HOME_SURFACE);

  /** Whose assignments the list shows: the widget type's. */
  protected readonly party: TmAssignmentParty = this.#widget.type === 'core.assignments-by-me' ? 'by-me' : 'to-me';

  /** The rows, newest first, at most ten. */
  protected readonly items: Signal<readonly TmEntityType<'core.Assignment'>[]> = computed(() => {
    const summary = this.#attention.assignments();
    return (this.party === 'to-me' ? summary?.latest : summary?.latestByMe)?.slice(0, MAX_ROWS) ?? [];
  });

  /** The users the rows name. */
  protected readonly related = computed(() => this.#attention.assignments()?.related ?? {});

  /** What the body shows: the rows, the empty or offline state, or the skeleton rows. */
  protected readonly view: Signal<TmAssignmentsView> = computed(() => {
    if (this.#attention.assignments() === undefined) {
      return this.#api.online() ? 'loading' : 'offline';
    }
    return this.items().length > 0 ? 'rows' : 'empty';
  });

  /** The list's accessible name: the tile's title. */
  protected readonly title = this.#l10n.ɵtext(`tm.dashboard.${this.party === 'to-me' ? 'toMe' : 'byMe'}.title`);

  /** The empty list's heading. */
  protected readonly empty = this.#l10n.ɵtext(`tm.dashboard.${this.party === 'to-me' ? 'toMe' : 'byMe'}.empty`);

  /** The offline heading. */
  protected readonly offline = this.#l10n.ɵtext('tm.dashboard.assignmentsOffline');

  /** Registers the strings, and reads the document again on a forced refresh. */
  constructor() {
    tmPrepareDashboards();
    // --- A forced refresh, the only tick a live list takes, reads the document again ---
    const first = untracked(this.#widget.tick).generation;
    effect(() => {
      if (this.#widget.tick().generation !== first) {
        untracked(() => void this.#attention.refresh('assignments'));
      }
    });
  }

  /**
   * Reports a row the user opened, on the home page.
   *
   * @param activation - The row and its place.
   */
  protected opened(activation: TmAssignmentActivation): void {
    if (this.#home) {
      this.#telemetry.event('tm.home.item-opened', { list: this.party, position: tmHomePosition(activation.position) });
    }
  }
}
