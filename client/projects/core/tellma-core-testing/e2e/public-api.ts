/**
 * Public API Surface of @tellma/core-testing/e2e — Playwright helpers for the
 * end-to-end suites of apps built on @tellma/core. This entry point runs in
 * Node, inside Playwright tests, and needs the optional peer
 * `@playwright/test`.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { expectNoAxeViolations, tmAxeViolations, type TmAxeViolation } from './tm-axe';
export {
  openTabs,
  tmBootFacts,
  tmBootRecord,
  tmCspWatch,
  tmStoredDocs,
  warmUp,
  type TmBootFacts,
  type TmBootRecordFacts,
  type TmCspViolation,
  type TmCspWatch,
} from './tm-app';
export { TM_E2E_ENV, tmE2eHost, type TmE2eHost } from './tm-e2e-host';
export {
  tmCallsRoute,
  tmIsServerPath,
  tmMailIsFor,
  tmProblemResponse,
  tmSignInCode,
  type TmDroppedAddress,
  type TmDroppedMail,
  type TmFakeProblem,
} from './tm-e2e-wire';
export {
  fakeProblem,
  goOffline,
  holdNetwork,
  type TmHeldRequest,
  type TmNetworkHold,
} from './tm-network';
export { signInAs, tmExpireSession, tmSignInToIdentityAs } from './tm-sign-in';
