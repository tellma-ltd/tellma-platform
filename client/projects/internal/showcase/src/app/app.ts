// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, DOCUMENT, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Dir } from '@angular/cdk/bidi';
import { TranslocoService } from '@jsverse/transloco';

import { SHOWCASE_STORIES } from './stories';

/**
 * The showcase shell: a persistent header with the story menu and the
 * light/dark + EN/AR toggles, visible on every page. The URL stays the
 * source of truth for appearance (?theme=dark, ?dir=rtl — every story stays
 * addressable in all combinations for the Playwright matrix): the theme
 * toggle rewrites the query params, and this shell is the ONE place that
 * applies dir/lang/data-theme to <html>. Direction follows the language
 * unless ?dir= forces it.
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

  protected readonly stories = SHOWCASE_STORIES;
  protected readonly lang = signal('en');

  // Query params are route-global, so the root route sees every page's.
  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);

  protected readonly theme = computed(() =>
    this.query()?.get('theme') === 'dark' ? 'dark' : 'light',
  );
  protected readonly dir = computed(() =>
    (this.query()?.get('dir') ?? (this.lang() === 'ar' ? 'rtl' : 'ltr')) === 'rtl'
      ? ('rtl' as const)
      : ('ltr' as const),
  );

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
}
