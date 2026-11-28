// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

import type { TmSettingsTabDeclaration } from '../admin/tm-settings-tab';
import type { TmWidgetTypeDeclaration } from '../dashboards/tm-widget';
import type { TmLazyComponent } from '../declarations/tm-declaration-types';
import type { TmPageDeclaration } from '../declarations/tm-page';
import type { TmEntityDeclaration } from '../entity/tm-entity-declaration';
import type { TmNavEntry, TmNavSection } from '../nav/tm-nav-declarations';
import type { TmShellSlot } from '../nav/tm-shell-slot';
import type { TmCodedReportMetadata, TmCodedReportPatch } from '../reports/tm-coded-report';

// The merged declarations (spec 0024 §2.2, §2.3): every feature's
// declarations united by the merge rule, overrides and `hide` applied. Shells,
// route hosts and the navigation registry read declarations only through this
// token, never from the features themselves.

/** Every declaration of every feature, merged and with `hide` applied. */
export interface TmAppDeclarations {
  /** The declared entities, by name, in feature order. */
  readonly entities: ReadonlyMap<string, TmEntityDeclaration>;
  /** The pages, in feature order. */
  readonly pages: readonly TmPageDeclaration[];
  /** Every coded report the server declares, each with its features' patches merged in. */
  readonly reports: readonly (TmCodedReportMetadata & TmCodedReportPatch)[];
  /** The widget type declarations, in feature order. */
  readonly widgets: readonly TmWidgetTypeDeclaration[];
  /** The launcher sections, in feature order. */
  readonly sections: readonly TmNavSection[];
  /** The launcher entries features declare beyond the derived ones, in feature order. */
  readonly entries: readonly TmNavEntry[];
  /**
   * The ids of the launcher entries `hide` dropped, the entries of hidden
   * sections among them. It covers the entries entities, pages and coded
   * reports derive, whose declarations stay; their routes stay too.
   */
  readonly hiddenEntries: ReadonlySet<string>;
  /** The settings tab replacements, in feature order. */
  readonly settingsTabs: readonly TmSettingsTabDeclaration[];
  /** The app-bar slots, in feature order; at most three beside the platform's reserved ones. */
  readonly appBar: readonly TmShellSlot[];
  /** The feature keys in merge order, `core` first. */
  readonly features: readonly string[];
  /** The tenant home page's body from the last feature that sets one, or `null` for the platform's. */
  readonly home: TmLazyComponent | null;
}

/**
 * The app's merged declarations. Provided by `provideTellma`, and built once
 * from the configured features and the client metadata's coded reports.
 *
 * @example
 * ```ts
 * const declarations = inject(TM_APP_DECLARATIONS);
 * const customers = declarations.entities.get('acme.Customer');
 * ```
 */
export const TM_APP_DECLARATIONS = new InjectionToken<TmAppDeclarations>('TM_APP_DECLARATIONS');
