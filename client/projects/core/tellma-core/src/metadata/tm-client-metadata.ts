// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, InjectionToken, inject } from '@angular/core';

import type { TmJobHandlerMetadata, TmSecurableMetadata, TmSettingCategoryMetadata, TmSettingKeyMetadata } from '../admin/tm-admin-metadata';
import type { TmNotificationTypeMetadata } from '../attention/tm-notification-metadata';
import type { TmEntityMetadata, TmEntitySummary, TmEnumMetadata } from '../entity/tm-entity-metadata';
import type { TmCodedReportMetadata } from '../reports/tm-coded-report';

// The client metadata (spec 0024 §3.1, §3.3, §3.4): the deployment facts the
// app needs before any request, which the Migrator emits at build time as
// hashed JSON chunks beside the bundle. This file owns the container types and
// the token through which the loaded Core chunk is read; each section's shape
// belongs to its domain. Every section is tenant-independent.

/** One language the distribution offers, and its formats. */
export interface TmLanguageMetadata {
  /** The language's code, such as `ar`. */
  readonly code: string;
  /** The language's name, in the language itself. */
  readonly nativeName: string;
  /** The language's text direction. */
  readonly dir: 'ltr' | 'rtl';
  /** The language's default region, such as `SA`. */
  readonly region: string;
  /** The regions offered with the language, in ordinal order. */
  readonly regions: readonly string[];
  /** The language's native digits, such as `arab`; `null` when they are Latin. */
  readonly nativeDigits: string | null;
}

/** Who the distribution is, as its build names it. */
export interface TmDistributionMetadata {
  /** The distribution's slug, such as `acme`. */
  readonly slug: string;
  /** The display name, used when no string pack localizes it. */
  readonly displayName: string;
  /** The copyright line, used when no string pack localizes it. */
  readonly copyright: string | null;
  /** The UI version: the composition's informational version, `X.Y.Z+<sha>`. */
  readonly version: string;
  /** The platform's version. */
  readonly platformVersion: string;
}

/** A feature the server composes, and its version. */
export interface TmFeatureMetadata {
  /** The feature's key. */
  readonly key: string;
  /** The feature assembly's version. */
  readonly version: string;
}

/** A notification channel the server registers. */
export interface TmNotificationChannelMetadata {
  /** The channel's key. */
  readonly key: string;
  /** Whether users receive the channel until they turn it off. */
  readonly defaultEnabled: boolean;
}

/**
 * The route words the server and the app reserve. One source on the server
 * writes them, and the router, the build and the hosting rules read them.
 */
export interface TmRoutingMetadata {
  /** The first segments of the server's tenantless surfaces, such as `api` and `bff`. */
  readonly serverFirstSegments: readonly string[];
  /** The segments the server serves under a tenant, such as `api` and `hub`. */
  readonly serverTenantSegments: readonly string[];
  /** The first segments of the app's tenantless pages: `tenants`, `signed-out`. */
  readonly spaFirstSegments: readonly string[];
  /** The segments of the app's core pages under a tenant: `settings`, `me`, `list`. */
  readonly spaTenantSegments: readonly string[];
}

/** The Core chunk: the metadata every route needs before its first render. */
export interface TmClientMetadata {
  /** The chunk's format; a change to it is a breaking change of the emit. */
  readonly formatVersion: 1;
  /** Who the distribution is. */
  readonly distribution: TmDistributionMetadata;
  /** The features the server composes, in composition order. */
  readonly features: readonly TmFeatureMetadata[];
  /** The languages the distribution offers; the first is the default. */
  readonly languages: readonly TmLanguageMetadata[];
  /** The calendars the distribution registers, such as `gc`, `uq` and `et`; the first is the default. */
  readonly calendars: readonly string[];
  /** A summary of every entity. */
  readonly entities: readonly TmEntitySummary[];
  /** Every registered securable. */
  readonly securables: readonly TmSecurableMetadata[];
  /** Every setting key. */
  readonly settingKeys: readonly TmSettingKeyMetadata[];
  /** Every settings tab, in tab order. */
  readonly settingCategories: readonly TmSettingCategoryMetadata[];
  /** Every notification type. */
  readonly notificationTypes: readonly TmNotificationTypeMetadata[];
  /** Every notification channel. */
  readonly notificationChannels: readonly TmNotificationChannelMetadata[];
  /** The names of the events the server pushes to clients. */
  readonly clientEvents: readonly string[];
  /** Every background job handler. */
  readonly jobHandlers: readonly TmJobHandlerMetadata[];
  /** The key of every dashboard widget type the server registers. */
  readonly widgetTypes: readonly string[];
  /** Every coded report the server declares. */
  readonly codedReports: readonly TmCodedReportMetadata[];
  /** The route words the server and the app reserve. */
  readonly routing: TmRoutingMetadata;
}

/** The Entities chunk: every entity's full metadata, loaded on entity routes. */
export interface TmEntitiesChunk {
  /** Every entity's metadata, ordered by name. */
  readonly entities: readonly TmEntityMetadata[];
  /** Every named enum, once. */
  readonly enums: readonly TmEnumMetadata[];
}

/**
 * The Contracts chunk: three facts about every route the server maps, keyed
 * by route template, such as `POST /{tenantId}/api/web/centers/query`.
 */
export interface TmContracts {
  /** Each route's contract fingerprint, which every call sends. */
  readonly fingerprints: Readonly<Record<string, string>>;
  /** The routes the server runs as long operations. */
  readonly long: readonly string[];
  /** The `POST` routes a retry may repeat safely. */
  readonly idempotent: readonly string[];
}

/**
 * Where the loaded Core chunk is kept. The boot sequence loads the chunk
 * before the first render and hands it over here; `TM_CLIENT_METADATA` reads
 * it.
 */
@Injectable({ providedIn: 'root' })
export class TmClientMetadataStore {
  /** The loaded Core chunk, or `null` before the boot sequence loads it. */
  #core: TmClientMetadata | null = null;

  /** Resolves {@link TmClientMetadataStore.loaded}. */
  #resolve!: (core: TmClientMetadata) => void;

  /**
   * Resolves with the Core chunk once the boot sequence has loaded it, for a
   * service that starts before then, such as the tab bus, whose channel
   * names carry the distribution's slug.
   */
  readonly loaded: Promise<TmClientMetadata> = new Promise((resolve) => (this.#resolve = resolve));

  /**
   * Keeps the loaded Core chunk.
   *
   * @param core - The chunk, as its loader returned it.
   */
  set(core: TmClientMetadata): void {
    this.#core = core;
    this.#resolve(core);
  }

  /**
   * The loaded Core chunk.
   *
   * @returns The chunk.
   * @throws Error when the chunk is not loaded yet.
   */
  get(): TmClientMetadata {
    if (this.#core === null) {
      throw new Error(
        'The client metadata is not loaded yet. Inject TM_CLIENT_METADATA only once the app has started, never in an initializer or while providers are created.',
      );
    }
    return this.#core;
  }
}

/**
 * The loaded Core chunk of the client metadata. The app loads it before its
 * first render, so components and services may inject it; injecting it
 * earlier, in an initializer or a provider factory, throws.
 *
 * @example
 * ```ts
 * const metadata = inject(TM_CLIENT_METADATA);
 * const offered = metadata.languages.map((language) => language.code);
 * ```
 */
export const TM_CLIENT_METADATA = new InjectionToken<TmClientMetadata>('TM_CLIENT_METADATA', {
  providedIn: 'root',
  factory: () => inject(TmClientMetadataStore).get(),
});
