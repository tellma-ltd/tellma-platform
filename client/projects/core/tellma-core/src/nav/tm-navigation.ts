// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, Injector, type Signal, untracked } from '@angular/core';

import { TM_APP_DECLARATIONS, type TmAppDeclarations } from '../app/tm-app-declarations';
import type { TmSecurableRef } from '../declarations/tm-declaration-types';
import { TmEntities } from '../entity/tm-entities';
import type { TmEntityDeclarationInput } from '../entity/tm-entity-declaration';
import { TmPermissions } from '../entity/tm-permissions';
import { TmLocalization } from '../l10n/tm-localization';
import { TmClientMetadataStore, type TmClientMetadata } from '../metadata/tm-client-metadata';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import { TmUrls, type TmRouteMatch } from '../urls/tm-urls';
import { TM_CORE_ENTRIES, TM_SETTINGS_ENTRY } from './tm-core-nav';
import type { TmNavContext, TmNavEntry, TmNavSection } from './tm-nav-declarations';

// The navigation registry (spec 0027 §3). It derives the launcher's entries
// from the merged declarations, after `hide`: every stack with a query
// operation and a segment, every page without a path parameter, every coded
// report the server declares, and every feature entry, each resolved for the
// routed tenant. An entry shows when its permission rule passes and its
// `visibleWhen`, if any, returns true; with the distribution document absent,
// an entry with `visibleWhen` is hidden. The entries recompute only when the
// access, the language, the tenant or the distribution document changes.
// `resolve` judges a stored URL (a pin's or a recent's) by the same rules
// (spec 0027 §8.3).

/** A visible launcher entry, resolved for the routed tenant. */
export interface TmNavItem extends TmNavEntry {
  /** What the entry derives from: an entity, a page, a coded report, or a feature's own entry. */
  readonly kind: 'entity' | 'page' | 'report' | 'entry';
  /** The entry's URL in the routed tenant, such as `/17/customers`. */
  readonly href: string;
  /** The entity, on an entity's entry, such as `acme.Customer`. */
  readonly entity?: string;
  /** The entity's create page, such as `/17/customers/new`, when the user may create one there; else `null`. */
  readonly createHref: string | null;
  /** The entry's icon: its own, else its kind's. */
  readonly icon: string;
  /** The entry's place within its section: its own, else 100. */
  readonly order: number;
  /** The string key of its synonyms: its own, else the label key with `_Synonyms`. */
  readonly synonymsKey: string;
  /** The entry's label in the active language. */
  readonly label: string;
}

/** A launcher section with its visible entries, in order. */
export interface TmNavSectionView extends TmNavSection {
  /** The section's label in the active language. */
  readonly label: string;
  /** The section's visible entries, ordered by `order`, then by label. */
  readonly entries: readonly TmNavItem[];
}

/** A derived or declared entry before the permission rule and `visibleWhen` judge it. */
interface TmCandidate {
  /** The entry. */
  readonly item: TmNavItem;
  /** Whether its permission rule passes. */
  readonly passes: boolean;
  /** Whether its `visibleWhen`, if any, shows it. */
  readonly shown: boolean;
}

/** What one recomputation derives: the sections, the visible entries, and the paths `visibleWhen` hides. */
interface TmNavState {
  /** The sections with a visible entry, in order. */
  readonly sections: readonly TmNavSectionView[];
  /** Every visible entry: by section order, then the search-only ones. */
  readonly items: readonly TmNavItem[];
  /** The visible entries by URL; the first wins. */
  readonly byHref: ReadonlyMap<string, TmNavItem>;
  /** The URLs of the entries that `visibleWhen` hides. */
  readonly hidden: ReadonlySet<string>;
}

/** The empty state: outside a tenant, or before the client metadata loads. */
const EMPTY: TmNavState = { sections: [], items: [], byHref: new Map(), hidden: new Set() };

/** An entry's order when it declares none. */
const DEFAULT_ORDER = 100;

/** The core entry whose `requires` gates each core route, by the route's template. */
const CORE_ROUTES: Readonly<Record<string, string>> = {
  '/:t/settings': TM_SETTINGS_ENTRY,
  '/:t/settings/:category': TM_SETTINGS_ENTRY,
  '/:t/me': 'core.me',
  '/:t/me/profile': 'core.me.profile',
  '/:t/me/region': 'core.me.region',
  '/:t/me/display': 'core.me.display',
  '/:t/me/notifications': 'core.me.notifications',
};

/** The entities whose records' pages report a refusal themselves: stored reports and dashboards. */
const SELF_GUARDED = new Set(['core.Report', 'core.Dashboard']);

