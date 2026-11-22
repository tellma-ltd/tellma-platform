// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ɵTmRouteError, type ɵTmRouteDataInternal, type ɵTmRouteErrorView } from '@tellma/core';

// The not-found page (spec 0024 §9.2, §11): the `**` routes' component,
// under the tenant shell and outside any shell. It renders only when no
// route matches, so it loads with the shell instead of the app.

// The strings are worded as spec 0024 §11's table words them.
/** The not-found view's strings. */
const NOT_FOUND: ɵTmRouteErrorView = {
  title: ['tm.errors.notFound.title', 'Page not found'],
  message: ['tm.errors.notFound.message', 'Check the address, or go back to the start.'],
  tone: 'neutral',
};

/** The not-found page: no route matches the path. */
@Component({
  selector: 'tm-not-found-page',
  imports: [ɵTmRouteError],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Outside any shell, the page brings the document's main landmark. -->
    @if (main) {
      <main><tm-route-error [view]="view" /></main>
    } @else {
      <tm-route-error [view]="view" />
    }
  `,
})
export class TmNotFoundPage {
  /** Whether the page renders outside any shell. */
  protected readonly main = (inject(ActivatedRoute).snapshot.data as ɵTmRouteDataInternal).ɵmain === true;

  /** The view's strings. */
  protected readonly view = NOT_FOUND;
}
