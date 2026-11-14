// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { EnvironmentProviders, Provider } from '@angular/core';
import type { TmIconSource } from '@tellma/core-ui/icon';

import type { TmSettingsTabDeclaration } from '../admin/tm-settings-tab';
import type { TmWidgetTypeDeclaration } from '../dashboards/tm-widget';
import type { TmEntityDeclaration } from '../entity/tm-entity-declaration';
import type { TmNavEntry, TmNavSection } from '../nav/tm-nav-declarations';
import type { TmShellSlot } from '../nav/tm-shell-slot';
import type { TmCodedReportDeclaration } from '../reports/tm-coded-report';
import type { TmDeclarationKey, TmLazyComponent } from './tm-declaration-types';
import type { TmDeclarationPatch } from './tm-override';
import type { TmPageDeclaration } from './tm-page';

// `tmFeature` (spec 0024 §2.2): one feature's declarations. A distribution's
// app lists its features, packs first and the distribution last, and the
// merge rule unites them in that order after the platform's implicit `core`
// feature. A feature is data: nothing in it runs until a page needs it.

/** Everything one feature declares. Every member but `key` is optional. */
export interface TmFeatureDefinition {
  /** The feature's key, which names the server feature it belongs to: `gl`, `acme`. */
  readonly key: string;
  /** Launcher sections. */
  readonly sections?: readonly TmNavSection[];
  /** Launcher entries beyond the ones entities, pages and coded reports derive. */
  readonly entries?: readonly TmNavEntry[];
  /** Entity declarations. */
  readonly entities?: readonly TmEntityDeclaration[];
  /** Pages with no entity behind them. */
  readonly pages?: readonly TmPageDeclaration[];
  /** Patches of the coded reports the server declares. */
  readonly reports?: readonly TmCodedReportDeclaration[];
  /** How the widget types the server registers render. */
  readonly widgets?: readonly TmWidgetTypeDeclaration[];
  /** Replacements of settings tabs' generic bodies. */
  readonly settingsTabs?: readonly TmSettingsTabDeclaration[];
  /** App-bar slots; at most three across every feature. */
  readonly appBar?: readonly TmShellSlot[];
  /** Icons added to the icon registry, by name. */
  readonly icons?: Readonly<Record<string, TmIconSource>>;
  /** Patches of earlier features' declarations, member by member. */
  readonly overrides?: readonly TmDeclarationPatch[];
  /** Earlier features' client declarations to drop, by kind-qualified key. */
  readonly hide?: readonly TmDeclarationKey[];
  /** The tenant home page's body; the last feature that sets it wins. */
  readonly home?: TmLazyComponent;
  /** Angular providers the feature adds, in order, after the platform's. */
  readonly providers?: readonly (Provider | EnvironmentProviders)[];
}

/**
 * The brand that sets a {@link TmFeature} apart from a plain definition. It is
 * declared and never exported, so only {@link tmFeature} (or an explicit
 * cast) produces a feature.
 */
declare const tmFeatureBrand: unique symbol;

/** One feature: a frozen {@link TmFeatureDefinition}, as `tmFeature` returns it. */
export type TmFeature = TmFeatureDefinition & { readonly [tmFeatureBrand]: true };

/** A frozen copy of a list, or `undefined` for none. */
function frozenList<T>(list: readonly T[] | undefined): readonly T[] | undefined {
  return list === undefined ? undefined : Object.freeze([...list]);
}

/**
 * Declares one feature. The definition is copied and frozen, lists included,
 * so the declarations a feature holds cannot change after the call.
 *
 * @param definition - Everything the feature declares.
 * @returns The feature, for `provideTellma`'s `features`.
 *
 * @example
 * ```ts
 * export const acmeFeature = tmFeature({
 *   key: 'acme',
 *   sections: [{ id: 'sales', labelKey: 'Acme_Nav_Sales', icon: 'users', order: 20 }],
 *   entities: [customers, salesInvoices],
 *   pages: [creditReview],
 *   reports: [tmCodedReport('acme-credit-exposure', { section: 'sales' })],
 *   hide: ['entry:core.Role'],
 * });
 * ```
 */
export function tmFeature(definition: TmFeatureDefinition): TmFeature {
  // Copy every member, then replace each list and record with a frozen copy
  // of its own; members the definition leaves out stay absent.
  const feature: Record<string, unknown> = { ...definition };
  for (const member of [
    'sections',
    'entries',
    'entities',
    'pages',
    'reports',
    'widgets',
    'settingsTabs',
    'appBar',
    'overrides',
    'hide',
    'providers',
  ] as const) {
    if (definition[member] !== undefined) {
      feature[member] = frozenList<unknown>(definition[member]);
    }
  }
  if (definition.icons !== undefined) {
    feature['icons'] = Object.freeze({ ...definition.icons });
  }
  return Object.freeze(feature) as unknown as TmFeature;
}
