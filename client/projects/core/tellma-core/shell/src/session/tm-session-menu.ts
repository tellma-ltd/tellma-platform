// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  output,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { Router } from '@angular/router';
import {
  TmAppVersion,
  TmLocalization,
  TmSession,
  type TmShellSlot,
  TmUrls,
  ɵTmClientMetadataStore,
} from '@tellma/core';
import { TmBreakpoints, TmTheme, type TmThemeSetting } from '@tellma/core-ui';
import { TmMenu, type TmMenuEntry, TmMenuHeaderDef, type TmMenuItem } from '@tellma/core-ui/menu';
import { Building2, CalendarDays, Globe, Info, Keyboard, LifeBuoy, UserCog } from 'lucide';

import type { TmShellMarker } from '../app-bar/tm-shell-identity';
import { TmShellIdentity } from '../app-bar/tm-shell-identity';
import type { TmShellDialog } from '../app-bar/tm-shell-overlays';
import { TmShellSlots } from '../app-bar/tm-shell-slots';
import { TM_SHELL_SUPPORT } from '../tm-shell-support';
import { TmSessionAvatar } from './tm-session-avatar';

// The session menu (spec 0027 §10): a tm-menu 268 px wide (the viewport less
// 8 px gutters below sm, where its rows grow to 44 px), opened from the app
// bar's session button. Its header is who the user is; its rows, in order:
// the tenant card, My settings, the language and calendar switches (each
// with two or more choices), the theme, Keyboard shortcuts, About, Report a
// problem, which opens the support dialog through the primary's facade (spec
// 0026 §12.1), the distribution's slots below md (where the bar has no room
// for them), and Sign out after a separator.

/** The suffix each marker adds to the tenant card's second line. */
const MARKER_SUFFIXES: Readonly<Record<TmShellMarker, string>> = {
  sandbox: 'tm.shellOverlays.menu.tenantSandbox',
  readOnly: 'tm.shellOverlays.menu.tenantReadOnly',
  maintenance: 'tm.shellOverlays.menu.tenantMaintenance',
};

/** The theme choices, in order, with their labels' keys. */
const THEMES: readonly (readonly [TmThemeSetting, string])[] = [
  ['system', 'tm.shellOverlays.menu.themeSystem'],
  ['light', 'tm.shellOverlays.menu.themeLight'],
  ['dark', 'tm.shellOverlays.menu.themeDark'],
];

/**
 * The tenant shell's session menu: who and where the user is, the culture
 * and theme switches, help about the app, and Sign out. The app bar's
 * session button opens it.
 */
