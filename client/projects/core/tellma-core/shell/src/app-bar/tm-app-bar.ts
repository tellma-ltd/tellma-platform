// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { NgComponentOutlet } from '@angular/common';
import {
  afterNextRender,
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  type ElementRef,
  inject,
  signal,
  type Type,
  untracked,
  ViewContainerRef,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TmLocalization, type TmShellSlot, TmTelemetry, TmUrls } from '@tellma/core';
import { TmCounter } from '@tellma/core-ui/counter';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmAvatarTone, tmInitials } from '@tellma/core-ui/initials';
import { tmAriaKeyShortcuts, tmFormatKeys } from '@tellma/core-ui/keys';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import Eye from 'lucide/dist/esm/icons/eye.mjs';
import FlaskConical from 'lucide/dist/esm/icons/flask-conical.mjs';
import Menu from 'lucide/dist/esm/icons/menu.mjs';
import Search from 'lucide/dist/esm/icons/search.mjs';
import Wrench from 'lucide/dist/esm/icons/wrench.mjs';

import { TmLauncher } from '../launcher/tm-launcher';
import { tmRegisterTenantShellStrings } from '../tm-tenant-shell-strings';
import { TM_SHELL_HISTORY_LOAD, TmInstalledMode } from './tm-installed-mode';
import { type TmShellMarker, TmShellIdentity } from './tm-shell-identity';
import { TmShellOverlays } from './tm-shell-overlays';
import { TmShellSlots, type TmShellSlotView } from './tm-shell-slots';

// The tenant app bar (spec 0027 §7.1): one 40 px ink row whose items run
// from the wordmark to the session button and collapse by breakpoint in a
// fixed order. Every item has a fixed box, so nothing moves when counts,
// names or states change. The breakpoints are CSS media queries over spec
// 0010 §B.1's widths, so the bar's first frame is right at every width and
// the hidden items leave the accessibility tree with their boxes.
//
// The bar draws only buttons. What they open lives in a deferred chunk
// (TmShellOverlays): the session menu, the markers' popovers and the slots'
// panels. The session button paints its initials here, on the same tone as
// tm-avatar's, and the chunk's avatar, which can show the user's photo,
// replaces them once it loads. The Menu button, the search field and the
// search buttons open the launcher, whose panel is a deferred chunk of its
// own; from lg up the field is the launcher's combobox input, and an open
// that beats the chunk shows a skeleton in the panel's place, which below lg
// holds focus, so the keys typed meanwhile are the launcher's. Each button
// hands itself to the launcher as it opens it, so focus can go back to it
// where a press does not focus a button (Safari, iOS). In installed
// mode the bar reserves Back and Forward's box and fills it from a chunk of
// their own, which a browser tab never loads.

/** The icon of each marker. */
const MARKER_ICONS = { sandbox: FlaskConical, readOnly: Eye, maintenance: Wrench } as const;

/** The suffix each marker adds to the session button's name. */
const MARKER_SUFFIXES: Readonly<Record<TmShellMarker, string>> = {
  sandbox: 'tm.tenantShell.bar.accountSandbox',
  readOnly: 'tm.tenantShell.bar.accountReadOnly',
  maintenance: 'tm.tenantShell.bar.accountMaintenance',
};

/**
 * The tenant shell's app bar: the wordmark, the grouped Menu and search,
 * the installed-mode history buttons, the distribution's slots, the
 * reserved boxes of the attention bell and tray, the tenant markers and the
 * session button. The tenant shell renders it in the frame's first row.
 *
 * @example
 * ```html
 * <tm-shell-frame>
 *   <tm-app-bar tmShellBar />
 *   <router-outlet />
 * </tm-shell-frame>
 * ```
 */
