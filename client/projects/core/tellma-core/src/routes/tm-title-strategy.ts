// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, effect, inject, Injectable, signal, type Signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { type ActivatedRouteSnapshot, type RouterStateSnapshot, TitleStrategy } from '@angular/router';

import type { TmRouteData } from '../app/tm-route-data';
import { TmEntities } from '../entity/tm-entities';
import { TmLocalization } from '../l10n/tm-localization';
import { TmRouteRules } from './tm-route-rules';
import type { TmRouteDataInternal } from './tm-routes';

// The document title (spec 0024 §4.4): `<page title> · {displayName}`. A
// route's title is its data's `titleKey`, a coded report's title, else its
// entity's label: plural on a search page or a list, singular on a details
// page, the create label on a create page. A page may name itself instead,
// as a details page names its record, while its own title is not empty. The
// title follows the language, since it is a signal.

/**
 * Sets the document title to the routed page's title and the distribution's
 * display name, `Centers · Acme`; the display name alone outside any page.
 */
@Injectable({ providedIn: 'root' })
export class TmTitleStrategy extends TitleStrategy {
  readonly #l10n = inject(TmLocalization);
  readonly #entities = inject(TmEntities);
  readonly #rules = inject(TmRouteRules);

  /** The routed page's title signal. */
  readonly #page = signal<Signal<string> | null>(null);

  /** The title the rendered page gives itself, and the page that gave it. */
  readonly #own = signal<{ readonly owner: object; readonly title: Signal<string> } | null>(null);

  /** The routed page's title, without the display name: the page's own while not empty, else its route's; empty outside any page. */
  readonly pageTitle: Signal<string> = computed(() => this.#own()?.title() || (this.#page()?.() ?? ''));

  /** Creates the strategy, which writes the document title as the page's title changes. */
  constructor() {
    super();
    const title = inject(Title);
    effect(() => {
      const page = this.pageTitle();
      const name = this.#l10n.displayName();
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
      found = this.ɵtitleOf(route) ?? found;
    }
    this.#page.set(found);
  }

  /**
   * Gives the document the rendered page's own title, or takes it back.
   * Internal: a details page names its record this way.
   *
   * @param owner - The page that names itself; taking the title back only clears that page's.
   * @param title - The title, or `null` to take the page's title back.
   */
  ɵsetPageTitle(owner: object, title: Signal<string> | null): void {
    if (title !== null) {
      this.#own.set({ owner, title });
    } else if (this.#own()?.owner === owner) {
      this.#own.set(null);
    }
  }

  /**
   * A route's title, or `null` when it names none. Internal: the route host
   * titles its page's default context with it.
   *
   * @param route - The route.
   * @returns The title.
   */
  ɵtitleOf(route: ActivatedRouteSnapshot): Signal<string> | null {
    const data = route.data as Partial<TmRouteData & TmRouteDataInternal>;
    if (data.titleKey) {
      return this.#l10n.ɵtext(data.titleKey);
    }
    const report = data.kind === 'report' ? this.#rules.report(route.paramMap.get('key') ?? '') : undefined;
    if (report) {
      return this.#l10n.ɵtext(report.titleKey);
    }
    const segment = route.paramMap.get('segment');
    const summary = data.ɵentity && segment !== null ? this.#entities.bySegment(segment) : undefined;
    if (!summary) {
      return null;
    }
    return data.ɵentity === 'create'
      ? this.#l10n.ɵtext(summary.newLabelKey, undefined, summary.labelFallback)
      : this.#entities.label(summary.name, data.ɵentity === 'search' || data.ɵentity === 'list');
  }
}
