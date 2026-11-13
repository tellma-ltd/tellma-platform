/**
 * Public API Surface of @tellma/core — the Tellma app framework, the runtime
 * a distribution's single-page app imports. This primary entry point loads
 * with the app; each lazy boundary of the framework is a secondary entry
 * point of its own.
 *
 * It holds the distribution API (`provideTellma`, `tmFeature` and the
 * declaration functions), the client metadata's types and loaders, the
 * HTTP layer with its error and plain-JSON value types, the exact decimal
 * helpers, the client store and the tab bus, and the seams through which
 * telemetry, the request log and page scopes are provided.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Plain JSON and exact decimals.
export type { TmDecimal, TmId, TmJson, TmJsonObject, TmJsonSchema } from './src/json/tm-json';
export { TmDecimals } from './src/json/tm-decimals';

// Telemetry.
export {
  TmTelemetry,
  type TmTelemetryItem,
  type TmTelemetryOptions,
  type TmTelemetryProps,
} from './src/observe/tm-telemetry';

// The HTTP layer.
export {
  TmApi,
  type TmBlobDescriptor,
  type TmDownload,
  type TmDownloadOptions,
  type TmReadOptions,
  type TmSendOptions,
  type TmUploadOptions,
  type TmWriteOptions,
} from './src/api/tm-api';
export {
  TmApiError,
  type TmApiErrorInit,
  type TmApiErrorKind,
  type TmProblem,
  type TmProblemError,
} from './src/api/tm-api-error';

// The request log.
export {
  TmRequestLog,
  type TmRequestEnd,
  type TmRequestHandle,
  type TmRequestOutcome,
  type TmRequestRecord,
  type TmRequestStart,
} from './src/requests/tm-request-log';

// Page scopes.
export { provideTmPageScope } from './src/context/tm-page-scope';

// The distribution API: the app configuration and its providers.
export type {
  TmAppConfig,
  TmCalendarDefinition,
  TmLanguageDefinition,
  TmStringTable,
  TmStringTree,
} from './src/app/tm-app-config';
export { provideTellma, tmAppConfigOf } from './src/app/provide-tellma';
export { TM_APP_DECLARATIONS, type TmAppDeclarations } from './src/app/tm-app-declarations';
export type { TmRouteSkeleton } from './src/app/tm-route-data';
export type { TmPageKind } from './src/context/tm-page-kind';

// Features and the declaration functions.
export type { TmDeclarationKey, TmLazyComponent, TmSecurableRef } from './src/declarations/tm-declaration-types';
export { tmFeature, type TmFeature, type TmFeatureDefinition } from './src/declarations/tm-feature';
export { tmOverride, type TmDeclarationPatch, type TmOverride } from './src/declarations/tm-override';
export { tmPage, type TmPageDeclaration, type TmPageDefinition } from './src/declarations/tm-page';
export { tmEntity, type TmEntityDeclaration, type TmEntityDeclarationInput } from './src/entity/tm-entity-declaration';
export {
  tmCodedReport,
  type TmCodedReportDeclaration,
  type TmCodedReportMetadata,
  type TmCodedReportPatch,
  type TmCodedReportRun,
  type TmReportArgs,
  type TmReportChoice,
  type TmReportDisplay,
  type TmReportFormat,
  type TmReportKind,
  type TmReportParameter,
  type TmReportParameterType,
} from './src/reports/tm-coded-report';
export {
  tmWidget,
  type TmWidgetDefinition,
  type TmWidgetSize,
  type TmWidgetTypeDeclaration,
} from './src/dashboards/tm-widget';
export type { TmSettingsTabDeclaration } from './src/admin/tm-settings-tab';
export type { TmNavContext, TmNavEntry, TmNavSection } from './src/nav/tm-nav-declarations';
export type { TmShellSlot } from './src/nav/tm-shell-slot';
export type {
  TmDistributionIdentityInfo,
  TmDistributionInfo,
  TmDistributionLinks,
  TmDistributionSessionInfo,
  TmDistributionSupportInfo,
  TmDistributionTelemetryInfo,
} from './src/state/tm-distribution-info';

// The merge rule, shared with the config check; not for app code.
export {
  ɵtmMergeAppDeclarations,
  type ɵTmMergeProblem,
  type ɵTmMergeProblemCode,
  type ɵTmMergeResult,
} from './src/declarations/tm-merge';

// The app's clock.
export { TM_NOW } from './src/app/tm-now';

// The boot script's result, and the boot record it keeps.
export type {
  TmBootConfig,
  TmBootLanguage,
  TmBootPageReason,
  TmBootResult,
  TmBootState,
  TmBootStrings,
} from './src/boot/tm-boot-types';
export type { TmBootRecord, TmBootTheme } from './src/state/tm-boot-record';

// The session: the profile cookie, sign-in and step-up, and the guard.
export { tmParseProfileCookie, type TmProfile } from './src/session/tm-profile';
export type { TmLoginMark } from './src/session/tm-login-mark';
export {
  TmSession,
  type TmSessionCheck,
  type TmSignInOptions,
  type TmStepUpChallenge,
} from './src/session/tm-session';
export type { TmSessionState } from './src/session/tm-session-paths';
export { tmSessionGuard } from './src/session/tm-session-guard';

// The client state: the store of cached documents and the boot record, and
// the tab bus with its message catalogue.
export {
  TmClientStore,
  TmStoreRefusedError,
  type TmBootRecordAccess,
  type TmStoredDoc,
  type TmStoreRefusedReason,
  type TmSweepRule,
} from './src/state/store/tm-client-store';
export type { TmPartition } from './src/state/store/tm-store-layout';
export { TM_DOC_FORMATS } from './src/state/store/tm-doc-formats';
export { TmTabBus } from './src/state/bus/tm-tab-bus';
export type {
  TmBusEnvelope,
  TmBusMessage,
  TmDraftsQueryMessage,
  TmDraftsReplyMessage,
  TmHubEventMessage,
  TmHubStatusMessage,
  TmLeaderProbeMessage,
  TmPrefsChangedMessage,
  TmSessionMessage,
  TmStoreChangedMessage,
} from './src/state/bus/tm-bus-messages';
export type { TmClientEvent, TmHubState } from './src/state/leader/tm-hub-types';

// The client store's browser, which @tellma/core-testing replaces; not for app code.
export { TM_STORE_HOST as ɵTM_STORE_HOST, type TmStoreHost as ɵTmStoreHost } from './src/state/store/tm-store-host';

// The launch: the single-membership rule and the automatic-entry mark.
export { tmSingleLiveTenant } from './src/launch/tm-single-live-tenant';
export type { TmMembership, TmTenantCategory, TmTenantState } from './src/launch/tm-membership';
export { TmLaunch } from './src/launch/tm-launch';

// The client metadata: its chunks, their loaders and the loaded Core chunk.
export {
  TM_CLIENT_METADATA,
  type TmClientMetadata,
  type TmContracts,
  type TmDistributionMetadata,
  type TmEntitiesChunk,
  type TmFeatureMetadata,
  type TmLanguageMetadata,
  type TmNotificationChannelMetadata,
  type TmRoutingMetadata,
} from './src/metadata/tm-client-metadata';
export {
  tmMetadataLoaders,
  type TmMetadataLoaders,
  type TmMetadataPaths,
  type TmStringPack,
} from './src/metadata/tm-metadata-loaders';
export type {
  TmActionField,
  TmActionMetadata,
  TmBlobKindPolicy,
  TmChildMetadata,
  TmDataType,
  TmDisplayNameRule,
  TmEntityMetadata,
  TmEntityOperation,
  TmEntitySummary,
  TmEnumMetadata,
  TmEnumValueMetadata,
  TmNavigationMetadata,
  TmPropertyMetadata,
  TmValueDescriptor,
} from './src/entity/tm-entity-metadata';
export type {
  TmJobHandlerMetadata,
  TmJobResultMetadata,
  TmSecurableMetadata,
  TmSecurableOwnerMetadata,
  TmSettingCategoryMetadata,
  TmSettingKeyMetadata,
  TmSettingValueType,
  TmSettingVisibility,
} from './src/admin/tm-admin-metadata';
export type { TmNotificationSeverity, TmNotificationTypeMetadata } from './src/attention/tm-notification-metadata';
