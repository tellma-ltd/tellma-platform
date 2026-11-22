// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { type TmSecurableRef, ɵTmRouteError, type ɵTmRouteErrorView } from '@tellma/core';

// The forbidden view (spec 0024 §4.4, §11): what the route host renders in
// place of a `tmPage` whose permission the user lacks (reason `no-grant`).
// It renders only on such a page, so it loads with the shell instead of the
// app.

// The strings are worded as spec 0024 §11's table words them.
/** The forbidden view's strings. */
const FORBIDDEN: ɵTmRouteErrorView = {
  title: ['tm.errors.forbidden.title', "You don't have permission for this"],
  message: ['tm.errors.forbidden.message', 'It needs “{permission}”. Ask your administrator to grant it.'],
};

/** The view of a page whose permission the user lacks, naming the permission. */
@Component({
  selector: 'tm-forbidden-view',
  imports: [ɵTmRouteError],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<tm-route-error [view]="view" [params]="params()" />`,
})
export class TmForbiddenView {
  /** The permission the page needs. */
  readonly securable = input<TmSecurableRef | undefined>(undefined);

  /** The view's strings. */
  protected readonly view = FORBIDDEN;

  /** The view's placeholders: the permission. */
  protected readonly params = computed(() => {
    const securable = this.securable();
    return { permission: securable ? `${securable[0]} · ${securable[1]}` : '' };
  });
}
