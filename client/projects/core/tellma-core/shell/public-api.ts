/**
 * Public API Surface of @tellma/core/shell — the app's shells: the tenant
 * picker, the signed-out page and the tenant shell, which the route table
 * loads with the app, beside the pages and views it renders only on some
 * routes: the launch page, the not-found page and the forbidden view.
 *
 * Each is a plain stand-in for now, enough for the app to boot, route and
 * render its pages; the shells with their app bars, launcher and pins
 * replace them.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmTenantShell } from './src/tm-tenant-shell';
export { TmTenantPicker } from './src/tm-tenant-picker';
export { TmSignedOutPage } from './src/tm-signed-out-page';

// The components the primary entry point loads by name, the shells above
// among them; not for app code.
export { TM_SHELL as ɵTM_SHELL } from './src/tm-shell-components';
