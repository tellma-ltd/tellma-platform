// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, DOCUMENT, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Dir } from '@angular/cdk/bidi';
import { TranslocoService } from '@jsverse/transloco';

import { ShowcaseCalendar, type ShowcaseCalendarId } from './i18n/showcase-calendar';
import { SHOWCASE_STORIES } from './stories';

/**
 * The showcase shell: a header carrying the light/dark, EN/AR, and
 * display-calendar toggles over a side rail listing the stories, both
 * present on every page. The URL stays the source of truth for appearance
 * (?theme=dark, ?dir=rtl — every story stays addressable in all
 * combinations for the Playwright matrix): the theme toggle rewrites the
 * query params, and this shell is the ONE place that applies
 * dir/lang/data-theme to <html>. Direction follows the language unless
 * ?dir= forces it.
 *
 * The story outlet is additionally wrapped in the CDK `Dir` directive: the
 * root Directionality reads <html dir> ONCE at construction, so a LIVE dir
 * flip would leave CDK overlays positioning (and stamping dir on) the old
 * direction. The wrapper gives every story a Directionality that follows
 * the toggle.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Dir],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly calendars = inject(ShowcaseCalendar);
  /** The ambient display calendar's id, for the shell's picker. */
  protected readonly calendarId = this.calendars.id.asReadonly();

  protected readonly stories = SHOWCASE_STORIES;
  protected readonly lang = signal('en');

  // Query params are route-global, so the root route sees every page's.
  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);

  /**
   * Until the router resolves, the theme comes from the RAW url: the query
   * signal starts `undefined`, and falling back to light would flip a cold
   * ?theme=dark load light→dark across the first paint — WebKit sometimes
   * fails to re-resolve custom properties on part of the tree after such a
   * post-paint flip, leaving stale light-theme colors on a dark page.
   */
  private readonly urlDark =
    new URLSearchParams(this.document.location.search).get('theme') === 'dark';

  /** Read the same way as the theme, and for the same first-paint reason. */
  private readonly urlSize = new URLSearchParams(this.document.location.search).get('size');

  protected readonly theme = computed(() => {
    const query = this.query();
    if (query === undefined) {
      return this.urlDark ? 'dark' : 'light';
    }
    return query.get('theme') === 'dark' ? 'dark' : 'light';
  });
  protected readonly dir = computed(() =>
    (this.query()?.get('dir') ?? (this.lang() === 'ar' ? 'rtl' : 'ltr')) === 'rtl'
      ? ('rtl' as const)
      : ('ltr' as const),
  );

  /**
   * The workspace-wide control size. Unlike theme and direction this is an
   * injected provider value rather than a DOM attribute, so switching it
   * navigates hard instead of updating in place — the shell is a dev tool
   * and a reload is a fair price for reading the value at bootstrap.
   */
  protected readonly size = computed(() => {
    const size = this.query()?.get('size') ?? this.urlSize;
    return size === 'md' || size === 'lg' ? size : 'sm';
  });

  constructor() {
    effect(() => {
      const root = this.document.documentElement;
      root.dir = this.dir();
      root.lang = this.lang();
      if (this.theme() === 'dark') {
        root.setAttribute('data-theme', 'dark');
      } else {
        root.removeAttribute('data-theme');
      }
    });
  }

  protected toggleTheme(): void {
    void this.router.navigate([], {
      queryParams: { theme: this.theme() === 'dark' ? null : 'dark' },
      queryParamsHandling: 'merge',
    });
  }

  protected setLang(lang: string): void {
    this.transloco.setActiveLang(lang);
    this.lang.set(lang);
  }

  /** Switches the ambient display calendar for every story at once. */
  protected setCalendar(event: Event): void {
    this.calendars.id.set((event.target as HTMLSelectElement).value as ShowcaseCalendarId);
  }

  /**
   * Switches the workspace-wide control size. A full page load, not a
   * router navigation: the size is read once when `provideTellmaUi()` runs
   * at bootstrap, so nothing already on screen would pick up a new value.
   */
  protected setSize(event: Event): void {
    const url = new URL(this.document.location.href);
    url.searchParams.set('size', (event.target as HTMLSelectElement).value);
    this.document.location.assign(url.toString());
  }
}
