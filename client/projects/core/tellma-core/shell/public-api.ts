/**
 * Public API Surface of @tellma/core/shell — the app's shells, in the frame
 * they share: the tenantless shell with the tenant picker and the
 * signed-out page, and the tenant shell, which the route table loads with
 * the app.
 *
 * The tenantless shell and its two pages ship in a chunk of their own that
 * only `/tenants` and `/signed-out` load, so this entry point exports their
 * types; the route table reaches the classes through the component map. The
 * tenant shell renders the app bar in the first frame; the session menu,
 * the markers' popovers, the slots' panels and the About and Shortcuts
 * dialogs it opens ship in deferred chunks, so this entry point exports the
 * session menu's type alone. The launcher's service ships here, and its
 * panel, index and search in a deferred chunk of their own. So do the
 * user's pins, which the tenant shell's pinned bar shows; the bar's menus,
 * its popover and the Manage pins dialog ship in deferred chunks.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmAppBar } from './src/app-bar/tm-app-bar';
export { TmTenantShell } from './src/tm-tenant-shell';
export { TmLauncher, type TmLauncherCloseReason, type TmLauncherVia } from './src/launcher/tm-launcher';
export { TmPins, type TmPinFolderView, type TmPinView } from './src/pins/tm-pins';
// The launcher's surface, as its deferred chunk implements it; not for app code.
export type { TmLauncherSurface as ɵTmLauncherSurface } from './src/launcher/tm-launcher';
// The session menu, as a type: the class loads with the overlays' chunk.
export type { TmSessionMenu } from './src/session/tm-session-panels';
// The tenantless chunk's components, as types: the classes load with the chunk.
export type { TmSignedOutPage, TmTenantlessShell, TmTenantPicker } from './src/tenantless/tm-tenantless';

// The components the primary entry point loads by name, the shells above
// among them; not for app code.
export { TM_SHELL as ɵTM_SHELL } from './src/tm-shell-components';