@Component({
  selector: 'tm-session-menu',
  imports: [TmMenu, TmMenuHeaderDef, TmSessionAvatar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-menu panelClass="tm-session-menu" [items]="items()" [aria-label]="label()">
      <!-- The header: the avatar, the user's name and email. -->
      <ng-template tmMenuHeader>
        <div class="tm-session-menu__who">
          <tm-session-avatar size="md" />
          <span class="tm-session-menu__names">
            <span class="tm-session-menu__name" dir="auto">{{ identity.userName() }}</span>
            <span class="tm-session-menu__email" dir="auto">{{ identity.email() }}</span>
          </span>
        </div>
      </ng-template>
    </tm-menu>
  `,
  // The panel lives in the overlay container and is tm-menu's markup, so its
  // width needs an unencapsulated rule; every selector is scoped by this
  // menu's class.
  encapsulation: ViewEncapsulation.None,
  styles: `
    .tm-menu__panel.tm-session-menu {
      box-sizing: border-box;
      inline-size: 268px;
    }

    /* Below sm: the viewport less 8 px gutters (tm-menu grows the rows to 44 px). */
    @media (width < 576px) {
      .tm-menu__panel.tm-session-menu {
        inline-size: calc(100vw - 16px);
      }
    }

    .tm-session-menu__who {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }

    .tm-session-menu__names {
      display: grid;
      min-inline-size: 0;
      line-height: var(--leading-ui);
    }

    .tm-session-menu__name,
    .tm-session-menu__email {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tm-session-menu__name {
      font-weight: var(--weight-semibold);
    }

    .tm-session-menu__email {
      color: var(--text-muted);
      font-size: var(--text-xs);
    }
  `,
})
export class TmSessionMenu {
  readonly #l10n = inject(TmLocalization);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #theme = inject(TmTheme);
  readonly #session = inject(TmSession);
  readonly #version = inject(TmAppVersion);
  readonly #metadata = inject(ɵTmClientMetadataStore);
  readonly #slots = inject(TmShellSlots).items;
  readonly #wide = inject(TmBreakpoints).atLeast('md');
  readonly #support = inject(TM_SHELL_SUPPORT);
  /** The menu. */
  private readonly menu = viewChild.required(TmMenu);

  /** Who and where the user is. */
  protected readonly identity = inject(TmShellIdentity);

  /** Emits the dialog a row asks for. */
  readonly dialog = output<TmShellDialog>();

  /** Emits the slot a row opens, as a drawer. */
  readonly slot = output<TmShellSlot>();

  /** The menu's name. */
  protected readonly label = this.#l10n.ɵtext('tm.shellOverlays.menu.label');

  /**
   * The rows, from the documents and the culture's current choices; none
   * once the route leaves the tenant, while the shell ends.
   */
  protected readonly items = computed<readonly TmMenuEntry[]>(() =>
    this.identity.tenantId() === null ? [] : this.#rows(),
  );

  /** The rows in a tenant. */
  #rows(): readonly TmMenuEntry[] {
    return [
      this.#tenantCard(),
      {
        id: 'my-settings',
        label: this.#text('mySettings'),
        icon: UserCog,
        href: this.#urls.page('me'),
        action: () => void this.#router.navigateByUrl(this.#urls.page('me')),
      },
      ...this.#language(),
      ...this.#calendar(),
      {
        group: true,
        label: this.#text('theme'),
        items: THEMES.map(([setting, key]): TmMenuItem => ({
          id: `theme-${setting}`,
          label: this.#l10n.ɵtext(key)(),
          role: 'radio',
          checked: this.#theme.setting() === setting,
          keepOpen: true,
          action: () => this.#theme.set(setting),
        })),
      },
      {
        id: 'shortcuts',
        label: this.#l10n.ɵtext('tm.shellOverlays.dialogs.shortcuts')(),
        icon: Keyboard,
        shortcut: 'Mod+/',
        action: () => this.#ask(() => this.dialog.emit('shortcuts')),
      },
      {
        id: 'about',
        label: this.#l10n.ɵtext('tm.shellOverlays.dialogs.about')(),
        icon: Info,
        detail: this.#version.display,
        action: () => this.#ask(() => this.dialog.emit('about')),
      },
      {
        id: 'report-problem',
        label: this.#text('reportProblem'),
        icon: LifeBuoy,
        // The facade opens the dialog in this frame once its host is in, which the startup preloads at idle.
        action: () => this.#ask(() => this.#support.open()),
      },
      ...this.#slotRows(),
      { separator: true },
      {
        id: 'sign-out',
        label: this.#text('signOut'),
        icon: 'log-out',
        // A failed sign-out has shown its own toast.
        action: () => void this.#session.signOut().catch(() => undefined),
      },
    ];
  }

  /**
   * Opens the menu at the session button, or closes it when it is open there.
   *
   * @param button - The session button, which takes focus back.
   * @param initialFocus - The row that takes focus.
   */
  toggle(button: HTMLElement, initialFocus: 'first' | 'last'): void {
    const menu = this.menu();
    if (menu.ɵisOpenFor(button)) {
      menu.close();
    } else {
      menu.open(button, { restoreFocus: button, initialFocus });
    }
  }

  /**
   * Whether the menu is open from a button.
   *
   * @param button - The button.
   * @returns Whether it is; a binding follows it.
   */
  isOpenFor(button: HTMLElement): boolean {
    return this.menu().ɵisOpenFor(button);
  }

  /**
   * The id of the open menu's list, which the button controls.
   *
   * @param button - The button.
   * @returns The id, or `null` while the menu is not open there.
   */
  controls(button: HTMLElement): string | null {
    return this.isOpenFor(button) ? this.menu().ɵlistId : null;
  }

  /**
   * Keeps a press on the session button from counting as an outside press.
   *
   * @param button - The session button.
   * @returns The callback that releases it.
   */
  register(button: HTMLElement): () => void {
    return this.menu().ɵregisterTrigger(button);
  }

  /** Closes the menu, leaving focus where it is. */
  close(): void {
    this.menu().close({ restoreFocus: false });
  }

  /**
   * Closes the menu, which returns focus to the session button, then asks
   * for a modal: the modal then returns focus to the button when it closes.
   */
  #ask(emit: () => void): void {
    this.menu().close();
    emit();
  }

  /** A string of the menu. */
  #text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.shellOverlays.menu.${key}`, params)();
  }

  /** The tenant card: the tenant's name over its id and markers, which switches tenants. */
  #tenantCard(): TmMenuItem {
    const tenantId = this.identity.tenantId();
    const description = this.identity
      .markers()
      .reduce(
        (line, marker) => line + this.#l10n.ɵtext(MARKER_SUFFIXES[marker])(),
        this.#text('tenant', { id: tenantId ?? '' }),
      );
    return {
      id: 'tenant',
      label: this.identity.tenantName() ?? this.#text('tenant', { id: tenantId ?? '' }),
      description,
      detail: this.#text('switch'),
      detailLabel: this.#text('switchTenant'),
      icon: Building2,
      // Nothing is cleared, so coming back to the tenant is a warm launch.
      action: () => void this.#router.navigateByUrl(this.#urls.picker()),
    };
  }

  /** The language switch: a submenu of the offered languages, by their native names; none with one language. */
  #language(): TmMenuEntry[] {
    const languages = this.#l10n.languages();
    if (languages.length < 2) {
      return [];
    }
    const active = this.#l10n.language();
    return [
      {
        id: 'language',
        label: this.#text('language'),
        icon: Globe,
        detail: this.#nativeName(active),
        items: [
          {
            group: true,
            items: languages.map(({ code }): TmMenuItem => ({
              id: `language-${code}`,
              label: this.#nativeName(code),
              lang: code,
              role: 'radio',
              checked: code === active,
              action: () => this.#l10n.setLanguage(code),
            })),
          },
        ],
      },
    ];
  }

  /** The calendar switch: a submenu of the tenant's calendars; none with one calendar. */
  #calendar(): TmMenuEntry[] {
    const calendars = this.#l10n.calendars();
    if (calendars.length < 2) {
      return [];
    }
    const active = this.#l10n.calendarCode();
    const name = (code: string): string => this.#l10n.ɵtext(`Calendar_${code}`, undefined, code)();
    return [
      {
        id: 'calendar',
        label: this.#text('calendar'),
        icon: CalendarDays,
        detail: name(active),
        items: [
          {
            group: true,
            items: calendars.map((code): TmMenuItem => ({
              id: `calendar-${code}`,
              label: name(code),
              role: 'radio',
              checked: code === active,
              action: () => this.#l10n.setCalendar(code),
            })),
          },
        ],
      },
    ];
  }

  /** The distribution's slots below md, where the app bar has no room for them: each opens its panel as a drawer. */
  #slotRows(): TmMenuItem[] {
    if (this.#wide()) {
      return [];
    }
    return this.#slots.map(({ slot, label, count }) => {
      const value = count?.() ?? null;
      return {
        id: `slot-${slot.id}`,
        label: label(),
        icon: slot.icon,
        ...(value === null || value === 0
          ? {}
          : { detail: new Intl.NumberFormat(this.#l10n.locale()).format(value) }),
        action: () => this.#ask(() => this.slot.emit(slot)),
      };
    });
  }

  /** A language's name in the language itself: the Core chunk's, else the browser's. */
  #nativeName(code: string): string {
    const metadata = this.#metadata.current()?.languages.find((language) => language.code === code);
    return (
      metadata?.nativeName ?? new Intl.DisplayNames([code], { type: 'language' }).of(code) ?? code
    );
  }
}
