// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TmTitleStrategy } from '@tellma/core';
import { TmPageHeader } from '@tellma/core-ui/page-header';

// The page of every route family whose host has no entry point yet: the
// tenant home, search, details, settings, My settings and coded reports
// (spec 0024 §9.2). It shows the routed page's title on the page-header
// layout, so routing, titles and focus work end to end.

/** A routed page's stand-in: its title in the page header. */
@Component({
  selector: 'tm-page-placeholder',
  imports: [TmPageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The routed page's title, which navigation focus moves to. -->
    <tm-page-header>
      <h1 tmPageTitle tabindex="-1">{{ title() }}</h1>
    </tm-page-header>
  `,
})
export class TmPagePlaceholder {
  /** The routed page's title. */
  protected readonly title = inject(TmTitleStrategy).pageTitle;
}
