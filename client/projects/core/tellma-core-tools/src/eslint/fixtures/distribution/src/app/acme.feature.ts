// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// A distribution's declarations, as the ESLint preset's tests lint them. The
// tests lint other text under this file's name, within its project.
declare function tmEntity(name: string, declaration: object): unknown;

/** The customers entity of the fixture distribution. */
export const customers = tmEntity('acme.Customer', { details: () => import('./customer-details') });
