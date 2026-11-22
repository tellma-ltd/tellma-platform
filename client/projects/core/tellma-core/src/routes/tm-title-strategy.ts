// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, effect, inject, Injectable, signal, type Signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { type ActivatedRouteSnapshot, type RouterStateSnapshot, TitleStrategy } from '@angular/router';

import { TmPlatformText } from '../app/tm-platform-text';
import type { TmRouteData } from '../app/tm-route-data';
import { TmEntities } from '../entity/tm-entities';
import { TmRouteRules } from './tm-route-rules';
import type { TmRouteDataInternal } from './tm-routes';

// The document title (spec 0024 §4.4): `<page title> · {displayName}`. A
// route's title is its data's `titleKey`, a coded report's title, else its
// entity's label: plural on a search page or a list, singular on a details
// page, the create label on a create page. The title follows the language,
// since it is a signal.

/** The English fallbacks of the platform's own route titles. */
const FALLBACKS: Readonly<Record<string, string>> = {
  'tm.home.title': 'Home',
  'tm.admin.settings.title': 'Settings',
  'tm.admin.me.title': 'My settings',
  'tm.errors.notFound.title': 'Page not found',
};

/**
 * Sets the document title to the routed page's title and the distribution's
 * display name, `Centers · Acme`; the display name alone outside any page.
 */
@Injectable({ providedIn: 'root' })
export class TmTitleStrategy extends TitleStrategy {
  readonly #text = inject(TmPlatformText);
  readonly #entities = inject(TmEntities);
  readonly #rules = inject(TmRouteRules);

  /** The routed page's title signal. */
  readonly #page = signal<Signal<string> | null>(null);

  /** The routed page's title, without the display name; empty outside any page. */
  readonly pageTitle: Signal<string> = computed(() => this.#page()?.() ?? '');

  /** Creates the strategy, which writes the document title as the page's title changes. */
  constructor() {
    super();
    const title = inject(Title);
    effect(() => {
      const page = this.pageTitle();
      const name = this.#text.displayName();
      title.setTitle(page && name ? `${page} · ${name}` : page || name);
    });
  }

  /**
   * Takes the title of the deepest route that has one.
   *
   * @param snapshot - The router state after a navigation.
   */
  override updateTitle(snapshot: RouterStateSnapshot): void {
    let found: Signal<string> | null = null;
    for (let route: ActivatedRouteSnapshot | null = snapshot.root; route; route = route.firstChild) {
      found = this.#titleOf(route) ?? found;
    }
    this.#page.set(found);
  }

  /** A route's title, or `null` when it names none. */
  #titleOf(route: ActivatedRouteSnapshot): Signal<string> | null {
    const data = route.data as Partial<TmRouteData & TmRouteDataInternal>;
    if (data.titleKey) {
      return this.#text.text(data.titleKey, FALLBACKS[data.titleKey]);
    }
    const report = data.kind === 'report' ? this.#rules.report(route.paramMap.get('key') ?? '') : undefined;
    if (report) {
      return this.#text.text(report.titleKey);
    }
    const segment = route.paramMap.get('segment');
    const summary = data.ɵentity && segment !== null ? this.#entities.bySegment(segment) : undefined;
    if (!summary) {
      return null;
    }
    return data.ɵentity === 'create'
      ? this.#text.text(summary.newLabelKey, summary.labelFallback)
      : this.#entities.label(summary.name, data.ɵentity === 'search' || data.ɵentity === 'list');
  }
}