/**
 * The launcher's entries for the routed tenant: the permission-filtered,
 * ordered sections and every visible entry, derived from the merged
 * declarations, and the state of a stored URL. A root service; the
 * launcher, the pinned bar and recently visited pages read it, and so may
 * any page.
 *
 * @example
 * ```ts
 * const navigation = inject(TmNavigation);
 * const names = computed(() => navigation.sections().map((section) => section.label));
 * navigation.resolve('/17/customers?f.CustomerType=in:Corporate'); // 'ok', 'no-access' or 'unavailable'
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmNavigation {
  readonly #injector = inject(Injector);
  readonly #urls = inject(TmUrls);
  readonly #permissions = inject(TmPermissions);
  readonly #entities = inject(TmEntities);
  readonly #l10n = inject(TmLocalization);
  readonly #metadata = inject(TmClientMetadataStore);

  /** The labels asked for so far, by key and fallback, so each key resolves through one signal. */
  readonly #labels = new Map<string, Signal<string>>();

  /** The `visibleWhen` predicates that threw, reported once each. */
  readonly #threw = new Set<string>();

  /** The routed tenant's distribution context, or `null` while the distribution document is absent. */
  readonly #context = computed<TmNavContext | null>(() => {
    const distribution = this.#injector.get(TmLaunchCache).distribution();
    return distribution === undefined ? null : { distribution };
  });

  /** The merged declarations, once the client metadata has loaded; `null` before then or without an app. */
  readonly #declarations = computed<TmAppDeclarations | null>(() =>
    // TM_APP_DECLARATIONS needs the loaded Core chunk: injected before it, the token would fail for good.
    this.#metadata.current() === null ? null : this.#injector.get(TM_APP_DECLARATIONS, null),
  );

  /** What the declarations derive for the routed tenant, under the user's access and in the active language. */
  readonly #state = computed<TmNavState>(() => {
    const t = this.#urls.tenantId();
    const declarations = this.#declarations();
    const metadata = this.#metadata.current();
    if (t === null || declarations === null || metadata === null) {
      return EMPTY;
    }
    const candidates = this.#candidates(t, declarations, metadata);

    // --- Visible entries, and the URLs `visibleWhen` hides (spec 0027 §8.3) ---
    const visible = candidates.filter((candidate) => candidate.passes && candidate.shown).map((candidate) => candidate.item);
    const hidden = new Set(candidates.filter((candidate) => !candidate.shown).map((candidate) => candidate.item.href));

    // --- Order: by `order`, then by the localized label, then by id, so ties never depend on feature order ---
    const collator = new Intl.Collator(this.#l10n.language());
    const byOrder = <T extends { readonly id: string; readonly order: number; readonly label: string }>(a: T, b: T): number =>
      a.order - b.order || collator.compare(a.label, b.label) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);

    // --- Sections with at least one visible entry; an entry naming no shown section is search-only ---
    const sections = declarations.sections
      .map((section): TmNavSectionView => ({
        ...section,
        label: this.#label(section.labelKey)(),
        entries: visible.filter((item) => item.section === section.id).sort(byOrder),
      }))
      .filter((section) => section.entries.length > 0)
      .sort(byOrder);
    const shownSections = new Set(sections.map((section) => section.id));
    const items = [
      ...sections.flatMap((section) => section.entries),
      ...visible.filter((item) => item.section === undefined || !shownSections.has(item.section)).sort(byOrder),
    ];
    const byHref = new Map<string, TmNavItem>();
    for (const item of items) {
      if (!byHref.has(item.href)) {
        byHref.set(item.href, item);
      }
    }
    return { sections, items, byHref, hidden };
  });

  /** The sections with at least one visible entry, ordered by `order`, then by label. */
  readonly sections: Signal<readonly TmNavSectionView[]> = computed(() => this.#state().sections);

  /** Every visible entry, the search-only ones included: by section order, then the search-only ones, each by `order` and label. */
  readonly items: Signal<readonly TmNavItem[]> = computed(() => this.#state().items);

  /**
   * The visible entry whose URL is exactly `href`, as pins and recently
   * visited pages name themselves after it. A URL with a query finds only
   * an entry whose own URL has that query.
   *
   * @param href - The URL, such as `/17/customers`.
   * @returns The entry, or `undefined`.
   */
  byHref(href: string): TmNavItem | undefined {
    return this.#state().byHref.get(href);
  }

  /**
   * A stored URL's state, such as a pin's or a recently visited page's:
   * `unavailable` when no route matches it, or when `visibleWhen` hides the
   * declaration that owns its route or the entry whose URL is its path;
   * `no-access` when the route's permission check fails; `ok` otherwise.
   * Stored reports and dashboards are `ok`: their pages report a refusal.
   * Read inside a `computed`, it follows the user's access.
   *
   * @param href - The URL in the routed tenant, such as `/17/customers?q=jed`.
   * @returns The state.
   */
  resolve(href: string): 'ok' | 'no-access' | 'unavailable' {
    const match = this.#urls.describe(href);
    const state = this.#state();
    if (match?.tenantId === undefined || state.hidden.has(href.split(/[?#]/, 1)[0]!) || !this.#ownerShown(match)) {
      return 'unavailable';
    }
    return this.#allowed(match) ? 'ok' : 'no-access';
  }

  /**
   * Every entry the declarations derive for a tenant, each judged by its
   * permission rule and its `visibleWhen`.
   *
   * @param t - The tenant.
   * @param declarations - The merged declarations.
   * @param metadata - The Core chunk.
   * @returns The candidates, in declaration order.
   */
  #candidates(t: number, declarations: TmAppDeclarations, metadata: TmClientMetadata): TmCandidate[] {
    const hidden = declarations.hiddenEntries;
    const candidates: TmCandidate[] = [];
    const add = (
      kind: TmNavItem['kind'],
      entry: TmNavEntry,
      label: Signal<string>,
      icon: string,
      passes: boolean,
      extra: { readonly entity?: string; readonly createHref?: string | null } = {},
    ): void => {
      const href = entry.url(t);
      candidates.push({
        item: {
          ...entry,
          kind,
          href,
          createHref: null,
          ...extra,
          icon: entry.icon ?? icon,
          order: entry.order ?? DEFAULT_ORDER,
          synonymsKey: entry.synonymsKey ?? `${entry.labelKey}_Synonyms`,
          label: label(),
        },
        passes,
        shown: this.#shown(entry.id, entry.visibleWhen),
      });
    };

    // --- Stacks with a segment and a query operation, whose search page the entry opens ---
    for (const [name, declaration] of declarations.entities) {
      const summary = this.#entities.summary(name);
      // A child entity's segment routes to drill-through lists alone, never to a search page.
      if (summary?.kind !== 'stack' || summary.segment == null || !summary.operations.includes('query') || hidden.has(name)) {
        continue;
      }
      const members = declaration as TmEntityDeclarationInput;
      const url = (tenant: number): string => `/${tenant}/${summary.segment}`;
      const create = this.#permissions.allows(name, 'save')() && this.#entities.ɵhasDetailsPage(summary);
      add(
        'entity',
        { ...pick(members), id: name, labelKey: summary.pluralLabelKey, url },
        this.#label(summary.pluralLabelKey, summary.labelFallback),
        'file-text',
        this.#permissions.allows(name, 'query')(),
        { entity: name, createHref: create ? `${url(t)}/new` : null },
      );
    }

    // --- Pages without a path parameter: an entry under the page's path ---
    for (const page of declarations.pages) {
      if (page.path.includes(':') || hidden.has(page.path)) {
        continue;
      }
      add(
        'page',
        { ...pick(page), id: page.path, labelKey: page.titleKey, url: (tenant) => `/${tenant}/${page.path}` },
        this.#label(page.titleKey),
        'square-dashed',
        page.securable === undefined || this.#passes([page.securable]),
      );
    }

    // --- Coded reports, each with its features' patches merged in ---
    for (const report of declarations.reports) {
      if (hidden.has(report.key)) {
        continue;
      }
      add(
        'report',
        { ...pick(report), id: report.key, labelKey: report.titleKey, url: (tenant) => `/${tenant}/reports/${report.key}` },
        this.#label(report.titleKey),
        'chart-column',
        this.#passes([report.securable]),
      );
    }

    // --- Feature entries, `hide` already applied by the merge ---
    for (const entry of declarations.entries) {
      add('entry', entry, this.#label(entry.labelKey), 'link', this.#passes(this.#requires(entry, metadata)));
    }
    return candidates;
  }

  /**
   * Whether an entry's `visibleWhen` shows it: an entry without one always
   * shows, one with it never while the distribution document is absent. A
   * predicate that throws hides its entry and is reported once.
   *
   * @param id - The entry's id, for the report.
   * @param visibleWhen - The predicate.
   * @returns Whether it shows.
   */
  #shown(id: string, visibleWhen: ((context: TmNavContext) => boolean) | undefined): boolean {
    if (visibleWhen === undefined) {
      return true;
    }
    const context = this.#context();
    try {
      return context !== null && visibleWhen(context);
    } catch (error) {
      if (!this.#threw.has(id)) {
        this.#threw.add(id);
        // The entries recompute inside a `computed`, where the report's own signal writes are refused.
        untracked(() => this.#injector.get(TmTelemetry).error(error, { where: 'tm.nav.visible-when', entry: id }));
      }
      return false;
    }
  }

  /**
   * Whether `visibleWhen` shows the declaration that owns a matched route:
   * the entity of an entity route, the page of a page route, or the coded
   * report of a report route.
   *
   * @param match - The route match.
   * @returns Whether it shows; a route with no such owner always does.
   */
  #ownerShown(match: TmRouteMatch): boolean {
    const owner =
      match.entity !== undefined
        ? (this.#entities.declaration(match.entity) as TmEntityDeclarationInput | undefined)
        : match.key !== undefined
          ? this.#declarations()?.reports.find((report) => report.key === match.key)
          : match.page;
    return this.#shown(match.entity ?? match.key ?? match.page?.path ?? '', owner?.visibleWhen);
  }

  /**
   * Whether the user passes a matched route's permission check: the
   * entity's `query` for a search, list or details route, its `save` for a
   * create route, the securable of a page or coded report, and the core
   * entry's `requires` for a core route. A stored report's or
   * dashboard's details route always passes.
   *
   * @param match - The route match.
   * @returns Whether it passes.
   */
  #allowed(match: TmRouteMatch): boolean {
    switch (match.kind) {
      case 'search':
      case 'list':
        return this.#permissions.allows(match.entity!, 'query')();
      case 'details':
        return SELF_GUARDED.has(match.entity!) || this.#permissions.allows(match.entity!, 'query')();
      case 'create':
        return this.#permissions.allows(match.entity!, 'save')();
      case 'page':
      case 'report':
        return match.securable === undefined || this.#passes([match.securable]);
      case 'core': {
        // A hidden core entry still gates its route, by the core feature's own declaration.
        const id = CORE_ROUTES[match.template];
        const entry = this.#declarations()?.entries.find((candidate) => candidate.id === id) ?? TM_CORE_ENTRIES.find((candidate) => candidate.id === id);
        const metadata = this.#metadata.current();
        return entry === undefined || metadata === null || this.#passes(this.#requires(entry, metadata));
      }
      default:
        return true;
    }
  }

  /**
   * The permissions any one of which shows an entry: its `requires`, as a
   * list; for the core Settings entry without its own, `Read` on every
   * settings category the client metadata lists.
   *
   * @param entry - The entry.
   * @param metadata - The Core chunk.
   * @returns The permissions; empty when the entry needs none.
   */
  #requires(entry: TmNavEntry, metadata: TmClientMetadata): readonly TmSecurableRef[] {
    const requires = entry.requires;
    if (requires === undefined) {
      return entry.id === TM_SETTINGS_ENTRY ? metadata.settingCategories.map((category): TmSecurableRef => [category.resource, 'Read']) : [];
    }
    // A single permission is a pair of strings; a list holds pairs.
    return typeof requires[0] === 'string' ? [requires as TmSecurableRef] : (requires as readonly TmSecurableRef[]);
  }

  /**
   * Whether any one of the permissions is granted; none needed passes.
   *
   * @param requires - The permissions.
   * @returns Whether it passes.
   */
  #passes(requires: readonly TmSecurableRef[]): boolean {
    return requires.length === 0 || requires.some(([resource, action]) => this.#permissions.can(resource, action)());
  }

  /**
   * A label in the active language, through one signal per key.
   *
   * @param key - The string key.
   * @param fallback - The text when no loaded strings hold the key.
   * @returns The label's signal.
   */
  #label(key: string, fallback?: string): Signal<string> {
    const memo = `${key}\n${fallback ?? ''}`;
    let label = this.#labels.get(memo);
    if (label === undefined) {
      this.#labels.set(memo, (label = this.#l10n.ɵtext(key, undefined, fallback)));
    }
    return label;
  }
}

/**
 * The launcher members a declaration sets flat beside its others, and
 * nothing else, so an entry never carries a page's component or an
 * entity's columns.
 *
 * @param declaration - The entity, page or coded report declaration.
 * @returns Its launcher members.
 */
function pick(declaration: Pick<TmNavEntry, 'section' | 'order' | 'icon' | 'synonymsKey' | 'visibleWhen'>): Partial<TmNavEntry> {
  const { section, order, icon, synonymsKey, visibleWhen } = declaration;
  return { section, order, icon, synonymsKey, visibleWhen };
}
