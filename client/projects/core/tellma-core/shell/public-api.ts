/**
 * Public API Surface of @tellma/core/shell — the app's shells, in the frame
 * they share: the tenantless shell with the tenant picker and the
 * signed-out page, and the tenant shell, which the route table loads with
 * the app.
 *
 * The tenantless shell and its two pages ship in a chunk of their own that
 * only `/tenants` and `/signed-out` load, so this entry point exports their
 * types; the route table reaches the classes through the component map. The
 * tenant shell is a stand-in for now, enough for the app to boot, route and
 * render its pages; the shell with its app bar, launcher and pins replaces
 * it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmTenantShell } from './src/tm-tenant-shell';
// The tenantless chunk's components, as types: the classes load with the chunk.
export type { TmSignedOutPage, TmTenantlessShell, TmTenantPicker } from './src/tenantless/tm-tenantless';

// The components the primary entry point loads by name, the shells above
// among them; not for app code.
export { TM_SHELL as ɵTM_SHELL } from './src/tm-shell-components';
