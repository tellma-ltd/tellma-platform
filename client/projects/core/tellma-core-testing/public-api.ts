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
export { type TmDetailsPageButton, TmDetailsPageHarness } from './src/tm-details-page-harness';
export { type TmDiagnosticsPanelButton, TmDiagnosticsPanelHarness, TmSupportOfflineHarness } from './src/tm-diagnostics-panel-harness';
export { TmEntityFieldHarness, TmEntityMultiFieldHarness } from './src/tm-entity-field-harness';
export { TmExpressionEditorHarness, type TmExpressionEditorChip } from './src/tm-expression-editor-harness';
export { type TmFakeDetails, type TmFakeDetailsCall, type TmFakeDetailsOptions, tmFakeDetails } from './src/tm-fake-details';
export { tmFakeAssignmentsSummary, tmFakeInboxSummary } from './src/tm-fake-attention';
export { TmBannerHostHarness } from './src/tm-banner-host-harness';
export { TmFakeApi, type TmFakeApiCall, type TmFakeApiCallKind, type TmFakeApiResponder } from './src/tm-fake-api';
export { TmFakeClientCache, type TmFakeClientCacheSweep } from './src/tm-fake-client-cache';
export { TmFakeDiscover, type TmFakeDiscoverAnswer } from './src/tm-fake-discover';
export { TmFakeHub, TmFakeHubConnection } from './src/tm-fake-hub';
export { TmFakeLocks } from './src/tm-fake-locks';
export { TmFakeSession, tmTestProfile } from './src/tm-fake-session';
export { TmFakeTabBus } from './src/tm-fake-tab-bus';
export { TmFakeTelemetry } from './src/tm-fake-telemetry';
export {
  TmLauncherGroupHarness,
  type TmLauncherGroupHarnessFilters,
  type TmLauncherGroupKind,
  TmLauncherHarness,
  TmLauncherOptionHarness,
  type TmLauncherOptionHarnessFilters,
  type TmLauncherSurfaceKind,
} from './src/tm-launcher-harness';
export { TmPageContextHarness } from './src/tm-page-context-harness';
export { TmPathPickerHarness } from './src/tm-path-picker-harness';
export { TmSearchPageHarness, type TmSearchPageCreate } from './src/tm-search-page-harness';
export { TmSessionMenuHarness, type TmSessionMenuTenantCard } from './src/tm-session-menu-harness';
export { TmTenantPickerHarness, type TmTenantPickerName, TmTenantPickerRowHarness } from './src/tm-tenant-picker-harness';
export {
  TmTenantShellHarness,
  type TmTenantShellLauncherControl,
  type TmTenantShellMain,
  type TmTenantShellSlot,
} from './src/tm-tenant-shell-harness';
export { tmTestEntities, type TmTestEntities } from './src/tm-test-entities';
export { tmTestEntitiesChunk, tmTestMetadata } from './src/tm-test-metadata';
export { tmTestShellSlot, TmTestShellSlotPanel, type TmTestShellSlotOptions } from './src/tm-test-shell-slot';
export { tmWarmTenant, type TmWarmTenantOptions } from './src/tm-warm-tenant';
