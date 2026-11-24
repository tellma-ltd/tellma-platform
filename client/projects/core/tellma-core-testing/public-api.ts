/**
 * Public API Surface of @tellma/core-testing — test providers, fakes and
 * component harnesses for apps built on @tellma/core. Its `/e2e` entry
 * point holds the Playwright helpers, and runs in Node.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { provideTellmaTesting } from './src/provide-tellma-testing';
export { provideTellmaStateTesting } from './src/provide-tellma-state-testing';
export { TmColumnFilterHarness } from './src/tm-column-filter-harness';
export { TmEntityFieldHarness, TmEntityMultiFieldHarness } from './src/tm-entity-field-harness';
export { tmFakeAssignmentsSummary, tmFakeInboxSummary } from './src/tm-fake-attention';
export { TmBannerHostHarness } from './src/tm-banner-host-harness';
export { TmFakeApi, type TmFakeApiCall, type TmFakeApiCallKind, type TmFakeApiResponder } from './src/tm-fake-api';
export { TmFakeHub, TmFakeHubConnection } from './src/tm-fake-hub';
export { TmFakeLocks } from './src/tm-fake-locks';
export { TmFakeSession, tmTestProfile } from './src/tm-fake-session';
export { TmFakeTabBus } from './src/tm-fake-tab-bus';
export { TmFakeTelemetry } from './src/tm-fake-telemetry';
export { TmPageContextHarness } from './src/tm-page-context-harness';
export { TmTenantPickerHarness, type TmTenantPickerName, TmTenantPickerRowHarness } from './src/tm-tenant-picker-harness';
export { tmTestEntities, type TmTestEntities } from './src/tm-test-entities';
export { tmTestEntitiesChunk, tmTestMetadata } from './src/tm-test-metadata';
export { tmWarmTenant, type TmWarmTenantOptions } from './src/tm-warm-tenant';
