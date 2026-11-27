// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TmLocalization, TmUrls, ɵtmRecentPageIcon, type TmPageKind } from '@tellma/core';
import { TmRecents } from '@tellma/core/recents';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import { TmIcon } from '@tellma/core-ui/icon';

import { tmPrepareDashboards } from './tm-dashboard-setup';

// The body of `core.recents` (spec 0035 §9.2): the pages the user last
// visited, newest first and at most ten, as one list of links. A row draws
// its launcher entry's icon, else its family's; it opens its page, and
// hovering or focusing it preloads that page's chunks. The list follows the
// recent pages across tabs, so a visit elsewhere changes only this list.

/** The recently visited pages, as a dashboard tile's body. */
@Component({
  selector: 'tm-recents-widget',
  imports: [RouterLink, TmEmptyState, TmIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (recents.pages(); as pages) {
      @if (pages.length > 0) {
        <!-- One link per page, each row a fixed height so ten fill the tile. -->
        <ul class="tm-recents-widget">
          @for (page of pages; track page.url) {
            <li>
              <a
                class="tm-dashboard-list-row tm-recents-widget__link"
                [routerLink]="page.url"
                (pointerenter)="preload(page.url)"
                (focus)="preload(page.url)"
              >
                <tm-icon class="tm-recents-widget__icon" [name]="page.icon ?? iconOf(page.kind)" size="sm" />
                <span class="tm-recents-widget__title">{{ page.title() }}</span>
              </a>
            </li>
          }
        </ul>
      } @else {
        <!-- Nothing visited yet. -->
        <tm-empty-state size="panel" icon="history" [heading]="empty()" />
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .tm-recents-widget {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .tm-recents-widget__link {
      color: var(--text-body);
      text-decoration: none;
    }
    .tm-recents-widget__link:hover {
      background: var(--surface-hover);
    }
    .tm-recents-widget__link:focus-visible {
      /* A transparent real outline beneath the token ring: forced colors strip box-shadows. */
      outline: var(--focus-ring-width) solid transparent;
      outline-offset: calc(-1 * var(--focus-ring-width));
      box-shadow: inset var(--focus-ring);
    }
    .tm-recents-widget__icon {
      flex: none;
      color: var(--text-secondary);
    }
    .tm-recents-widget__title {
      min-inline-size: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
})
export class ɵTmRecentsWidget {
  readonly #urls = inject(TmUrls);

  /** The recent pages, newest first. */
  protected readonly recents = inject(TmRecents);

  /** The empty list's heading. */
  protected readonly empty = inject(TmLocalization).ɵtext('tm.dashboard.recents.empty');

  /** The icon a page's family draws when no launcher entry names one. */
  protected readonly iconOf: (kind: TmPageKind) => string = ɵtmRecentPageIcon;

  /** Registers the entry point's strings and icons. */
  constructor() {
    tmPrepareDashboards();
  }

  /**
   * Preloads a page's chunks at idle, before the user opens it.
   *
   * @param url - The page's URL.
   */
  protected preload(url: string): void {
    this.#urls.preload(url);
  }
}
