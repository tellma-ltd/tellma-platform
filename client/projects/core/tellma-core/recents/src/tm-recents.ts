// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  DestroyRef,
  effect,
  inject,
  Injectable,
  InjectionToken,
  signal,
  untracked,
  type Signal,
} from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import {
  TM_NOW,
  TmCacheRefresher,
  type TmPageKind,
  TmPageContexts,
  TmPreferences,
  tmPreferenceKey,
  TmUrls,
  ɵTM_API_HOST,
} from '@tellma/core';

// Recently visited pages (spec 0025 §13): the pages the user last visited in
// the routed tenant, at most ten, kept in the write-behind preference
// `nav.recent` so they follow the user to every tab and browser. A visit is a
// page whose context (`TmPageContexts.current`) is of a listed kind, not the
// drill-through list, and stays current for 2 s while the document is
// visible. The launcher entry of a page names and draws it; access filters
// the list on read, so a page whose access returns comes back. A tab keeps
// its own visits while they are among the ten and merges them back into a
// list that lacks them, so a tab that started from an older stored list
// cannot erase them.

/** A recently visited page, as the launcher and the home page list it. */
export interface TmRecentPage {
  /** The page's path, such as `/17/centers`; no query, since the page restores its own state. */
  readonly url: string;
  /** The page's family. */
  readonly kind: TmPageKind;
  /** The launcher entry's label in the active language, else the title as displayed at the visit. */
  readonly title: Signal<string>;
  /** The launcher entry's icon, else `null`: the consumer picks one by kind. */
  readonly icon: string | null;
  /** When the user last visited it, in epoch milliseconds, to the second. */
  readonly visitedAt: number;
}

/** A page's launcher entry, as recents name and draw it. Internal. */
export interface TmRecentsEntry {
  /** The entry's label in the active language. */
  readonly label: Signal<string>;
  /** The entry's icon, or `null`. */
  readonly icon: string | null;
}

/** The launcher's answers about a stored URL. Internal. */
export interface TmRecentsNavigation {
  /**
   * The launcher entry whose path is the URL's.
   *
   * @param href - The URL, such as `/17/centers`.
   * @returns The entry, or `undefined`.
   */
  entry(href: string): TmRecentsEntry | undefined;
  /**
   * Whether a stored URL still opens for the user; tracked, so the list
   * follows the user's access.
   *
   * @param href - The URL.
   * @returns `ok`, `no-access` or `unavailable`.
   */
  resolve(href: string): 'ok' | 'no-access' | 'unavailable';
}

/**
 * The launcher's answers about stored URLs: until the launcher lands, no
 * entry, and a URL that no route reaches is unavailable. Internal.
 */
export const TM_RECENTS_NAVIGATION = new InjectionToken<TmRecentsNavigation>('TM_RECENTS_NAVIGATION', {
  providedIn: 'root',
  factory: () => {
    const urls = inject(TmUrls);
    return { entry: () => undefined, resolve: (href) => (urls.describe(href) === null ? 'unavailable' : 'ok') };
  },
});

/** One stored visit: the tenant-relative path, the kind, the time in epoch seconds and the title. */
interface TmStoredVisit {
  /** The path after the tenant, such as `reports/42`. */
  readonly p: string;
  /** The page's family. */
  readonly k: TmPageKind;
  /** When, in epoch seconds. */
  readonly t: number;
  /** The title as displayed at the visit. */
  readonly n: string;
}

/** The kinds a visit counts for. */
const KINDS: readonly TmPageKind[] = ['search', 'report', 'dashboard', 'settings', 'access', 'custom'];

/** The drill-through list's route template: its visits never count. */
const LIST = '/:t/list/:segment';

/** How many pages the list keeps. */
const CAPACITY = 10;

/** How long a page stays current while visible before it counts. */
const DWELL_MS = 2_000;

/** Whether a stored value is a list of visits. */
function isVisits(value: unknown): value is readonly TmStoredVisit[] {
  return (
    Array.isArray(value) &&
    value.every((visit: Partial<TmStoredVisit> | null) =>
      typeof visit === 'object' &&
      visit !== null &&
      typeof visit.p === 'string' &&
      KINDS.includes(visit.k!) &&
      typeof visit.t === 'number' &&
      typeof visit.n === 'string',
    )
  );
}

/** The platform's `nav.recent` key. */
const NAV_RECENT = tmPreferenceKey<readonly TmStoredVisit[]>('nav.recent', 'write-behind', [], isVisits);

/** A router URL's path, without its query or fragment. */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0]!;
}

/**
 * The newest visits of the lists, by path: a path keeps its latest visit,
 * the first list's on a tie, newest first and at most ten.
 */
function merge(...lists: readonly (readonly TmStoredVisit[])[]): TmStoredVisit[] {
  const latest = new Map<string, TmStoredVisit>();
  for (const visit of lists.flat()) {
    const held = latest.get(visit.p);
    if (held === undefined || visit.t > held.t) {
      latest.set(visit.p, visit);
    }
  }
  return [...latest.values()].sort((a, b) => b.t - a.t).slice(0, CAPACITY);
}

/** Whether two lists hold the same visits in the same order. */
function sameVisits(a: readonly TmStoredVisit[], b: readonly TmStoredVisit[]): boolean {
  return a.length === b.length && a.every((visit, i) => visit.p === b[i]!.p && visit.t === b[i]!.t);
}

/** The tenant-relative path of a router URL: `/17/reports/42?x=1` reads `reports/42`. */
function relative(url: string): string {
  return pathOf(url).split('/').slice(2).join('/');
}

