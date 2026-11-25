// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, Injector, input, type OnInit, runInInjectionContext, type Signal, signal } from '@angular/core';
import { NavigationEnd, NavigationStart, Router } from '@angular/router';
import { TmLocalization, TmTelemetry } from '@tellma/core';
import { TmIcon } from '@tellma/core-ui/icon';

// Back and forward in installed mode (spec 0027 §7.3), a chunk the app bar
// loads only once the app runs installed. The bar reserves the buttons'
// boxes until it lands, so nothing moves. The bar passes in the Navigation
// API that installed mode reads, so this chunk shares no module with the
// first frame's.
//
// Whether each way is open comes from the Navigation API where the browser
// has it. Without it (Safari before 26.2) the router's page id stands in for
// the spec's depth counter: the router writes it into every history entry
// (`canceledNavigationResolution: 'computed'`), adding 1 on a push and
// keeping it on a replace, so the entry's id is how far Back can go, and the
// highest id since the last push is how far Forward can. A reload forgets
// the highest id, as the spec's counter forgets its count.

/** The scope of the history buttons' strings under `tm`. */
const SCOPE = 'shellHistory';

/** The history buttons' English strings, under `tm.shellHistory`. */
export const TM_SHELL_HISTORY_STRINGS_EN = {
  /** The buttons' names. */
  back: 'Back',
  forward: 'Forward',
} as const;

/** The router's page id in a history entry's state. */
function pageId(state: unknown): number {
  const id = (state as { readonly ɵrouterPageId?: unknown } | null)?.ɵrouterPageId;
  return typeof id === 'number' ? id : 0;
}

/**
 * Leaves a history move to the router, which follows it on `popstate`. A
 * move with no entry that way, or one a newer navigation interrupts,
 * rejects both of its promises; neither is an error here.
 */
function settle(result: NavigationResult): void {
  result.committed?.catch(() => undefined);
  result.finished?.catch(() => undefined);
}

/**
 * Whether history can move back or forward from the current entry, and the
 * moves themselves: what the installed-mode Back and Forward buttons read.
 * Created by the buttons, in their injection context, for as long as they
 * show.
 */
class TmInstalledHistory {
  readonly #navigation: Navigation | null;
  readonly #location = inject(Location);
  readonly #canGoBack = signal(false);
  readonly #canGoForward = signal(false);

  /** Whether Back has an entry to go to. */
  readonly canGoBack: Signal<boolean> = this.#canGoBack.asReadonly();

  /** Whether Forward has an entry to go to. */
  readonly canGoForward: Signal<boolean> = this.#canGoForward.asReadonly();

  /**
   * Creates the tracker, which follows the history entries until the buttons end.
   * @param navigation - The Navigation API, or `null` where the browser lacks it.
   */
  constructor(navigation: Navigation | null) {
    this.#navigation = navigation;
    const destroyRef = inject(DestroyRef);

    // --- With the Navigation API: the browser's own answers, re-read on every entry change ---
    if (navigation !== null) {
      const read = (): void => {
        this.#canGoBack.set(navigation.canGoBack);
        this.#canGoForward.set(navigation.canGoForward);
      };
      read();
      navigation.addEventListener('currententrychange', read);
      destroyRef.onDestroy(() => navigation.removeEventListener('currententrychange', read));
      return;
    }

    // --- Without it: the router's page ids ---
    // A push raises the highest id to the new entry's; a history move keeps it.
    let highest = pageId(this.#location.getState());
    let popped = false;
    const subscription = inject(Router).events.subscribe((event) => {
      if (event instanceof NavigationStart) {
        popped = event.navigationTrigger === 'popstate';
      } else if (event instanceof NavigationEnd) {
        const current = pageId(this.#location.getState());
        highest = popped ? Math.max(highest, current) : current;
        this.#canGoBack.set(current > 0);
        this.#canGoForward.set(current < highest);
      }
    });
    this.#canGoBack.set(highest > 0);
    destroyRef.onDestroy(() => subscription.unsubscribe());
  }

  /** Moves one entry back. */
  back(): void {
    const navigation = this.#navigation;
    if (navigation === null) {
      this.#location.back();
    } else {
      settle(navigation.back());
    }
  }

  /** Moves one entry forward. */
  forward(): void {
    const navigation = this.#navigation;
    if (navigation === null) {
      this.#location.forward();
    } else {
      settle(navigation.forward());
    }
  }
}

/**
 * The installed-mode Back and Forward buttons, which the app bar renders in
 * the box it reserves for them while the app runs installed.
 */
@Component({
  selector: 'tm-shell-history',
  imports: [TmIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Back and Forward, each disabled while history has no entry that way. -->
    <button type="button" class="tm-shell-history__button" [disabled]="!history.canGoBack()" [attr.aria-label]="backLabel()" (click)="back()">
      <tm-icon name="arrow-left" />
    </button>
    <button
      type="button"
      class="tm-shell-history__button"
      [disabled]="!history.canGoForward()"
      [attr.aria-label]="forwardLabel()"
      (click)="forward()"
    >
      <tm-icon name="arrow-right" />
    </button>
  `,
  styles: `
    /* The app bar's 30 px icon buttons, on its ink. */
    :host {
      display: contents;
    }

    .tm-shell-history__button {
      display: inline-flex;
      flex: none;
      align-items: center;
      justify-content: center;
      inline-size: 30px;
      block-size: 30px;
      padding: 0;
      border: 0;
      border-radius: var(--radius-md);
      background: transparent;
      color: inherit;
      cursor: pointer;
    }

    .tm-shell-history__button:disabled {
      cursor: default;
      opacity: 0.4;
    }

    .tm-shell-history__button:not(:disabled):hover {
      background: rgb(255 255 255 / 0.1);
    }

    .tm-shell-history__button:focus-visible {
      outline: var(--focus-ring-width) solid var(--text-on-dark);
      outline-offset: 1px;
    }
  `,
})
export class TmShellHistory implements OnInit {
  readonly #l10n = inject(TmLocalization);
  readonly #telemetry = inject(TmTelemetry);
  readonly #injector = inject(Injector);

  /** The Navigation API, where the browser has it, or `null` to follow the router's page ids instead. */
  readonly navigation = input.required<Navigation | null>();

  /** Where history can move; created as the buttons start, before their first render. */
  protected history!: TmInstalledHistory;

  /** Back's name. */
  protected readonly backLabel = this.#l10n.ɵtext('tm.shellHistory.back');

  /** Forward's name. */
  protected readonly forwardLabel = this.#l10n.ɵtext('tm.shellHistory.forward');

  /** Creates the buttons, registering their strings. */
  constructor() {
    this.#l10n.ɵregisterStrings(SCOPE, TM_SHELL_HISTORY_STRINGS_EN);
  }

  /** Starts following history, once the input names the Navigation API. */
  ngOnInit(): void {
    this.history = runInInjectionContext(this.#injector, () => new TmInstalledHistory(this.navigation()));
  }

  /** Moves one entry back. */
  protected back(): void {
    this.#telemetry.event('tm.shell.navigated', { source: 'back' }, { sampled: true });
    this.history.back();
  }

  /** Moves one entry forward. */
  protected forward(): void {
    this.#telemetry.event('tm.shell.navigated', { source: 'forward' }, { sampled: true });
    this.history.forward();
  }
}
