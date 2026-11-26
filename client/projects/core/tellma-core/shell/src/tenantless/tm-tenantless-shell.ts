// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  inject,
  input,
  signal,
} from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { TmLaunchCache, TmLocalization, TmSession, ɵTmClientMetadataStore } from '@tellma/core';
import { TmAvatar } from '@tellma/core-ui/avatar';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmMenu, type TmMenuEntry, type TmMenuItem, TmMenuTrigger } from '@tellma/core-ui/menu';
import { Globe } from 'lucide';

import { TmShellFrame } from '../frame/tm-shell-frame';
import { tmRegisterTenantlessStrings } from './tm-tenantless-strings';

// The tenantless shell (spec 0027 §6.1): the shell frame of `/tenants` and
// `/signed-out`, under one 40 px ink app bar with the wordmark, a spacer,
// the language menu and the account menu. Below `md` the wordmark becomes
// the mark, the language's name gives way to the globe icon and the
// account's name and email to an avatar; the hidden text stays in each
// button's accessible name. On the signed-out page the bar shows the
// wordmark alone. The pinned bar's row is 0 outside a tenant.

/**
 * The frame of the tenantless pages: the ink app bar with the wordmark, the
 * language menu and the account menu, then the page in `main`.
 *
 * @example
 * ```html
 * <tm-tenantless-shell>
 *   <h1 tmPageTitle tabindex="-1">Choose a tenant</h1>
 * </tm-tenantless-shell>
 * ```
 */
