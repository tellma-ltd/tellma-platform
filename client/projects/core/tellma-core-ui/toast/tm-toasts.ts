// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { OverlayContainer } from '@angular/cdk/overlay';
import {
  ApplicationRef,
  type ComponentRef,
  computed,
  createComponent,
  DestroyRef,
  type EnvironmentProviders,
  EnvironmentInjector,
  ErrorHandler,
  inject,
  Injectable,
  InjectionToken,
  makeEnvironmentProviders,
  signal,
} from '@angular/core';

import { TM_NOW, TM_UI_TRANSLATE } from '@tellma/core-ui';

import { TmToastHost } from './internal/tm-toast-host';
import type {
  TmToast,
  TmToastController,
  TmToastEntry,
  TmToastPill,
} from './internal/tm-toast-model';

/** How a toast closed (the union `TmToastRef.closed` resolves with). */
type TmToastCloseReason = 'timeout' | 'dismiss' | 'action' | 'api' | 'replaced';

/** The default time a toast shows, in milliseconds. */
const DEFAULT_DURATION_MS = 6000;

/** The pause between emptying a live region and filling it, so the change is announced. */
const ANNOUNCE_DELAY_MS = 100;

/** A handle on one shown toast. */
export interface TmToastRef {
  /** Closes the toast (`closed` resolves `'api'`). Does nothing once it is closed or replaced. */
  close(): void;
  /**
   * Changes the toast in place and restarts its timer. A changed title or
   * detail is announced. Does nothing once the toast is closed or replaced.
   */
  update(patch: Partial<TmToast>): void;
  /**
   * Resolves once, when the toast goes: on its timer (`'timeout'`), by the
   * close button or Escape (`'dismiss'`), after its action (`'action'`),
   * through `close` or `clear` (`'api'`), or when a `show` with its key
   * replaces it (`'replaced'`).
   */
  readonly closed: Promise<'timeout' | 'dismiss' | 'action' | 'api' | 'replaced'>;
}

/** The app-wide configuration of the toasts. */
export interface TmToastsOptions {
  /** How many toasts show at once; older ones hide behind the overflow pill. Default 3. */
  readonly maxVisible?: number;
  /**
   * The overflow pill for `hidden` hidden toasts, evaluated whenever the
   * count changes. Without `run` the pill is plain text; without this
   * option it reads "N more".
   */
  readonly overflow?: (hidden: number) => { readonly label: string; readonly run?: () => void };
}

/** The options `provideTellmaToasts` sets. */
const TM_TOASTS_OPTIONS = new InjectionToken<TmToastsOptions>('TM_TOASTS_OPTIONS', {
  providedIn: 'root',
  factory: () => ({}),
});

/**
 * Configures the toasts for the whole app: how many show at once and what
 * the overflow pill says and does. The toasts work with no configuration.
 *
 * @example
 * ```ts
 * bootstrapApplication(App, {
 *   providers: [
 *     provideTellmaToasts({
 *       maxVisible: 3,
 *       overflow: (hidden) => ({ label: `${hidden} more`, run: () => inbox.open() }),
 *     }),
 *   ],
 * });
 * ```
 */
export function provideTellmaToasts(options: TmToastsOptions): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: TM_TOASTS_OPTIONS, useValue: options }]);
}

/** The service's private view of one open toast. */
interface OpenToast {
  readonly id: number;
  toast: TmToast;
  /** The live handle; a replacement swaps it for a new one. */
  ref: ToastRef;
  /** Milliseconds left on the timer; `null` for a sticky toast. */
  remaining: number | null;
  /** When the running timer started (by `TM_NOW`), or `null` while it is stopped. */
  startedAt: number | null;
  /** The running timer. */
  timer: ReturnType<typeof setTimeout> | undefined;
}

/** A {@link TmToastRef}: inert once its toast has closed or been replaced. */
class ToastRef implements TmToastRef {
  /** Whether the handle still controls its toast. */
  live = true;
  readonly closed: Promise<TmToastCloseReason>;
  private resolve!: (reason: TmToastCloseReason) => void;

  constructor(
    private readonly owner: TmToasts,
    private readonly id: number,
  ) {
    this.closed = new Promise((resolve) => (this.resolve = resolve));
  }

  close(): void {
    if (this.live) {
      this.owner.ɵclose(this.id, 'api');
    }
  }

