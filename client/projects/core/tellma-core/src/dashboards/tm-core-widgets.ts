// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Type } from '@angular/core';

import { tmWidget, type TmWidgetTypeDeclaration } from './tm-widget';

// The widget types Core registers on the server, declared for the client
// (spec 0035 §4.2): each body lives in @tellma/core/dashboards, so the
// declaration carries only its loader. The server's registry and these keys
// must agree; the build's configuration check refuses a difference. The
// loader's specifier is cast so that the package build does not follow it:
// the dashboards entry point imports this one, and the build would report
// the import back as a cycle.

/** What `@tellma/core/dashboards` holds for the core widget types' bodies. Internal. */
interface TmCoreWidgetBodies {
  /** The body of `core.recents`: the pages the user last visited. */
  readonly ɵTmRecentsWidget: Type<unknown>;
}

/** The widget types of the platform's implicit `core` feature. */
export const TM_CORE_WIDGETS: readonly TmWidgetTypeDeclaration[] = [
  // The recently visited pages, beside the assignment lists on the home page.
  tmWidget('core.recents', {
    labelKey: 'tm.dashboard.recents.title',
    icon: 'history',
    component: () => (import('@tellma/core/dashboards' as string) as Promise<TmCoreWidgetBodies>).then((m) => m.ɵTmRecentsWidget),
    defaultSize: { w: 5, h: 9 },
    minSize: { w: 3, h: 4 },
  }),
];
