/**
 * Public API Surface of @tellma/core/dashboards — the dashboard grid and
 * the tenant home page, loaded with the home route. The grid lays a set of
 * widgets on twelve columns of fixed rows, keeping the stored positions
 * from 768 px of its own width up and stacking the tiles in reading order
 * below it; each tile renders its widget type's body, or the state that
 * stands in for it, and its body reads its context through
 * `injectTmWidget()`. The home page is a greeting and date line over a
 * code-defined grid of the built-in widgets (the assignments to and by the
 * user, and the recently visited pages), and its host renders a feature's
 * `home` hatch in its place.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The home page: the host the route renders, the page, its greeting and its widgets.
export { TmHomeHost } from './src/tm-home-host';
export { TmHomePage } from './src/tm-home-page';
export { TmHomeGreeting } from './src/tm-home-greeting';
export { TM_HOME_WIDGETS } from './src/tm-home-widgets';

// The grid, its tiles and its layout.
export { TmDashboardGrid, type TmTileState } from './src/tm-dashboard-grid';
export { TmDashboardTile, type TmTileModel, type TmTileView } from './src/tm-dashboard-tile';
export { type TmHomeList, type TmTileList } from './src/tm-list-tiles';
export { tmPackDashboard, type TmPlacement } from './src/tm-pack-dashboard';

// What a widget type's body reads from its tile, and how it reports its runs.
export { injectTmWidget, type TmWidgetContext, type TmWidgetRun, type TmWidgetTick } from './src/tm-widget-context';
// The token a widget body reads its context from, for the testing package's fake dashboard.
export { TM_WIDGET_CONTEXT as ɵTM_WIDGET_CONTEXT } from './src/tm-widget-context';

// The core widget types' bodies, which the primary's declarations load; not for app code.
export { ɵTmAssignmentsWidget } from './src/tm-assignments-widget';
export { ɵTmRecentsWidget } from './src/tm-recents-widget';