@Component({
  selector: 'tm-tenantless-shell',
  imports: [RouterLink, TmShellFrame, TmAvatar, TmIcon, TmMenu, TmMenuTrigger],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-shell-frame>
      <!-- The app bar: the wordmark, a spacer, then the menus at the inline end. -->
      <div tmShellBar class="tm-tenantless-shell__bar">
        <a class="tm-tenantless-shell__wordmark" routerLink="/">
          <picture>
            <source media="(width < 768px)" srcset="brand/mark-on-dark.svg" />
            <img src="brand/wordmark-on-dark.svg" [alt]="displayName()" />
          </picture>
        </a>
        <span class="tm-tenantless-shell__spacer"></span>
        @if (!signedOut()) {
          <!-- The language menu, absent with one language. -->
          @if (languageItems().length > 1) {
            <button class="tm-tenantless-shell__button" [tmMenuTriggerFor]="languageMenu">
              <tm-icon class="tm-tenantless-shell__narrow" [svg]="globe" />
              <span class="tm-tenantless-shell__wide">
                <span class="tm-tenantless-shell__hidden">{{ languageLabel() }}: </span>
                <span [lang]="language()">{{ languageName() }}</span>
              </span>
            </button>
            <tm-menu #languageMenu [items]="languageItems()" [aria-label]="languageLabel()" />
          }
          <!-- The account menu: the user's name over the email, or the avatar below md. -->
          @if (profile(); as profile) {
            <button class="tm-tenantless-shell__button" [tmMenuTriggerFor]="accountMenu">
              <tm-avatar class="tm-tenantless-shell__narrow" [name]="profile.name" decorative />
              <span class="tm-tenantless-shell__wide tm-tenantless-shell__who">
                <span class="tm-tenantless-shell__name" dir="auto">{{ profile.name }}</span>
                <span class="tm-tenantless-shell__email" dir="auto">{{ profile.email }}</span>
              </span>
            </button>
            <tm-menu #accountMenu [items]="accountItems()" [aria-label]="accountLabel()" />
          }
        }
      </div>
      <!-- The page. -->
      <ng-content />
    </tm-shell-frame>
  `,
  styles: `
    :host {
      display: contents;
    }

    /* The ink bar, the same in both themes. */
    .tm-tenantless-shell__bar {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      block-size: 100%;
      /* The insets are physical sides, whatever the direction. */
      padding-right: max(var(--space-4), env(safe-area-inset-right));
      padding-left: max(var(--space-4), env(safe-area-inset-left));
      background: var(--ink-900);
      color: var(--text-on-dark);
    }

    .tm-tenantless-shell__wordmark {
      display: flex;
      align-items: center;
      border-radius: var(--radius-sm);
    }

    /*
     * The wordmark never mirrors. Its width is its height times the file's
     * ratio, which the shell frame writes, so the box is final before the file
     * loads; without the ratio the declaration is invalid at computed-value
     * time, and the width is auto.
     */
    .tm-tenantless-shell__wordmark img {
      display: block;
      block-size: 14px;
      inline-size: calc(14px * var(--tm-brand-wordmark-on-dark));
    }

    .tm-tenantless-shell__spacer {
      flex: 1;
    }

    .tm-tenantless-shell__button {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      min-inline-size: 30px;
      max-inline-size: 260px;
      block-size: 30px;
      padding-inline: var(--space-2);
      border: 0;
      border-radius: var(--radius-md);
      background: transparent;
      color: inherit;
      font: var(--weight-regular) var(--text-sm) / var(--leading-ui) var(--font-ui);
      cursor: pointer;
    }

    .tm-tenantless-shell__button:hover,
    .tm-tenantless-shell__button[aria-expanded='true'] {
      background: rgb(255 255 255 / 0.1);
    }

    .tm-tenantless-shell__wordmark:focus-visible,
    .tm-tenantless-shell__button:focus-visible {
      outline: var(--focus-ring-width) solid var(--text-on-dark);
      outline-offset: 1px;
    }

    .tm-tenantless-shell__who {
      display: grid;
      min-inline-size: 0;
      text-align: start;
      line-height: 1.15;
    }

    .tm-tenantless-shell__name,
    .tm-tenantless-shell__email {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tm-tenantless-shell__email {
      font-size: var(--text-xs);
      opacity: 0.8;
    }

    .tm-tenantless-shell__narrow {
      display: none;
    }

    /* Text that is read but not drawn. */
    .tm-tenantless-shell__hidden {
      position: absolute;
      clip-path: inset(50%);
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      white-space: nowrap;
    }

    /* Below md: the mark, the globe and the avatar; the names stay in the accessible names. */
    @media (width < 768px) {
      .tm-tenantless-shell__bar {
        gap: 6px;
      }

      .tm-tenantless-shell__wordmark img {
        block-size: 18px;
        inline-size: calc(18px * var(--tm-brand-mark-on-dark));
      }

      .tm-tenantless-shell__narrow {
        display: flex;
      }

      .tm-tenantless-shell__wide {
        position: absolute;
        clip-path: inset(50%);
        inline-size: 1px;
        block-size: 1px;
        overflow: hidden;
        white-space: nowrap;
      }
    }
  `,
})
export class TmTenantlessShell {
  readonly #l10n = inject(TmLocalization);
  readonly #session = inject(TmSession);
  readonly #cache = inject(TmLaunchCache);
  readonly #metadata = inject(ɵTmClientMetadataStore);
  readonly #location = inject(DOCUMENT).location;
  // The page's address, which the account link returns to.
  readonly #here = signal(this.#location.href);

  /** Whether the page is the signed-out page, whose bar shows the wordmark alone. */
  readonly signedOut = input(false, { transform: booleanAttribute });

  /** The globe icon, the language menu's face below `md`. */
  protected readonly globe = Globe;

  /** The distribution's display name, the wordmark's text. */
  protected readonly displayName = this.#l10n.displayName;

  /** The active language. */
  protected readonly language = this.#l10n.language;

  /** The signed-in user, whose name and email the account menu shows. */
  protected readonly profile = this.#session.profile;

  /** The language menu's name. */
  protected readonly languageLabel = this.#l10n.ɵtext('tm.tenantless.bar.language');

  /** The account menu's name. */
  protected readonly accountLabel = this.#l10n.ɵtext('tm.tenantless.bar.account');

  /** The active language's native name. */
  protected readonly languageName = computed(() => this.#nativeName(this.language()));

  /** The language menu's radio items, one per language the switch offers. */
  protected readonly languageItems = computed<readonly TmMenuEntry[]>(() => {
    const active = this.language();
    return this.#l10n.languages().map(({ code }): TmMenuItem => ({
      id: `language-${code}`,
      label: this.#nativeName(code),
      lang: code,
      role: 'radio',
      checked: code === active,
      action: () => this.#l10n.setLanguage(code),
    }));
  });

  /** The account menu's items: My account while the distribution's facts are stored, then Sign out. */
  protected readonly accountItems = computed<readonly TmMenuEntry[]>(() => {
    // Spec 0027 §10.1: the account URL returns to this page, in the active
    // language. A distribution document without its identity facts offers none.
    const accountUrl = this.#cache.distribution()?.identity?.accountUrl;
    const href =
      accountUrl === undefined
        ? null
        : `${accountUrl}&return_url=${encodeURIComponent(this.#here())}&ui_locales=${encodeURIComponent(this.language())}`;
    return [
      ...(href === null
        ? []
        : [{ id: 'my-account', label: this.#l10n.ɵtext('tm.tenantless.bar.myAccount')(), href, target: '_blank' as const }]),
      {
        id: 'sign-out',
        label: this.#l10n.ɵtext('tm.tenantless.bar.signOut')(),
        icon: 'log-out',
        // A failed sign-out has shown its own toast.
        action: () => void this.#session.signOut().catch(() => undefined),
      },
    ];
  });

  /** Creates the shell, which follows the page's address for the account link. */
  constructor() {
    tmRegisterTenantlessStrings(this.#l10n);
    const subscription = inject(Router).events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        this.#here.set(this.#location.href);
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  /** A language's name in the language itself: the Core chunk's, else the browser's. */
  #nativeName(code: string): string {
    const metadata = this.#metadata.current()?.languages.find((language) => language.code === code);
    return metadata?.nativeName ?? new Intl.DisplayNames([code], { type: 'language' }).of(code) ?? code;
  }
}
