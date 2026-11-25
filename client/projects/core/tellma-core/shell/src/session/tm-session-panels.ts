// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  InjectionToken,
  signal,
  type Type,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TmLaunchCache, TmLocalization, type TmShellSlot, TmShortcuts, TmUrls } from '@tellma/core';
import { TmBreakpoints } from '@tellma/core-ui';
import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';
import { TmPopover, TmPopoverContent } from '@tellma/core-ui/popover';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

import { type TmShellMarker, TmShellIdentity } from '../app-bar/tm-shell-identity';
import type { TmShellDialog, TmShellPanels } from '../app-bar/tm-shell-overlays';
import { TmShellSlots } from '../app-bar/tm-shell-slots';
import { tmTenantNames } from '../tenantless/tm-tenant-names';
import { TM_SHELL_SUPPORT } from '../tm-shell-support';
import { TmSessionMenu } from './tm-session-menu';
import { TmShellLazyContent, type TmShellLazyContentData } from './tm-shell-lazy-content';
import { tmRegisterShellOverlaysStrings } from './tm-shell-overlays-strings';

// The tenant shell's overlays (spec 0027 §7.2, §7.4, §10, §11), the deferred
// chunk the app bar's buttons open:
//
// - the session menu;
// - one marker popover, 280 px, re-anchored to whichever marker opened it;
// - one slot popover of size md from lg up; below lg, and from the session
//   menu's rows, a slot opens its panel in an end drawer instead;
// - the About and Shortcuts dialogs, TmModal dialogs of size md (full screen
//   below sm), each in a deferred chunk of its own, which open at once with
//   a placeholder in their final geometry.
//
// Every overlay closes on Escape or an outside click and returns focus to
// its button. A slot's panel loads on first open, or on the first hover or
// focus of its button.

export { TmSessionAvatar } from './tm-session-avatar';
export type { TmSessionMenu } from './tm-session-menu';

/** Loads each dialog's component. Internal: a test delays a load or makes it fail. */
export const TM_SHELL_DIALOGS_LOAD = new InjectionToken<Readonly<Record<TmShellDialog, () => Promise<Type<unknown>>>>>(
  'TM_SHELL_DIALOGS_LOAD',
  {
    providedIn: 'root',
    factory: () => ({
      about: () => import('../dialogs/tm-about-dialog').then((chunk) => chunk.TmAboutDialog),
      shortcuts: () => import('../dialogs/tm-shortcuts-dialog').then((chunk) => chunk.TmShortcutsDialog),
    }),
  },
);

/**
 * Each dialog's height, which its placeholder takes so nothing moves when it
 * arrives. About's is its About tab's content (22rem) and the tab strip above
 * it: the 34 px strip, its 1 px border and the panel's 12 px.
 */
const DIALOG_HEIGHTS: Readonly<Record<TmShellDialog, string>> = { about: '25rem', shortcuts: '30rem' };

/** A slot panel's loaded component. */
async function slotComponent(slot: TmShellSlot): Promise<Type<unknown>> {
  const loaded = await slot.panel();
  return 'default' in loaded ? loaded.default : loaded;
}

/**
 * The host of the tenant shell's overlays: the session menu, the marker
 * popover, the slot popover, and the drawers and dialogs it opens through
 * TmModal. The app bar creates it once, beside its buttons.
 */