@Component({
  selector: 'tm-app-bar',
  imports: [NgComponentOutlet, RouterLink, TmCounter, TmIcon, TmSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The wordmark, a link to the tenant's home: the mark below md. -->
    <a class="tm-app-bar__wordmark" [routerLink]="home()" (click)="navigated()">
      <picture>
        <source media="(width < 768px)" srcset="brand/mark-on-dark.svg" />
        <img src="brand/wordmark-on-dark.svg" [alt]="homeLabel()" />
      </picture>
    </a>
    <!--
      Menu and search, grouped from md; the field from lg, its keycap from xl.
      A press inside the group is the launcher's own, never an outside press.
    -->
    <div class="tm-app-bar__group" data-tm-launcher-trigger (pointerenter)="prefetchLauncher()" (focusin)="prefetchLauncher()">
      <button
        #menuButton
        type="button"
        class="tm-app-bar__menu"
        aria-haspopup="dialog"
        [attr.aria-expanded]="launcherOpen()"
        [attr.aria-label]="menuLabel()"
        (click)="toggleLauncher(menuButton)"
      >
        <tm-icon [svg]="menuIcon" />
        <span class="tm-app-bar__wide" aria-hidden="true">{{ menuLabel() }}</span>
      </button>
      <button
        #groupSearch
        type="button"
        class="tm-app-bar__group-search"
        aria-haspopup="dialog"
        [attr.aria-expanded]="launcherOpen()"
        [attr.aria-label]="searchLabel()"
        (click)="searchLauncher(groupSearch)"
      >
        <tm-icon [svg]="searchIcon" />
      </button>
      <label class="tm-app-bar__field">
        <tm-icon [svg]="searchIcon" />
        <!-- The launcher's combobox input from lg up: its listbox and active option are the panel's. -->
        <input
          #field
          type="text"
          role="combobox"
          autocomplete="off"
          spellcheck="false"
          aria-autocomplete="list"
          [placeholder]="searchLabel()"
          [attr.aria-label]="searchLabel()"
          [attr.aria-expanded]="launcherOpen()"
          [attr.aria-controls]="launcherListbox()"
          [attr.aria-activedescendant]="launcherActive()"
          [attr.aria-describedby]="launcherHint()"
          [attr.aria-keyshortcuts]="keyShortcuts"
          [value]="launcher.query()"
          (focus)="launcher.show('field')"
          (click)="launcher.show('field')"
          (input)="type($event)"
        />
        <kbd class="tm-app-bar__keycap" aria-hidden="true">{{ keycap }}</kbd>
      </label>
    </div>
    <!-- Back and forward, in installed mode only, from md: their box, which their chunk fills. -->
    @if (installed()) {
      <span class="tm-app-bar__history">
        @if (historyButtons(); as buttons) {
          <ng-container [ngComponentOutlet]="buttons" [ngComponentOutletInputs]="historyInputs" />
        }
      </span>
    }
    <span class="tm-app-bar__spacer"></span>
    <!-- Search, after the spacer below md. -->
    <button
      #narrowSearch
      type="button"
      class="tm-app-bar__icon-button tm-app-bar__narrow-search"
      aria-haspopup="dialog"
      data-tm-launcher-trigger
      [attr.aria-expanded]="launcherOpen()"
      [attr.aria-label]="searchLabel()"
      (pointerenter)="prefetchLauncher()"
      (focus)="prefetchLauncher()"
      (click)="searchLauncher(narrowSearch)"
    >
      <tm-icon [svg]="searchIcon" />
    </button>
    <!-- The distribution's slots, from md; below it, the session menu lists them. -->
    @for (item of slots; track item.slot.id) {
      <button
        #slotButton
        type="button"
        class="tm-app-bar__icon-button tm-app-bar__slot"
        aria-haspopup="dialog"
        [attr.data-tm-slot]="item.slot.id"
        [attr.aria-expanded]="overlays.isOpenFor(slotButton)"
        [attr.aria-label]="slotLabel(item)"
        (pointerenter)="preloadSlot(item.slot)"
        (focus)="preloadSlot(item.slot)"
        (click)="toggleSlot(item.slot, slotButton)"
      >
        <tm-icon [name]="item.slot.icon" />
        @if (item.count; as count) {
          <tm-counter placement="overlay" [value]="count()" />
        }
      </button>
    }
    <!-- The attention bell and tray: fixed boxes their own spec fills. -->
    <span class="tm-app-bar__reserved" data-tm-slot="bell"></span>
    <span class="tm-app-bar__reserved" data-tm-slot="tray"></span>
    <!-- The help and assistant slots: reserved, with no width. -->
    <span data-tm-slot="help" hidden></span>
    <span data-tm-slot="assistant" hidden></span>
    <!-- The markers: chips from md, icon circles below. -->
    @for (marker of markers(); track marker) {
      <button
        #markerButton
        type="button"
        class="tm-app-bar__marker"
        aria-haspopup="dialog"
        [class.tm-app-bar__marker--accent]="marker === 'sandbox'"
        [attr.data-tm-marker]="marker"
        [attr.aria-expanded]="overlays.isOpenFor(markerButton)"
        (pointerenter)="overlays.preload()"
        (focus)="overlays.preload()"
        (click)="toggleMarker(marker, markerButton)"
      >
        <tm-icon [svg]="markerIcons[marker]" />
        <span class="tm-app-bar__marker-label">{{ markerLabel(marker) }}</span>
      </button>
    }
    <!-- The session button: the avatar, then the names this width shows. -->
    <button
      #session
      type="button"
      class="tm-app-bar__session"
      aria-haspopup="menu"
      [attr.aria-expanded]="overlays.isOpenFor(session)"
      [attr.aria-controls]="overlays.controls(session)"
      [attr.aria-label]="accountLabel()"
      (pointerenter)="overlays.preload()"
      (focus)="overlays.preload()"
      (click)="toggleSession(session)"
      (keydown)="sessionKeydown($event, session)"
    >
      @if (overlays.avatar(); as avatar) {
        <ng-container [ngComponentOutlet]="avatar" />
      } @else {
        <span
          class="tm-app-bar__face"
          aria-hidden="true"
          [attr.data-tone]="tone()"
          [style.--tm-app-bar-face-bg]="tone() === null ? null : 'var(--categorical-' + tone() + '-tint)'"
          [style.--tm-app-bar-face-text]="tone() === null ? null : 'var(--categorical-' + tone() + '-on-tint)'"
          >{{ initials() }}</span
        >
      }
      <span class="tm-app-bar__names" aria-hidden="true">
        <span class="tm-app-bar__user" dir="auto">{{ identity.userName() }}</span>
        <!--
          The dot and the tenant's name, or the cold launch's skeleton: the
          sync replaces the pair whole, so the dot, which follows the user's
          name, is never moved from the cold name's end to the synced one's.
        -->
        @if (identity.tenantName(); as tenant) {
          <span class="tm-app-bar__dot">·</span>
          <span class="tm-app-bar__tenant" dir="auto">{{ tenant }}</span>
        } @else {
          <span class="tm-app-bar__dot">·</span>
          <tm-skeleton class="tm-app-bar__tenant-skeleton" width="96px" />
        }
      </span>
    </button>
    <!-- The overlays' host, where their deferred chunk renders. -->
    <ng-container #overlayHost />
    <!--
      The launcher's skeleton, in its panel's place, while an open waits for
      its chunk; below lg it holds focus, the input not being there yet.
    -->
    @if (launcherPending()) {
      <div #launcherSkeleton popover="manual" class="tm-app-bar__launcher-skeleton" role="dialog" tabindex="-1" aria-busy="true" [attr.aria-label]="menuLabel()">
        <tm-skeleton [lines]="6" />
      </div>
    }
  `,
  styles: `
    /* The ink bar, the same in both themes, padded clear of the window's safe areas. */
    :host {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      block-size: 100%;
      box-sizing: border-box;
      /* The insets are physical sides, whatever the direction. */
      padding-right: max(var(--space-4), env(safe-area-inset-right));
      padding-left: max(var(--space-4), env(safe-area-inset-left));
      background: var(--ink-900);
      color: var(--text-on-dark);
      font: var(--weight-regular) var(--text-sm) / var(--leading-ui) var(--font-ui);
      --icon-size: 16px;
      --counter-halo: var(--ink-900);
    }

    button {
      flex: none;
      padding: 0;
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
      cursor: pointer;
    }

    button:disabled {
      cursor: default;
      opacity: 0.4;
    }

    button:not(:disabled):hover,
    button[aria-expanded='true'] {
      background: rgb(255 255 255 / 0.1);
    }

    a:focus-visible,
    button:focus-visible {
      outline: var(--focus-ring-width) solid var(--text-on-dark);
      outline-offset: 1px;
    }

    /* The wordmark never mirrors. */
    .tm-app-bar__wordmark {
      display: flex;
      flex: none;
      align-items: center;
      border-radius: var(--radius-sm);
    }

    /*
     * The width is the height times the file's ratio, which the shell frame
     * writes, so the box is final before the file loads; without the ratio
     * the declaration is invalid at computed-value time, and the width is auto.
     */
    .tm-app-bar__wordmark img {
      display: block;
      block-size: 14px;
      inline-size: calc(14px * var(--tm-brand-wordmark-on-dark));
    }

    /* The 30 px icon buttons. */
    .tm-app-bar__icon-button,
    .tm-app-bar__group-search {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      inline-size: 30px;
      block-size: 30px;
      border-radius: var(--radius-md);
    }

    .tm-app-bar__slot {
      position: relative;
    }

    /* The two history buttons' box, reserved before their chunk lands. */
    .tm-app-bar__history {
      display: flex;
      flex: none;
      gap: var(--space-2);
      inline-size: calc(60px + var(--space-2));
      block-size: 30px;
    }

    /* Menu and search in one bordered box. */
    .tm-app-bar__group {
      display: flex;
      flex: 0 1 auto;
      align-items: center;
      min-inline-size: 0;
      block-size: 30px;
      box-sizing: border-box;
      border: var(--border-width) solid rgb(255 255 255 / 0.12);
      border-radius: 6px;
      background: rgb(255 255 255 / 0.07);
    }

    .tm-app-bar__group:focus-within {
      background: rgb(255 255 255 / 0.1);
    }

    .tm-app-bar__group:has(:focus-visible) {
      outline: var(--focus-ring-width) solid var(--text-on-dark);
      outline-offset: 1px;
    }

    .tm-app-bar__group :focus-visible {
      outline: none;
    }

    .tm-app-bar__menu {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      block-size: 100%;
      padding-inline: var(--space-2);
      border-radius: 5px;
    }

    .tm-app-bar__group-search {
      block-size: 100%;
      border-radius: 5px;
    }

    /* The field: the launcher's input from lg, capped by width. */
    .tm-app-bar__field {
      display: none;
      flex: 1 1 auto;
      align-items: center;
      gap: var(--space-2);
      min-inline-size: 0;
      block-size: 100%;
      padding-inline: var(--space-2);
      border-inline-start: var(--border-width) solid rgb(255 255 255 / 0.12);
      cursor: text;
    }

    .tm-app-bar__field input {
      flex: 1 1 auto;
      min-inline-size: 0;
      padding: 0;
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
    }

    .tm-app-bar__field input::placeholder {
      color: inherit;
      opacity: 0.7;
    }

    .tm-app-bar__keycap {
      display: none;
      flex: none;
      padding: 0 var(--space-1);
      border: var(--border-width) solid rgb(255 255 255 / 0.24);
      border-radius: var(--radius-sm);
      font: var(--weight-regular) var(--text-xs) / 16px var(--font-ui);
      opacity: 0.8;
    }

    .tm-app-bar__spacer {
      flex: 1 1 0;
    }

    /* The attention bell's and tray's boxes. */
    .tm-app-bar__reserved {
      flex: none;
      inline-size: 30px;
      block-size: 30px;
    }

    /* The markers: 20 px pills, a sandbox's in the accent tint and a state's in the warning tint. */
    .tm-app-bar__marker {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      block-size: 20px;
      padding-inline: var(--space-2);
      border: var(--border-width) solid var(--warning-border);
      border-radius: var(--radius-full);
      background: var(--warning-bg);
      color: var(--text-body);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      white-space: nowrap;
      --icon-size: 12px;
    }

    .tm-app-bar__marker tm-icon {
      color: var(--warning);
    }

    .tm-app-bar__marker--accent {
      border-color: var(--badge-accent-bg);
      background: var(--badge-accent-bg);
      color: var(--badge-accent-text);
    }

    .tm-app-bar__marker--accent tm-icon {
      color: inherit;
    }

    .tm-app-bar__marker:not(:disabled):hover,
    .tm-app-bar__marker[aria-expanded='true'] {
      filter: brightness(1.1);
      background: var(--warning-bg);
    }

    .tm-app-bar__marker--accent:not(:disabled):hover,
    .tm-app-bar__marker--accent[aria-expanded='true'] {
      background: var(--badge-accent-bg);
    }

    /*
     * The session button: a fixed box wherever it shows names (below), so a
     * cold launch's names, the cookie's user name and the tenant's skeleton,
     * give way to the synced ones with nothing moving; the tenant's name
     * truncates last.
     */
    .tm-app-bar__session {
      display: inline-flex;
      flex: 0 1 auto;
      align-items: center;
      gap: var(--space-2);
      min-inline-size: 30px;
      max-inline-size: 260px;
      block-size: 30px;
      padding-inline: 3px var(--space-2);
      border-radius: var(--radius-md);
    }

    /* The first frame's face: tm-avatar's initials on the same tone. */
    .tm-app-bar__face {
      display: inline-flex;
      flex: none;
      align-items: center;
      justify-content: center;
      inline-size: var(--avatar-size-sm);
      block-size: var(--avatar-size-sm);
      border-radius: var(--radius-full);
      background: var(--tm-app-bar-face-bg, var(--avatar-bg));
      color: var(--tm-app-bar-face-text, var(--avatar-text));
      font-size: var(--avatar-font-size-sm);
      font-weight: var(--weight-semibold);
      line-height: 1;
      user-select: none;
    }

    /*
     * The names fill the button's height, so a cold line holding only the
     * tenant's skeleton bar (below xl) starts where the synced text line does.
     */
    .tm-app-bar__names {
      display: flex;
      align-self: stretch;
      align-items: center;
      gap: var(--space-1);
      min-inline-size: 0;
      white-space: nowrap;
    }

    .tm-app-bar__user,
    .tm-app-bar__tenant {
      min-inline-size: 0;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .tm-app-bar__user {
      flex: 0 999 auto;
    }

    .tm-app-bar__tenant {
      flex: 0 1 auto;
      font-weight: var(--weight-semibold);
    }

    .tm-app-bar__tenant-skeleton {
      flex: none;
    }

    /*
     * The session button's fixed box: from md, the avatar and the tenant's
     * name, with room for the cold launch's 96 px skeleton (139 px with the
     * avatar and the insets); from xl, the user's name, the dot and the
     * tenant's name, where the skeleton leaves the user's name about 40 px.
     */
    @media (width >= 768px) {
      .tm-app-bar__session {
        inline-size: 160px;
      }
    }

    @media (width >= 1200px) {
      .tm-app-bar__session {
        inline-size: 200px;
      }
    }

    /* Below xl: the tenant's name alone beside the avatar. */
    @media (width < 1200px) {
      .tm-app-bar__user,
      .tm-app-bar__dot {
        display: none;
      }
    }

    /* From md: the group's Menu with its word; below lg, the group's search icon. */
    @media (width >= 768px) {
      .tm-app-bar__narrow-search {
        display: none;
      }
    }

    @media (width >= 992px) {
      .tm-app-bar__group {
        flex-basis: 280px;
        max-inline-size: 280px;
      }

      .tm-app-bar__group-search {
        display: none;
      }

      .tm-app-bar__field {
        display: flex;
      }
    }

    @media (width >= 1200px) {
      .tm-app-bar__group {
        flex-basis: 440px;
        max-inline-size: 440px;
      }

      .tm-app-bar__keycap {
        display: inline-block;
      }
    }

    @media (width >= 1440px) {
      .tm-app-bar__group {
        flex-basis: 560px;
        max-inline-size: 560px;
      }
    }

    /* Below md: the mark, icon buttons and circles, and the avatar alone; 6 px gaps and 8 px insets. */
    @media (width < 768px) {
      :host {
        gap: 6px;
        padding-right: max(var(--space-2), env(safe-area-inset-right));
        padding-left: max(var(--space-2), env(safe-area-inset-left));
      }

      .tm-app-bar__wordmark img {
        block-size: 18px;
        inline-size: calc(18px * var(--tm-brand-mark-on-dark));
      }

      .tm-app-bar__group {
        border-color: transparent;
        background: transparent;
      }

      .tm-app-bar__menu {
        justify-content: center;
        inline-size: 30px;
        padding: 0;
      }

      .tm-app-bar__group-search,
      .tm-app-bar__history,
      .tm-app-bar__slot,
      .tm-app-bar__names {
        display: none;
      }

      .tm-app-bar__session {
        padding-inline: 3px;
      }

      .tm-app-bar__marker {
        justify-content: center;
        inline-size: 24px;
        block-size: 24px;
        padding: 0;
      }

      .tm-app-bar__marker-label {
        position: absolute;
        clip-path: inset(50%);
        inline-size: 1px;
        block-size: 1px;
        overflow: hidden;
        white-space: nowrap;
      }
    }

    /* Text that is read but not drawn: the Menu button's word below md. */
    @media (width < 768px) {
      .tm-app-bar__wide {
        display: none;
      }
    }

    /*
     * The launcher's skeleton: the panel's place under the shell's rows from
     * md up, the Menu modal's below it, full screen below sm.
     */
    .tm-app-bar__launcher-skeleton {
      position: fixed;
      inset: var(--tm-shell-top) 0 auto;
      inline-size: auto;
      block-size: 16rem;
      margin: 0;
      padding: var(--space-4);
      border: 0;
      border-block-end: var(--border-width) solid var(--divider);
      background: var(--surface-card);
      box-shadow: var(--shadow-lg);
      color: var(--text-body);
    }

    /* It holds focus as the whole surface does, its input not there yet: no ring of its own. */
    .tm-app-bar__launcher-skeleton:focus {
      outline: none;
    }

    @media (width < 768px) {
      .tm-app-bar__launcher-skeleton {
        inset: var(--modal-lg-margin);
        block-size: auto;
        border-radius: var(--modal-radius);
      }
    }

    @media (width < 576px) {
      .tm-app-bar__launcher-skeleton {
        inset: 0;
        border-radius: 0;
      }
    }

    @media (forced-colors: active) {
      .tm-app-bar__group,
      .tm-app-bar__marker {
        border-color: CanvasText;
      }
    }
  `,
})
export class TmAppBar {
  readonly #l10n = inject(TmLocalization);
  readonly #urls = inject(TmUrls);
  readonly #telemetry = inject(TmTelemetry);
  /** Where the overlays' chunk renders its host. */
  private readonly overlayHost = viewChild.required('overlayHost', { read: ViewContainerRef });
  /** The search field, the launcher's input from lg up. */
  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');
  /** The Menu button, where the launcher sends focus when no control had it. */
  private readonly menuButton = viewChild.required<ElementRef<HTMLButtonElement>>('menuButton');
  /** The launcher's skeleton, while it shows. */
  private readonly launcherSkeleton = viewChild<ElementRef<HTMLElement>>('launcherSkeleton');

  /** The launcher the Menu button and the search open. */
  protected readonly launcher = inject(TmLauncher);

  /** The shell's overlays. */
  protected readonly overlays = inject(TmShellOverlays);

  /** Who and where the user is. */
  protected readonly identity = inject(TmShellIdentity);

  readonly #loadHistory = inject(TM_SHELL_HISTORY_LOAD);
  readonly #historyButtons = signal<Type<unknown> | null>(null);

  readonly #installedMode = inject(TmInstalledMode);

  /** Whether the app runs installed, where the bar draws Back and Forward. */
  protected readonly installed = this.#installedMode.installed;

  /** The history buttons' inputs: the Navigation API they read, where the browser has it. */
  protected readonly historyInputs: Readonly<Record<string, unknown>> = { navigation: this.#installedMode.navigation };

  /** The loaded Back and Forward buttons, or `null` until their chunk is in. */
  protected readonly historyButtons = this.#historyButtons.asReadonly();

  /** The Menu glyph. */
  protected readonly menuIcon = Menu;

  /** The search glyph. */
  protected readonly searchIcon = Search;

  /** Each marker's glyph. */
  protected readonly markerIcons = MARKER_ICONS;

  /** The search field's keycap, labelled for the platform: `Ctrl K` or `⌘ K`. */
  protected readonly keycap = tmFormatKeys('Mod+K').join(' ');

  /** The search field's `aria-keyshortcuts`. */
  protected readonly keyShortcuts = tmAriaKeyShortcuts('Mod+K');

  /** The distribution's slots, each with its badge's count. */
  protected readonly slots = inject(TmShellSlots).items;

  /** The wordmark link's target: the tenant's home. */
  protected readonly home = computed(() => (this.identity.tenantId() === null ? '/' : this.#urls.home()));

  /** The wordmark's name: "{displayName} home". */
  protected readonly homeLabel = computed(() => this.#l10n.ɵtext('tm.tenantShell.bar.home', { name: this.#l10n.displayName() })());

  /** The Menu button's name. */
  protected readonly menuLabel = this.#l10n.ɵtext('tm.tenantShell.bar.menu');

  /** The search's name. */
  protected readonly searchLabel = this.#l10n.ɵtext('tm.tenantShell.bar.search');

  /** Whether the launcher is open, which the buttons that open it report. */
  protected readonly launcherOpen = computed(() => this.launcher.state() !== 'closed');

  /** The launcher's listbox, which the field controls while it is the launcher's input, else `null`. */
  protected readonly launcherListbox = computed(() => {
    const surface = this.launcher.ɵsurface();
    return surface?.usesField() ? surface.listboxId : null;
  });

  /** The launcher's active option, which the field points at while it is the launcher's input, else `null`. */
  protected readonly launcherActive = computed(() => {
    const surface = this.launcher.ɵsurface();
    return surface?.usesField() ? surface.activeId() : null;
  });

  /** The launcher's hidden instructions, which describe the field once its chunk is in, else `null`. */
  protected readonly launcherHint = computed(() => this.launcher.ɵsurface()?.hintId ?? null);

  /** Whether an open waits for the launcher's chunk, so its skeleton shows. */
  protected readonly launcherPending = this.launcher.ɵpending;

  /** The markers that show. */
  protected readonly markers = this.identity.markers;

  /**
   * The session button's name: "Account: {user}, {tenant}", then a suffix
   * per marker; "Account: {user}" while a cold launch has no tenant name.
   */
  protected readonly accountLabel = computed(() => {
    const user = this.identity.userName();
    const tenant = this.identity.tenantName();
    const base =
      tenant === null
        ? this.#l10n.ɵtext('tm.tenantShell.bar.accountUser', { user })()
        : this.#l10n.ɵtext('tm.tenantShell.bar.account', { user, tenant })();
    return this.markers().reduce((name, marker) => name + this.#l10n.ɵtext(MARKER_SUFFIXES[marker])(), base);
  });

  /** The first frame's initials, from the user's name in the active locale. */
  protected readonly initials = computed(() => tmInitials(this.identity.userName(), this.#l10n.locale()));

  /** The first frame's face tone (1 to 8), from the user's record key; `null`, the accent tone, before the record is known. */
  protected readonly tone = computed(() => {
    const key = this.identity.colorKey();
    return key === null ? null : tmAvatarTone(key);
  });

  /**
   * Creates the bar, which hands the overlays their host and the launcher
   * its field and its Menu button after its first render, and loads the
   * launcher's chunk once the browser is idle.
   */
  constructor() {
    tmRegisterTenantShellStrings(this.#l10n);
    afterNextRender(() => {
      this.overlays.attach(this.overlayHost());
      const field = this.field().nativeElement;
      this.launcher.ɵfield = field;
      this.launcher.ɵmenu = this.menuButton().nativeElement;
      const view = field.ownerDocument.defaultView;
      const idle = (run: () => void): unknown => (view?.requestIdleCallback ? view.requestIdleCallback(run) : setTimeout(run));
      idle(() => this.launcher.ɵprefetch());
    });
    // The skeleton shows in the top layer, above the page, as the panel would, and the launcher hears of it.
    afterRenderEffect(() => {
      const skeleton = this.launcherSkeleton()?.nativeElement ?? null;
      skeleton?.showPopover();
      untracked(() => this.launcher.ɵskeleton(skeleton));
    });

    // --- The history buttons' chunk, loaded once the app runs installed ---
    // A failed load (offline, or after a deploy removed the chunk) leaves the
    // reserved box empty; the next change into installed mode tries again.
    let loading = false;
    effect(() => {
      if (!this.installed() || this.#historyButtons() !== null) {
        return;
      }
      untracked(() => {
        if (loading) {
          return;
        }
        loading = true;
        this.#loadHistory().then(
          (chunk) => this.#historyButtons.set(chunk.TmShellHistory),
          (error: unknown) => {
            this.#telemetry.error(error, { source: 'tm-app-bar' });
            loading = false;
          },
        );
      });
    });
  }

  /**
   * A slot button's name: "{label}", or "{label}, {n}" with a count.
   *
   * @param item - The slot.
   * @returns The name.
   */
  protected slotLabel(item: TmShellSlotView): string {
    const count = item.count?.() ?? null;
    const label = item.label();
    return count === null || count === 0 ? label : this.#l10n.ɵtext('tm.tenantShell.bar.slotCount', { label, count })();
  }

  /**
   * A marker's label.
   *
   * @param marker - The marker.
   * @returns The label, such as "Sandbox".
   */
  protected markerLabel(marker: TmShellMarker): string {
    return this.#l10n.ɵtext(`tm.tenantShell.bar.markers.${marker}`)();
  }

  /** Starts loading the launcher's chunk, as a hover or focus on a control that opens it does. */
  protected prefetchLauncher(): void {
    this.launcher.ɵprefetch();
  }

  /**
   * A search button: opens the launcher, or, when it is open, focuses its
   * input and selects the text.
   *
   * @param button - The button.
   */
  protected searchLauncher(button: HTMLElement): void {
    this.launcher.ɵshowFrom('icon', button);
  }

  /**
   * The Menu button: opens the launcher, or closes it when it is open.
   *
   * @param button - The button.
   */
  protected toggleLauncher(button: HTMLElement): void {
    if (this.launcher.state() === 'closed') {
      this.launcher.ɵshowFrom('menu', button);
    } else {
      this.launcher.close('outside');
    }
  }

  /**
   * Hands the field's text to the launcher, which opens as the user types.
   *
   * @param event - The field's input event.
   */
  protected type(event: Event): void {
    this.launcher.setQuery((event.target as HTMLInputElement).value);
    this.launcher.show('field');
  }

  /** Starts loading a slot's panel, with the overlays' chunk. */
  protected preloadSlot(slot: TmShellSlot): void {
    this.overlays.preload();
    this.overlays.run((panels) => panels.preloadSlot(slot));
  }

  /** Opens or closes a slot's panel. */
  protected toggleSlot(slot: TmShellSlot, button: HTMLElement): void {
    this.overlays.press(button, (panels) => panels.toggleSlot(slot, button));
  }

  /** Opens or closes a marker's popover. */
  protected toggleMarker(marker: TmShellMarker, button: HTMLElement): void {
    this.overlays.press(button, (panels) => panels.toggleMarker(marker, button));
  }

  /** Opens or closes the session menu, with focus on its first row. */
  protected toggleSession(button: HTMLElement): void {
    this.overlays.press(button, (panels) => panels.toggleSession(button, 'first'));
  }

  /**
   * The session button's menu-button keys: ArrowDown opens the menu on its
   * first row and ArrowUp on its last. Enter and Space arrive as clicks.
   *
   * @param event - The key.
   * @param button - The session button.
   */
  protected sessionKeydown(event: KeyboardEvent, button: HTMLElement): void {
    if ((event.key !== 'ArrowDown' && event.key !== 'ArrowUp') || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) {
      return;
    }
    // The arrow would otherwise scroll the page behind the menu.
    event.preventDefault();
    if (!this.overlays.isOpenFor(button)) {
      const initialFocus = event.key === 'ArrowDown' ? 'first' : 'last';
      this.overlays.press(button, (panels) => panels.toggleSession(button, initialFocus), true);
    }
  }

  /** The wordmark's navigation, for telemetry. */
  protected navigated(): void {
    this.#telemetry.event('tm.shell.navigated', { source: 'wordmark' }, { sampled: true });
  }
}