  update(patch: Partial<TmToast>): void {
    if (this.live) {
      this.owner.ɵupdate(this.id, patch);
    }
  }

  /** Ends the handle with `reason`. */
  settle(reason: TmToastCloseReason): void {
    if (this.live) {
      this.live = false;
      this.resolve(reason);
    }
  }
}

/**
 * Short, timed messages in one top-layer stack, at the block-start,
 * inline-end corner of the page: "Saved", "Started in the background".
 * A root service that works with no configuration; `provideTellmaToasts`
 * sets how many show at once and what the overflow pill does.
 *
 * At most `maxVisible` toasts show, newest on top; older ones hide behind a
 * pill that counts them. Only the toasts on show run their timers, and
 * every timer pauses while the pointer is over the stack, while focus is
 * inside it and while the page is hidden; it resumes with the time it had
 * left. A toast's text is announced through a persistent live region when
 * it shows or is replaced (assertive for the `error` tone, polite
 * otherwise); the cards themselves are not live.
 *
 * A toast never takes focus by itself. `focus()` moves focus to the newest
 * one; Tab and Shift+Tab move between the toasts' buttons, and Escape
 * closes the focused toast and returns focus where it came from.
 *
 * @example
 * ```ts
 * inject(TmToasts).show({
 *   tone: 'accent',
 *   title: 'Started in the background.',
 *   key: `job:${jobId}`,
 *   action: { label: 'Track', run: () => router.navigateByUrl(`/jobs/${jobId}`) },
 * });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmToasts {
  private readonly options = inject(TM_TOASTS_OPTIONS);
  private readonly appRef = inject(ApplicationRef);
  private readonly environment = inject(EnvironmentInjector);
  private readonly errorHandler = inject(ErrorHandler);
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly now = inject(TM_NOW);
  private readonly overlayContainer = inject(OverlayContainer);

  /** The open toasts, oldest first. */
  private readonly open = signal<readonly OpenToast[]>([]);

  /** What the host draws of each toast; a new object only when the toast changes. */
  private readonly entries = computed<readonly TmToastEntry[]>(() =>
    this.open().map((item) => this.entryOf(item)),
  );

  /** The last entry handed out per toast, so an unchanged toast keeps its object. */
  private readonly entryCache = new Map<number, TmToastEntry>();

  /** How many toasts show at once. */
  private readonly maxVisible = Math.max(1, this.options.maxVisible ?? 3);

  /** The toasts on show, newest first. */
  private readonly visible = computed(() => this.entries().slice(-this.maxVisible).reverse());

  /** How many toasts hide behind the pill. */
  private readonly hidden = computed(() => Math.max(0, this.open().length - this.maxVisible));

  /** The overflow pill: the app's, else "N more". */
  private readonly pill = computed<TmToastPill | null>(() => {
    const hidden = this.hidden();
    if (hidden === 0) {
      return null;
    }
    return (
      this.options.overflow?.(hidden) ?? { label: this.translate('toast.more', { count: hidden })() }
    );
  });

  /** The live regions' text. */
  private readonly politeText = signal('');
  private readonly assertiveText = signal('');

  /** Announcements waiting for their region to be emptied first. */
  private readonly pending = { polite: [] as string[], assertive: [] as string[] };
  private readonly announceTimers: {
    polite?: ReturnType<typeof setTimeout>;
    assertive?: ReturnType<typeof setTimeout>;
  } = {};

  /** What pauses the timers: the pointer over the stack, focus in it, a hidden page. */
  private readonly pausedBy = { hover: false, focus: false };

  /** The next toast id. */
  private nextId = 1;

  /** The host, once the first toast has shown. */
  private host: ComponentRef<TmToastHost> | null = null;

  /** Re-raises the host when a modal opens over it. */
  private overlayObserver: MutationObserver | null = null;

  /** Set once the service is destroyed. */
  private destroyed = false;

  /** What the host reads and calls. */
  private readonly controller: TmToastController = {
    visible: this.visible,
    pill: this.pill,
    politeText: this.politeText.asReadonly(),
    assertiveText: this.assertiveText.asReadonly(),
    dismiss: (id) => this.ɵclose(id, 'dismiss'),
    runAction: (id) => this.runAction(id),
    runPill: () => this.runPill(),
    setPaused: (source, paused) => {
      this.pausedBy[source] = paused;
      this.reconcileTimers();
    },
  };

  /** Pauses and resumes the timers with the page's visibility. */
  private readonly onVisibilityChange = (): void => this.reconcileTimers();

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      for (const item of this.open()) {
        clearTimeout(item.timer);
      }
      clearTimeout(this.announceTimers.polite);
      clearTimeout(this.announceTimers.assertive);
      this.overlayObserver?.disconnect();
      document.removeEventListener('visibilitychange', this.onVisibilityChange);
      const element = this.host?.location.nativeElement as HTMLElement | undefined;
      this.host?.destroy();
      element?.remove();
    });
  }

  /**
   * Shows a toast and announces it. A toast whose `key` matches an open
   * toast replaces that toast in place (its handle resolves
   * `'replaced'`), with a fresh timer.
   */
  show(toast: TmToast): TmToastRef {
    const existing =
      toast.key === undefined ? undefined : this.open().find((item) => item.toast.key === toast.key);
    let ref: ToastRef;
    if (existing !== undefined) {
      // In place: the slot, the id and the card stay; the handle is new.
      existing.ref.settle('replaced');
      ref = new ToastRef(this, existing.id);
      clearTimeout(existing.timer);
      existing.toast = toast;
      existing.ref = ref;
      existing.remaining = durationOf(toast);
      existing.startedAt = null;
      existing.timer = undefined;
      this.open.set([...this.open()]);
    } else {
      const id = this.nextId++;
      ref = new ToastRef(this, id);
      const item: OpenToast = {
        id,
        toast,
        ref,
        remaining: durationOf(toast),
        startedAt: null,
        timer: undefined,
      };
      this.open.set([...this.open(), item]);
    }
    this.ensureHost();
    this.announce(toast);
    this.reconcileTimers();
    return ref;
  }

  /** Closes every toast (each handle resolves `'api'`). */
  clear(): void {
    for (const item of [...this.open()]) {
      this.ɵclose(item.id, 'api');
    }
  }

  /**
   * Moves focus to the newest toast's first button. Returns `false` when no
   * toast is open.
   */
  focus(): boolean {
    if (this.host === null || this.open().length === 0) {
      return false;
    }
    // A toast shown in this same task is drawn before it is focused.
    this.host.changeDetectorRef.detectChanges();
    return this.host.instance.focusNewest();
  }

  /**
   * Closes the toast `id` with `reason`.
   * @internal
   */
  ɵclose(id: number, reason: TmToastCloseReason): void {
    const item = this.open().find((candidate) => candidate.id === id);
    if (item === undefined) {
      return;
    }
    // Focus leaves the card before the card leaves the page.
    this.host?.instance.releaseFocus(id);
    clearTimeout(item.timer);
    this.entryCache.delete(id);
    this.open.set(this.open().filter((candidate) => candidate !== item));
    item.ref.settle(reason);
    this.reconcileTimers();
  }

  /**
   * Changes the toast `id` in place and restarts its timer.
   * @internal
   */
  ɵupdate(id: number, patch: Partial<TmToast>): void {
    const item = this.open().find((candidate) => candidate.id === id);
    if (item === undefined) {
      return;
    }
    const before = item.toast;
    const toast: TmToast = { ...before, ...patch };
    // Taking another open toast's key replaces that toast.
    if (toast.key !== undefined && toast.key !== before.key) {
      const other = this.open().find((candidate) => candidate !== item && candidate.toast.key === toast.key);
      if (other !== undefined) {
        this.ɵclose(other.id, 'replaced');
      }
    }
    clearTimeout(item.timer);
    item.toast = toast;
    item.remaining = durationOf(toast);
    item.startedAt = null;
    item.timer = undefined;
    this.open.set([...this.open()]);
    if (announcementOf(toast) !== announcementOf(before) || toast.tone !== before.tone) {
      this.announce(toast);
    }
    this.reconcileTimers();
  }

  /** Runs a toast's action, then closes it; a failing action is reported, not thrown. */
  private runAction(id: number): void {
    const item = this.open().find((candidate) => candidate.id === id);
    if (item === undefined) {
      return;
    }
    try {
      item.toast.action?.run();
    } catch (error) {
      this.errorHandler.handleError(error);
    } finally {
      this.ɵclose(id, 'action');
    }
  }

  /** Runs the overflow pill's `run`; a failure is reported, not thrown. */
  private runPill(): void {
    try {
      this.pill()?.run?.();
    } catch (error) {
      this.errorHandler.handleError(error);
    }
  }

  /** The host's view of a toast, reusing the last object while the toast is the same. */
  private entryOf(item: OpenToast): TmToastEntry {
    const cached = this.entryCache.get(item.id);
    if (cached?.toast === item.toast) {
      return cached;
    }
    const entry: TmToastEntry = { id: item.id, toast: item.toast };
    this.entryCache.set(item.id, entry);
    return entry;
  }

  /**
   * Starts the timers that should run and stops the rest. A timer runs
   * only for a toast on show while nothing pauses the stack; a stopped
   * timer keeps the time it had left.
   */
  private reconcileTimers(): void {
    if (this.destroyed) {
      return;
    }
    const open = this.open();
    const shown = new Set(open.slice(-this.maxVisible));
    const paused =
      this.pausedBy.hover ||
      this.pausedBy.focus ||
      (typeof document !== 'undefined' && document.visibilityState === 'hidden');
    const now = this.now();
    for (const item of open) {
      const run = item.remaining !== null && shown.has(item) && !paused;
      if (run && item.startedAt === null) {
        item.startedAt = now;
        const id = item.id;
        item.timer = setTimeout(() => this.ɵclose(id, 'timeout'), item.remaining!);
      } else if (!run && item.startedAt !== null) {
        clearTimeout(item.timer);
        item.timer = undefined;
        item.remaining = Math.max(0, item.remaining! - (now - item.startedAt));
        item.startedAt = null;
      }
    }
  }

  /**
   * Queues "title. detail" for the live region of the toast's tone. The
   * region is emptied at once and filled a moment later, so a repeated
   * message is announced again; messages arriving in that moment are read
   * together.
   */
  private announce(toast: TmToast): void {
    const region = toast.tone === 'error' ? 'assertive' : 'polite';
    const text = region === 'assertive' ? this.assertiveText : this.politeText;
    this.pending[region].push(announcementOf(toast));
    text.set('');
    clearTimeout(this.announceTimers[region]);
    this.announceTimers[region] = setTimeout(() => {
      text.set(this.pending[region].join(' '));
      this.pending[region] = [];
    }, ANNOUNCE_DELAY_MS);
  }

  /**
   * Creates the host on first use: a `popover="manual"` element on
   * `<body>`, shown once and kept open, re-raised above any modal that
   * opens after it, and timers that follow the page's visibility.
   */
  private ensureHost(): void {
    if (this.host !== null) {
      return;
    }
    const host = createComponent(TmToastHost, { environmentInjector: this.environment });
    host.setInput('controller', this.controller);
    this.appRef.attachView(host.hostView);
    const element = host.location.nativeElement as HTMLElement;
    document.body.appendChild(element);
    host.changeDetectorRef.detectChanges();
    element.showPopover();
    this.host = host;
    document.addEventListener('visibilitychange', this.onVisibilityChange);
    // Modals open in the overlay container as top-layer popovers too, and
    // the latest shown sits on top: re-showing the host keeps the toasts
    // above every modal.
    this.overlayObserver = new MutationObserver(() => this.raise());
    this.overlayObserver.observe(this.overlayContainer.getContainerElement(), { childList: true });
  }

  /** Re-shows the host at the top of the top layer, keeping focus where it was. */
  private raise(): void {
    const element = this.host?.location.nativeElement as HTMLElement | undefined;
    if (element === undefined || !element.matches(':popover-open')) {
      return;
    }
    const active = document.activeElement;
    element.hidePopover();
    element.showPopover();
    if (active instanceof HTMLElement && document.activeElement !== active && active.isConnected) {
      active.focus({ preventScroll: true });
    }
  }
}

/** A toast's duration: its own, else sticky for `error` and six seconds for the rest. */
function durationOf(toast: TmToast): number | null {
  const duration = toast.duration ?? (toast.tone === 'error' ? 'sticky' : DEFAULT_DURATION_MS);
  return duration === 'sticky' ? null : Math.max(0, duration);
}

/**
 * What a toast announces: "title. detail", without a second stop when the
 * title already ends in one.
 */
function announcementOf(toast: TmToast): string {
  if (toast.detail === undefined || toast.detail === '') {
    return toast.title;
  }
  const stop = /[.!?…؟]$/u.test(toast.title.trimEnd()) ? ' ' : '. ';
  return `${toast.title.trimEnd()}${stop}${toast.detail}`;
}
