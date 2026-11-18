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
import {
  ɵtmKeepAboveModals,
  ɵtmOpenModals,
  ɵtmRaiseTopLayer,
  ɵtmWatchModals,
} from '@tellma/core-ui/overlay';

import { TmBusyOverlayHost, type TmBusyOverlayState } from './internal/tm-busy-overlay-host';

/** The scrim delay when `--busy-overlay-delay` cannot be read, in milliseconds. */
const DEFAULT_DELAY_MS = 200;

/** One run in progress, the label it asked for and the modals open when it began. */
interface BusyRun {
  /** The label the run asked for; the default when undefined. */
  readonly label: string | Signal<string> | undefined;
  /** The modals' hosts open when the run began, which it blocks with the page. */
  readonly modals: ReadonlySet<HTMLElement>;
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

/** The focused element, or `null` when focus is on nothing (the body). */
function focusedElement(): HTMLElement | null {
  const active = document.activeElement;
  return active instanceof HTMLElement && active !== document.body ? active : null;
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
 * Escape and every other key do nothing while the overlay is up, and the
 * overlay stays above the toasts and every modal.
 *
 * A modal opened during a run, such as a confirmation the write asks for,
 * must be answered before the run can end, so the overlay stands aside for
 * it: the page is unblocked and the modal takes focus. When the modal
 * closes with the run still in progress, the page is blocked again and the
 * scrim shows at once, since nothing on the page shows the work any more.
 * A run started from inside that modal blocks the modal too. If the last
 * run settles while such a modal is open, focus stays in the modal.
 *
 * @tmGroup overlay
 * @tmA11yNotes No tab stop: the page behind goes `inert` and keys are held
 *   until the work settles, then focus returns to where it was. The label
 *   is a `status` that is announced as it changes. A modal opened during a
 *   run is answered as usual: the overlay stands aside until it closes.
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

  /** Whether the page is blocked: a run is in progress and no modal it yields to is open. */
  private readonly blocked = signal(false);

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

  /** The modals open when the first run began: the ones the work did not open. */
  private baseModals: ReadonlySet<HTMLElement> = new Set();

  /**
   * The element focused when the page was last blocked, given focus back
   * when the overlay stands aside again, if it is inside an open modal.
   */
  private inner: HTMLElement | null = null;

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
      active: this.blocked.asReadonly(),
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

    // --- The page-wide layers: on top while blocking, standing aside for a new modal ---
    const releaseLayer = ɵtmKeepAboveModals(this.element, 2, () => this.blocked());
    const stopWatching = ɵtmWatchModals(() => this.modalsChanged());
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(readyTimer);
      stopWatching();
      releaseLayer();
      this.release();
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
    const entry: BusyRun = { label: options?.label, modals: new Set(ɵtmOpenModals()) };
    if (this.runs().length === 0) {
      this.origin = focusedElement();
      this.baseModals = entry.modals;
    }
    this.runs.set([...this.runs(), entry]);
    this.sync();
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

  /** Ends one run; the last one unblocks the page and returns focus. */
  private settle(entry: BusyRun): void {
    const runs = this.runs().filter((candidate) => candidate !== entry);
    this.runs.set(runs);
    if (runs.length > 0) {
      this.sync();
      return;
    }
    this.release();
    // A modal the work opened is still open: focus stays in it, or goes
    // back to where it was in it. Otherwise focus returns to where it was
    // when the first run began.
    const opened = ɵtmOpenModals().some((modal) => !this.baseModals.has(modal));
    const back = opened ? this.inModal(this.inner) : this.origin;
    this.origin = null;
    this.inner = null;
    this.baseModals = new Set();
    if (back?.isConnected === true) {
      back.focus({ preventScroll: true });
    }
  }

  /**
   * A modal opened or closed. A modal opened during the latest run makes
   * the overlay stand aside; when the last such modal closes with a run
   * still in progress, the page is blocked again with the scrim at once,
   * since the trigger that showed the work is gone.
   */
  private modalsChanged(): void {
    const wasBlocked = this.blocked();
    this.sync();
    if (!wasBlocked && this.blocked()) {
      clearTimeout(this.scrimTimer);
      this.scrim.set(true);
    }
  }

  /**
   * Blocks or unblocks the page to match the runs and the modals: blocked
   * while a run is in progress, unless a modal opened since the latest run
   * began is open.
   */
  private sync(): void {
    const runs = this.runs();
    const latest = runs.length === 0 ? undefined : runs[runs.length - 1];
    const blocking =
      latest !== undefined && !ɵtmOpenModals().some((modal) => !latest.modals.has(modal));
    if (blocking === this.blocked()) {
      return;
    }
    if (blocking) {
      this.block();
      return;
    }
    // Standing aside for a modal: focus goes back to where it was inside a
    // modal; a modal that just opened takes focus itself.
    this.release();
    const back = this.inModal(this.inner);
    this.inner = null;
    back?.focus({ preventScroll: true });
  }

  /**
   * Blocks the page in the calling task: sets focus aside, makes every
   * other child of `<body>` inert, raises the host and holds the keyboard.
   */
  private block(): void {
    this.blocked.set(true);
    this.inner = focusedElement();
    this.inner?.blur();
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
      ɵtmRaiseTopLayer();
    });
    this.bodyObserver.observe(document.body, { childList: true });
    ɵtmRaiseTopLayer();
    window.addEventListener('keydown', this.holdKey, true);
    window.addEventListener('keyup', this.holdKey, true);
    window.addEventListener('keypress', this.holdKey, true);
  }

  /** Undoes {@link block} and hides the scrim; placing focus is the caller's. */
  private release(): void {
    clearTimeout(this.scrimTimer);
    this.scrimTimer = undefined;
    this.scrim.set(false);
    this.blocked.set(false);
    this.bodyObserver?.disconnect();
    this.bodyObserver = null;
    window.removeEventListener('keydown', this.holdKey, true);
    window.removeEventListener('keyup', this.holdKey, true);
    window.removeEventListener('keypress', this.holdKey, true);
    for (const element of this.inerted) {
      (element as HTMLElement).inert = false;
    }
    this.inerted.clear();
  }

  /** `element` when it is still in the page, inside an open modal; else `null`. */
  private inModal(element: HTMLElement | null): HTMLElement | null {
    return element?.isConnected === true &&
      ɵtmOpenModals().some((modal) => modal.contains(element))
      ? element
      : null;
  }

  /**
   * Makes one `<body>` child inert, unless it is the host, already inert,
   * or a top-layer popover or live region. Those stay in the accessibility
   * tree, so a toast or an announcement made during the run is heard; the
   * host, raised above them, still takes the pointer and the window still
   * holds the keys.
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

  /** The scrim delay: `--busy-overlay-delay` on the host, else 200 ms. */
  private delay(): number {
    const token = getComputedStyle(this.element).getPropertyValue('--busy-overlay-delay');
    return parseCssTime(token) ?? DEFAULT_DELAY_MS;
  }
}
