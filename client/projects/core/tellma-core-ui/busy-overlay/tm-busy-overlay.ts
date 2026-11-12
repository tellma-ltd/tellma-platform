// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ApplicationRef,
  computed,
  createComponent,
  DestroyRef,
  EnvironmentInjector,
  inject,
  Injectable,
  isSignal,
  type Signal,
  signal,
} from '@angular/core';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

import { TmBusyOverlayHost, type TmBusyOverlayState } from './internal/tm-busy-overlay-host';

/** The scrim delay when `--busy-overlay-delay` cannot be read, in milliseconds. */
const DEFAULT_DELAY_MS = 200;

/** One run in progress, and the label it asked for. */
interface BusyRun {
  readonly label: string | Signal<string> | undefined;
}

/** Parses a CSS `<time>` (`200ms`, `0.2s`) into milliseconds; `null` when it is not one. */
function parseCssTime(value: string): number | null {
  const match = /^(\d+(?:\.\d+)?|\.\d+)(ms|s)$/.exec(value.trim());
  if (match === null) {
    return null;
  }
  const amount = Number(match[1]);
  return match[2] === 's' ? amount * 1000 : amount;
}

/**
 * Blocks the page while a write runs that the user must wait for, such as
 * a save or a sign-out. A root service.
 *
 * `run(work)` blocks at once, in the same task: every child of `<body>`
 * goes `inert` (top-layer popovers and live regions stay readable, so
 * announcements are still heard), the keyboard is held, focus is set aside and the cursor
 * turns to `progress`, with nothing drawn yet. After `--busy-overlay-delay`
 * (200 ms) a scrim veils the page with a spinner and the label, which is
 * announced as it changes. When the last run settles, everything is undone
 * and focus returns, before the caller's code continues. A caller whose
 * own trigger shows it busy in the click's frame gets no flash on a fast
 * write; a caller with no visible trigger passes `immediate: true` to show
 * the scrim at once.
 *
 * Runs nest and share the one overlay; the latest run's label shows.
 * Escape and every other key do nothing while the overlay is up.
 *
 * @example
 * ```ts
 * const busy = inject(TmBusyOverlay);
 * await busy.run(save(), { label: 'Saving…' });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmBusyOverlay {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The runs in progress, oldest first. */
  private readonly runs = signal<readonly BusyRun[]>([]);

  /** Whether a run is in progress. */
  readonly active: Signal<boolean> = computed(() => this.runs().length > 0);

  /** Whether the scrim, spinner and label show. */
  private readonly scrim = signal(false);

  /** The status region has been in the page for a task, so a change to it is announced. */
  private readonly ready = signal(false);

  /** "Please wait". */
  private readonly defaultLabel = this.translate('busyOverlay.label');

  /** The latest run's label, read live; the default when it gave none. */
  private readonly label = computed(() => {
    const runs = this.runs();
    const label = runs.length === 0 ? undefined : runs[runs.length - 1].label;
    if (label === undefined) {
      return this.defaultLabel();
    }
    return isSignal(label) ? label() : label;
  });

  /** The host element. */
  private readonly element: HTMLElement;

  /** The `<body>` children this overlay made inert (never ones that already were). */
  private readonly inerted = new Set<Element>();

  /** The element focus is returned to when the last run settles. */
  private origin: HTMLElement | null = null;

  /** Shows the scrim after the delay. */
  private scrimTimer: ReturnType<typeof setTimeout> | undefined;

  /** Makes children added to `<body>` while blocked inert, and keeps the host on top. */
  private bodyObserver: MutationObserver | null = null;

  /** Holds every key while blocked: nothing on the page may act on one. */
  private readonly holdKey = (event: KeyboardEvent): void => {
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  constructor() {
    // The host exists from the start, so its status region is in the page
    // well before it has anything to say.
    const environmentInjector = inject(EnvironmentInjector);
    const appRef = inject(ApplicationRef);
    const state: TmBusyOverlayState = {
      active: this.active,
      scrim: this.scrim.asReadonly(),
      label: this.label,
      ready: this.ready.asReadonly(),
    };
    const host = createComponent(TmBusyOverlayHost, { environmentInjector });
    host.setInput('state', state);
    appRef.attachView(host.hostView);
    this.element = host.location.nativeElement as HTMLElement;
    document.body.appendChild(this.element);
    host.changeDetectorRef.detectChanges();
    this.element.showPopover();
    const readyTimer = setTimeout(() => this.ready.set(true));
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(readyTimer);
      this.unblock();
      host.destroy();
      this.element.remove();
    });
  }

  /**
   * Blocks the page until `work` settles and resolves (or rejects) as it
   * does, once the page is unblocked. `label` (a string, or a signal read
   * live) replaces "Please wait"; `immediate` shows the scrim, spinner and
   * label at once instead of after the delay.
   */
  run<T>(
    work: Promise<T>,
    options?: { readonly label?: string | Signal<string>; readonly immediate?: boolean },
  ): Promise<T> {
    const entry: BusyRun = { label: options?.label };
    if (this.runs().length === 0) {
      this.block();
    }
    this.runs.set([...this.runs(), entry]);
    if (options?.immediate === true) {
      clearTimeout(this.scrimTimer);
      this.scrim.set(true);
    } else if (!this.scrim() && this.scrimTimer === undefined) {
      this.scrimTimer = setTimeout(() => this.scrim.set(true), this.delay());
    }
    return Promise.resolve(work).then(
      (value) => {
        this.settle(entry);
        return value;
      },
      (error: unknown) => {
        this.settle(entry);
        throw error;
      },
    );
  }

  /** Ends one run; the last one unblocks the page. */
  private settle(entry: BusyRun): void {
    const runs = this.runs().filter((candidate) => candidate !== entry);
    this.runs.set(runs);
    if (runs.length === 0) {
      this.unblock();
    }
  }

  /**
   * Blocks the page in the calling task: sets focus aside, makes every
   * other child of `<body>` inert, raises the host and holds the keyboard.
   */
  private block(): void {
    const active = document.activeElement;
    this.origin = active instanceof HTMLElement && active !== document.body ? active : null;
    this.origin?.blur();
    for (const child of Array.from(document.body.children)) {
      this.makeInert(child);
    }
    this.bodyObserver = new MutationObserver((records) => {
      for (const record of records) {
        record.addedNodes.forEach((node) => {
          if (node instanceof Element) {
            this.makeInert(node);
          }
        });
      }
      this.raise();
    });
    this.bodyObserver.observe(document.body, { childList: true });
    this.raise();
    window.addEventListener('keydown', this.holdKey, true);
    window.addEventListener('keyup', this.holdKey, true);
    window.addEventListener('keypress', this.holdKey, true);
  }

  /** Undoes {@link block}, then returns focus to where it was. */
  private unblock(): void {
    clearTimeout(this.scrimTimer);
    this.scrimTimer = undefined;
    this.scrim.set(false);
    this.bodyObserver?.disconnect();
    this.bodyObserver = null;
    window.removeEventListener('keydown', this.holdKey, true);
    window.removeEventListener('keyup', this.holdKey, true);
    window.removeEventListener('keypress', this.holdKey, true);
    for (const element of this.inerted) {
      (element as HTMLElement).inert = false;
    }
    this.inerted.clear();
    const origin = this.origin;
    this.origin = null;
    if (origin?.isConnected === true) {
      origin.focus({ preventScroll: true });
    }
  }

  /**
   * Makes one `<body>` child inert, unless it is the host, already inert,
   * or a top-layer popover or live region. Those stay in the accessibility
   * tree, so a toast or an announcement made during the run is heard; the
   * raised host still takes the pointer and the window still holds the keys.
   */
  private makeInert(element: Element): void {
    if (
      element === this.element ||
      !(element instanceof HTMLElement) ||
      element.inert ||
      element.hasAttribute('popover') ||
      element.hasAttribute('aria-live')
    ) {
      return;
    }
    element.inert = true;
    this.inerted.add(element);
  }

  /** Re-shows the host so it sits above everything else in the top layer. */
  private raise(): void {
    if (this.element.matches(':popover-open')) {
      this.element.hidePopover();
      this.element.showPopover();
    }
  }

  /** The scrim delay: `--busy-overlay-delay` on the host, else 200 ms. */
  private delay(): number {
    const token = getComputedStyle(this.element).getPropertyValue('--busy-overlay-delay');
    return parseCssTime(token) ?? DEFAULT_DELAY_MS;
  }
}