/**
 * The pages the user last visited in the routed tenant, newest first, for
 * the launcher and the home page: at most ten, each one the user may still
 * open.
 *
 * @example
 * ```ts
 * const recents = inject(TmRecents);
 * const items = computed(() => recents.pages().map((page) => ({ href: page.url, label: page.title() })));
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmRecents {
  readonly #navigation = inject(TM_RECENTS_NAVIGATION);
  readonly #now = inject(TM_NOW);
  readonly #prefs = inject(TmPreferences);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #stored = this.#prefs.get(NAV_RECENT);

  /** The routed tenant's openable recent pages, newest first, at most ten. */
  readonly pages: Signal<readonly TmRecentPage[]> = computed(() => {
    const t = this.#refresher.ɵpartition()?.tenantId;
    return t === undefined
      ? []
      : this.#stored()
          .map(({ p, k, t: at, n }) => {
            const url = `/${t}/${p}`;
            const entry = this.#navigation.entry(url);
            return { url, kind: k, title: entry?.label ?? signal(n).asReadonly(), icon: entry?.icon ?? null, visitedAt: at * 1_000 };
          })
          // Filtered on read: the stored list keeps a page for when access returns.
          .filter((page) => this.#navigation.resolve(page.url) === 'ok');
  });

  /** The path whose dwell is being timed, or `null` while none is. */
  #armed: string | null = null;
  /** The routed page's path, when it is of a kind that counts. */
  #path: string | null = null;
  /** Whether the routed page's visit already counted. */
  #counted = false;
  /** The dwell's timer, while one runs. */
  #timer: ReturnType<typeof setTimeout> | undefined;
  /** This tab's own visits by tenant, newest first, while the list's ten still hold them. */
  readonly #own = new Map<number, readonly TmStoredVisit[]>();

  /** Creates the service, which counts the routed page's visits from now on. */
  constructor() {
    const host = inject(ɵTM_API_HOST);
    const contexts = inject(TmPageContexts);
    const router = inject(Router);
    const visible = signal(host.visible());
    const stop = host.listen('document', 'visibilitychange', () => visible.set(host.visible()));
    // Each navigation that lands, so a path that changes under the same context is a visit of its own.
    const navigated = signal(0);
    const navigations = router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        navigated.update((count) => count + 1);
      }
    });

    // --- A visit: a page of a listed kind, current for 2 s while visible ---
    // The page is the routed context at the router's URL. A query change on
    // the same path is the same visit, and so is a context that replaces
    // another on it; a hidden document stops the clock, and showing it again
    // starts the 2 s afresh.
    effect(() => {
      const routed = contexts.current();
      const shown = visible();
      navigated();
      untracked(() => {
        const counts = routed !== null && routed.routeTemplate !== LIST && KINDS.includes(routed.kind);
        const path = counts ? pathOf(router.url) : null;
        if (path !== this.#path) {
          this.#path = path;
          this.#counted = false;
        }
        const armed = shown && !this.#counted ? path : null;
        if (armed !== this.#armed) {
          clearTimeout(this.#timer);
          this.#armed = armed;
          if (armed !== null) {
            this.#timer = setTimeout(() => {
              this.#counted = true;
              this.#armed = null;
              // The context as it stands at the end of the 2 s, which names the page.
              const page = contexts.current();
              if (page !== null && KINDS.includes(page.kind)) {
                this.#visit(armed, page.kind, page.title);
              }
            }, DWELL_MS);
          }
        }
      });
    });

    // --- Another tab's list that lacks this tab's visits gets them back ---
    // A tab opened after this tab's visits builds on the stored list, which
    // may not hold them yet, and its list replaces this tab's unsaved one:
    // this tab sets the two merged, which every tab then shows and keeps.
    effect(() => {
      const list = this.#stored();
      const t = this.#refresher.ɵpartition()?.tenantId;
      untracked(() => {
        const own = t === undefined ? undefined : this.#own.get(t);
        if (own !== undefined) {
          this.#keep(t!, own, list);
        }
      });
    });

    inject(DestroyRef).onDestroy(() => {
      stop();
      navigations.unsubscribe();
      clearTimeout(this.#timer);
    });
  }

  /**
   * Records a visit at the front of the list; a revisit moves its entry, and
   * the eleventh falls off.
   *
   * @param path - The page's path, such as `/17/reports/42`.
   * @param kind - The page's family.
   * @param title - The page's title as displayed.
   */
  #visit(path: string, kind: TmPageKind, title: Signal<string>): void {
    const t = untracked(this.#refresher.ɵpartition)?.tenantId;
    if (t === undefined) {
      return;
    }
    const p = relative(path);
    const visit: TmStoredVisit = { p, k: kind, t: Math.floor(this.#now() / 1_000), n: untracked(title) };
    const own = [visit, ...(this.#own.get(t) ?? []).filter((held) => held.p !== p)];
    this.#keep(t, own, untracked(this.#stored));
  }

  /**
   * Sets the tenant's list to the shown one with this tab's visits merged in,
   * when it lacks any of them; the visits the merged ten no longer hold are
   * forgotten.
   */
  #keep(t: number, own: readonly TmStoredVisit[], list: readonly TmStoredVisit[]): void {
    const merged = merge(own, list);
    const kept = own.filter((visit) => merged.includes(visit));
    if (kept.length > 0) {
      this.#own.set(t, kept);
    } else {
      this.#own.delete(t);
    }
    if (!sameVisits(merged, list)) {
      this.#prefs.set(NAV_RECENT, merged);
    }
  }
}
