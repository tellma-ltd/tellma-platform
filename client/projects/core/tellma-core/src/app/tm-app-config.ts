// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmCalendar } from '@tellma/core-ui/l10n';

import type { TmFeature } from '../declarations/tm-feature';
import type { TmMetadataLoaders } from '../metadata/tm-metadata-loaders';

// The distribution's whole app configuration (spec 0024 §2.2): the emitted
// metadata loaders, the languages it ships client strings for, its features
// and any calendar of its own. `provideTellma` takes it, and the build checks
// it against the emitted metadata.

/** One level of a client string table: keys to messages or to nested levels. */
export type TmStringTree = { readonly [key: string]: string | TmStringTree };

/** One language's client string tables: the platform's and the component library's. */
export interface TmStringTable {
  /** The platform's strings. */
  readonly tm: TmStringTree;
  /** The component library's strings. */
  readonly tmUi: TmStringTree;
}

/**
 * One language the app ships client strings for. The server owns the rest of
 * the language's facts, such as its regions.
 */
export interface TmLanguageDefinition {
  /** The language's code, such as `ar`. */
  readonly code: string;
  /** The language's text direction. */
  readonly dir: 'ltr' | 'rtl';
  /** Loads the language's client string tables. */
  readonly strings: () => Promise<TmStringTable>;
}

/** A calendar the distribution registers on the server and the platform lacks. */
export interface TmCalendarDefinition {
  /** The calendar's code, as the server registers it. */
  readonly code: string;
  /** Loads the calendar. */
  readonly load: () => Promise<TmCalendar>;
}

/**
 * Everything `provideTellma` takes: one distribution's app configuration.
 *
 * @example
 * ```ts
 * const config: TmAppConfig = {
 *   metadata: tellmaMetadata,
 *   languages: [tmEnglish(), tmArabic()],
 *   features: [tmGlFeature(), acmeFeature],
 * };
 * ```
 */
export interface TmAppConfig {
  /** The client metadata's loaders, from the emitted `generated/tellma-metadata.ts`. */
  readonly metadata: TmMetadataLoaders;
  /** The languages the app ships client strings for; the first is the distribution's default. */
  readonly languages: readonly TmLanguageDefinition[];
  /** The features, packs first and the distribution last. */
  readonly features: readonly TmFeature[];
  /** Calendars the distribution registers on the server and the platform lacks. */
  readonly calendars?: readonly TmCalendarDefinition[];
}
