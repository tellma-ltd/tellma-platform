// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The tenantless pages' chunk (spec 0027 §6): the tenant picker and the
// signed-out page, with the tenantless shell they render in. The shell entry
// point loads it with import() when the route table asks for either page,
// so a tenant route's first frame never carries it.

export { TmSignedOutPage } from './tm-signed-out-page';
export { TmTenantPicker } from './tm-tenant-picker';
export { TmTenantlessShell } from './tm-tenantless-shell';