@Component({
  selector: 'tm-session-panels',
  imports: [NgComponentOutlet, RouterLink, TmPopover, TmPopoverContent, TmSessionMenu, TmSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The popovers' panels live in the overlay container and are tm-popover's
  // markup, so their widths need unencapsulated rules; every selector is
  // scoped by a tm-session-panels class.
  encapsulation: ViewEncapsulation.None,
  // Out of the bar's flow: the host and its children render nothing in place
  // (the menu and the popovers open in the overlay container), so they must
  // take no flex item's gap when the chunk lands, which would push the
  // session button aside after the first frame.
  host: { style: 'position: absolute' },
  template: `
    <!-- The session menu; its rows ask for the dialogs and the slots' drawers. -->
    <tm-session-menu (dialog)="openDialog($event)" (slot)="toggleSlot($event, null)" />
    <!-- The marker popover: the open marker's message. -->
    <tm-popover
      #markerPopover
      align="end"
      panelClass="tm-session-panels__marker"
      [aria-label]="markerLabel()"
      (closed)="markerAnchor.set(null)"
    >
      <ng-template tmPopoverContent>
        @switch (marker()) {
          @case ('sandbox') {
            <p class="tm-session-panels__message">{{ text('sandbox') }}</p>
            @if (live(); as live) {
              <a class="tm-session-panels__link" [routerLink]="live.href" (click)="markerPopover.close()">{{
                text('openLive', { name: live.name })
              }}</a>
            }
          }
          @case ('readOnly') {
            <p class="tm-session-panels__message">{{ text('readOnly', { tenant: tenantName() }) }}</p>
          }
          @case ('maintenance') {
            <p class="tm-session-panels__message">{{ text('maintenance', { tenant: tenantName() }) }}</p>
          }
        }
      </ng-template>
    </tm-popover>
    <!-- The slot popover, from lg up: the open slot's panel, a skeleton while it loads. -->
    <tm-popover #slotPopover size="md" align="end" [aria-label]="slotLabel()" (closed)="slotAnchor.set(null)">
      <ng-template tmPopoverContent>
        @if (slotPanel(); as panel) {
          <ng-container [ngComponentOutlet]="panel" />
        } @else {
          <tm-skeleton [lines]="4" />
        }
      </ng-template>
    </tm-popover>
  `,
  styles: `
    .tm-popover__panel.tm-session-panels__marker {
      --tm-popover-width: 280px;
    }

    .tm-session-panels__message {
      margin: 0;
    }

    .tm-session-panels__link {
      display: inline-block;
      margin-block-start: var(--space-2);
      color: var(--text-link);
      font-weight: var(--weight-semibold);
    }
  `,
})
export class TmSessionPanels implements TmShellPanels {
  readonly #l10n = inject(TmLocalization);
  readonly #urls = inject(TmUrls);
  readonly #cache = inject(TmLaunchCache);
  readonly #modal = inject(TmModal);
  readonly #shortcuts = inject(TmShortcuts);
  readonly #dialogs = inject(TM_SHELL_DIALOGS_LOAD);
  readonly #support = inject(TM_SHELL_SUPPORT);
  readonly #slots = inject(TmShellSlots).items;
  readonly #wide = inject(TmBreakpoints).atLeast('lg');
  readonly #identity = inject(TmShellIdentity);
  readonly #panels = new Map<string, Promise<Type<unknown>>>();
  readonly #triggers = new Map<HTMLElement, () => void>();
  readonly #modals = new Set<TmModalRef<void>>();
  readonly #slotPanel = signal<Type<unknown> | null>(null);
  readonly #slot = signal<TmShellSlot | null>(null);
  readonly #drawers = signal<ReadonlyMap<string, TmModalRef<void>>>(new Map());
  readonly #marker = signal<TmShellMarker | null>(null);
  /** The session menu. */
  private readonly menu = viewChild.required(TmSessionMenu);
  /** The marker popover. */
  private readonly markerPopover = viewChild.required<TmPopover>('markerPopover');
  /** The slot popover. */
  private readonly slotPopover = viewChild.required<TmPopover>('slotPopover');

  /** The marker whose popover is open, re-anchored on each open. */
  protected readonly marker = this.#marker.asReadonly();

  /** The button the marker popover is open at, or `null`. */
  protected readonly markerAnchor = signal<HTMLElement | null>(null);

  /** The button the slot popover is open at, or `null`. */
  protected readonly slotAnchor = signal<HTMLElement | null>(null);

  /** The open slot's loaded panel, or `null` while it loads. */
  protected readonly slotPanel = this.#slotPanel.asReadonly();

  /** The marker popover's name: the marker's label. */
  protected readonly markerLabel = computed(() => {
    const marker = this.#marker();
    return marker === null ? null : this.#l10n.ɵtext(`tm.tenantShell.bar.markers.${marker}`)();
  });

  /** The slot popover's name: the slot's label. */
  protected readonly slotLabel = computed(() => {
    const id = this.#slot()?.id;
    return this.#slots.find((item) => item.slot.id === id)?.label() ?? null;
  });

  /** The tenant's name, which the state markers' messages name. */
  protected readonly tenantName = computed(() => this.#identity.tenantName() ?? '');

  /** A sandbox's live tenant, when it is in the user's list: its name and home. */
  protected readonly live = computed(() => {
    const id = this.#identity.liveTenantId();
    const membership = id === null ? undefined : this.#cache.tenants()?.find((tenant) => tenant.id === id);
    if (id === null || membership === undefined) {
      return null;
    }
    return { name: tmTenantNames(membership, this.#l10n.language()).name.text, href: this.#urls.forTenant(id).home() };
  });

  /** Creates the host, whose buttons it releases and whose drawers and dialogs it closes at its end. */
  constructor() {
    tmRegisterShellOverlaysStrings(this.#l10n);
    inject(DestroyRef).onDestroy(() => {
      this.#triggers.forEach((release) => release());
      this.closeAll();
    });
  }

  /**
   * A string of the overlays' markers.
   *
   * @param key - The key under `tm.shellOverlays.markers`.
   * @param params - The message's parameters.
   * @returns The text.
   */
  protected text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.shellOverlays.markers.${key}`, params)();
  }

  /** {@inheritDoc TmShellPanels.toggleSession} */
  toggleSession(button: HTMLElement, initialFocus: 'first' | 'last'): void {
    this.#register(button, () => this.menu().register(button));
    this.menu().toggle(button, initialFocus);
  }

  /** {@inheritDoc TmShellPanels.toggleMarker} */
  toggleMarker(marker: TmShellMarker, button: HTMLElement): void {
    const popover = this.markerPopover();
    if (untracked(popover.isOpen) && untracked(this.markerAnchor) === button) {
      popover.close();
      return;
    }
    this.#register(button, () => popover.ɵregisterTrigger(button));
    this.#marker.set(marker);
    this.markerAnchor.set(button);
    popover.open(button);
  }

  /** {@inheritDoc TmShellPanels.preloadSlot} */
  preloadSlot(slot: TmShellSlot): void {
    void this.#panel(slot).catch(() => undefined);
  }

  /** {@inheritDoc TmShellPanels.toggleSlot} */
  toggleSlot(slot: TmShellSlot, button: HTMLElement | null): void {
    // --- From lg up, at its button: the popover ---
    const popover = this.slotPopover();
    if (button !== null && untracked(this.#wide)) {
      if (untracked(popover.isOpen) && untracked(this.slotAnchor) === button) {
        popover.close();
        return;
      }
      this.#register(button, () => popover.ɵregisterTrigger(button));
      this.#slot.set(slot);
      this.#slotPanel.set(null);
      this.slotAnchor.set(button);
      popover.open(button);
      this.#panel(slot).then(
        (component) => untracked(this.#slot) === slot && this.#slotPanel.set(component),
        // The skeleton stays; the next open tries again.
        () => undefined,
      );
      return;
    }
    // --- Below lg, or from the session menu: the end drawer ---
    const open = untracked(this.#drawers).get(slot.id);
    if (open !== undefined) {
      open.close();
      return;
    }
    const label = this.#slots.find((item) => item.slot.id === slot.id)?.label() ?? slot.id;
    const data: TmShellLazyContentData = { load: () => this.#panel(slot), height: '100%' };
    const ref = this.#track(this.#modal.open(TmShellLazyContent, { placement: 'end', title: label, data }));
    this.#drawers.update((drawers) => new Map(drawers).set(slot.id, ref));
    void ref.closed.then(() =>
      this.#drawers.update((drawers) => {
        const next = new Map(drawers);
        next.delete(slot.id);
        return next;
      }),
    );
  }

  /** {@inheritDoc TmShellPanels.openDialog} */
  openDialog(dialog: TmShellDialog): void {
    // The page's shortcuts are those registered as the dialog opens; About
    // takes the support tools, for its Diagnostics tab.
    const inputs = dialog === 'shortcuts' ? { registered: untracked(this.#shortcuts.active) } : { support: this.#support };
    const title =
      dialog === 'about' ? this.#l10n.ɵtext('tm.shellOverlays.dialogs.about')() : this.#l10n.ɵtext('tm.shellOverlays.dialogs.shortcuts')();
    const data: TmShellLazyContentData = { load: this.#dialogs[dialog], inputs, height: DIALOG_HEIGHTS[dialog] };
    this.#track(this.#modal.open(TmShellLazyContent, { size: 'md', title, data }));
  }

  /** {@inheritDoc TmShellPanels.isOpenFor} */
  isOpenFor(button: HTMLElement): boolean {
    return (
      this.menu().isOpenFor(button) ||
      (this.markerPopover().isOpen() && this.markerAnchor() === button) ||
      (this.slotPopover().isOpen() && this.slotAnchor() === button) ||
      [...this.#drawers().keys()].some((id) => button.dataset['tmSlot'] === id)
    );
  }

  /** {@inheritDoc TmShellPanels.controls} */
  controls(button: HTMLElement): string | null {
    return this.menu().controls(button);
  }

  /** {@inheritDoc TmShellPanels.closeAll} */
  closeAll(): void {
    this.menu().close();
    this.markerPopover().close();
    this.slotPopover().close();
    this.#modals.forEach((ref) => ref.close());
  }

  /** A slot's panel, loaded once; a failed load is tried again next time. */
  #panel(slot: TmShellSlot): Promise<Type<unknown>> {
    let panel = this.#panels.get(slot.id);
    if (panel === undefined) {
      panel = slotComponent(slot);
      this.#panels.set(slot.id, panel);
      panel.catch(() => this.#panels.delete(slot.id));
    }
    return panel;
  }

  /** Registers a button with its overlay once, so a press on it is the button's, not an outside press. */
  #register(button: HTMLElement, register: () => () => void): void {
    if (!this.#triggers.has(button)) {
      this.#triggers.set(button, register());
    }
  }

  /** Keeps a modal until it closes, so a tenant switch can close it. */
  #track(ref: TmModalRef<void>): TmModalRef<void> {
    this.#modals.add(ref);
    void ref.closed.then(() => this.#modals.delete(ref));
    return ref;
  }
}
