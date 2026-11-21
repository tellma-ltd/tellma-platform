// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmMarkDeclaration } from '../declarations/tm-declaration-registry';
import type { TmLazyComponent, TmSecurableRef } from '../declarations/tm-declaration-types';
import type { TmJson } from '../json/tm-json';
import type { TmNavContext } from '../nav/tm-nav-declarations';

// Coded reports (spec 0033 §11.4, spec 0034 §2.2). The server declares every
// coded report and publishes it in the Core chunk's `codedReports` section:
// its key, title, kind, securable and parameters. A feature's
// `tmCodedReport` only patches one entry with its launcher placement and, for
// a report the server cannot run itself, the client code that runs or renders
// it. The merge rule unites every entry with its patches.

/** The kind of a report's output. */
export type TmReportKind = 'List' | 'Summary' | 'Pivot' | 'Chart';

/** The type of a report parameter's value. */
export type TmReportParameterType = 'Numeric' | 'String' | 'Bool' | 'Date' | 'DateTime' | 'DateTimeOffset' | 'Guid';

/** How a report value displays, which also picks a parameter's control. */
export type TmReportDisplay =
  | 'number'
  | 'integer'
  | 'percent'
  | 'monthName'
  | 'weekdayName'
  | 'text'
  | 'enum'
  | 'date'
  | 'month'
  | 'quarter'
  | 'year'
  | 'datetime'
  | 'instant'
  | 'bool';

/** One value's label under the `enum` display. */
export interface TmReportChoice {
  /** The value the label applies to. */
  readonly value: string | number | boolean;
  /** The label in the first content language. */
  readonly label?: string;
  /** The label in the second content language. */
  readonly label2?: string;
  /** The label in the third content language. */
  readonly label3?: string;
  /** A string key, in place of the labels. */
  readonly labelKey?: string;
}

/** How a report value's text is written. */
export interface TmReportFormat {
  /** The digits after the point. */
  readonly decimals?: number;
  /** Whether digits are grouped. */
  readonly grouping?: boolean;
  /** How long a date's text is. */
  readonly dateStyle?: 'numeric' | 'medium' | 'long';
  /** The calendar of a date's text. */
  readonly calendar?: 'gc' | 'uq' | 'et';
  /** The named enum whose labels an `enum` display shows. */
  readonly enum?: string;
  /** Inline labels an `enum` display shows. */
  readonly choices?: readonly TmReportChoice[];
}

/** One parameter of a report, as its parameter bar renders it. */
export interface TmReportParameter {
  /** The parameter's key, such as `AsOf`. */
  readonly key: string;
  /** The label in the first content language. */
  readonly label?: string;
  /** The label in the second content language. */
  readonly label2?: string;
  /** The label in the third content language. */
  readonly label3?: string;
  /** A string key, in place of the labels. */
  readonly labelKey?: string;
  /** Whether a run needs a value. */
  readonly required?: boolean;
  /** The default value, as a stored or inline report's expression. */
  readonly default?: string;
  /** Set on a coded report's parameter when the server applies a default. */
  readonly hasDefault?: boolean;
  /** The value's type. */
  readonly type: TmReportParameterType;
  /** The entity whose id the value holds. */
  readonly entity?: string;
  /** Set on a coded report's integer parameter. */
  readonly integer?: true;
  /** How the value displays. */
  readonly display?: TmReportDisplay;
  /** How the value's text is written. */
  readonly format?: TmReportFormat;
}

/** A report run's arguments, keyed by parameter; `null` omits one. */
export type TmReportArgs = Readonly<Record<string, TmJson>>;

/**
 * One entry of the Core chunk's `codedReports` section: everything the server
 * declares about a coded report.
 */
export interface TmCodedReportMetadata {
  /** The report's key, such as `acme-credit-exposure`. */
  readonly key: string;
  /** The key of the feature that declares it. */
  readonly feature: string;
  /** The report action's route; absent when client code runs the report. */
  readonly route?: string;
  /** The permission that gates the report's route and launcher entry. */
  readonly securable: TmSecurableRef;
  /** The kind of the report's output. */
  readonly kind: TmReportKind;
  /** The string key of the report's title. */
  readonly titleKey: string;
  /** The string key of the report's description. */
  readonly descriptionKey?: string;
  /** Who writes the report's Excel export: the platform, the report's own action, or nobody. */
  readonly export: 'Platform' | 'Custom' | 'None';
  /** The report's parameters, in the order of the parameter bar. */
  readonly parameters: readonly TmReportParameter[];
}

/**
 * The client code that fetches a coded report the server has no route for.
 * Its first argument is the app's HTTP layer, through which it reads.
 */
export type TmCodedReportRun = (
  api: never,
  args: TmReportArgs,
  options: { readonly signal: AbortSignal; readonly priority?: 'low' },
) => Promise<unknown>;

/**
 * What a feature may set on a coded report: its launcher placement and its
 * client code. The title, kind, securable and parameters are the server's.
 */
export interface TmCodedReportPatch {
  /** The launcher section of the report's entry; absent, the entry is found by search alone. */
  readonly section?: string;
  /** The entry's place within its section. */
  readonly order?: number;
  /** A registered icon name for the entry. */
  readonly icon?: string;
  /** The string key of the entry's comma-separated search synonyms. */
  readonly synonymsKey?: string;
  /** Hides the launcher entry when it returns `false`. */
  readonly visibleWhen?: (context: TmNavContext) => boolean;
  /** Fetches the report's result, for a report without a server route. */
  readonly run?: TmCodedReportRun;
  /** Replaces the rendered output; for a report without a server route it also fetches the data. */
  readonly component?: TmLazyComponent;
}

/** A feature's patch of one coded report, keyed by the report's key. */
export interface TmCodedReportDeclaration extends TmCodedReportPatch {
  /** The key of the `codedReports` entry the patch applies to. */
  readonly key: string;
}

/**
 * Patches the coded report the server declares under `key`. Listed in a
 * feature's `reports` or `overrides`, it merges into the report in feature
 * order, so a pack and a distribution may both patch one report.
 *
 * @param key - The report's key, as the server declares it.
 * @param patch - The members to set.
 * @returns The frozen patch.
 *
 * @example
 * ```ts
 * export const creditExposureReport = tmCodedReport('acme-credit-exposure', { section: 'sales' });
 * ```
 */
export function tmCodedReport(key: string, patch: TmCodedReportPatch): TmCodedReportDeclaration {
  return tmMarkDeclaration(Object.freeze({ ...patch, key }), 'report');
}
