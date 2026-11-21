// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';

/** The tone of a toast: its icon and, for `error`, the assertive announcement. */
export type TmToastTone = 'neutral' | 'accent' | 'success' | 'warning' | 'error';

/**
 * A toast. All text arrives localized by the caller.
 */
export interface TmToast {
  /** The tone: the icon, and whether the announcement is assertive (`error`). */
  readonly tone: TmToastTone;
  /** The first line, in bold. */
  readonly title: string;
  /** A second, quieter line. */
  readonly detail?: string;
  /** An icon by registry name; unset, the tone's own. */
  readonly icon?: string;
  /** A button on the toast: it runs, then the toast closes. */
  readonly action?: { readonly label: string; readonly run: () => void };
  /**
   * How long the toast shows, in milliseconds, or `'sticky'` to stay until
   * closed. Default 6000; the `error` tone defaults to `'sticky'`.
   */
  readonly duration?: number | 'sticky';
  /** A `show` with the key of an open toast replaces that toast in place. */
  readonly key?: string;
}

/** One open toast, as the host draws it. */
export interface TmToastEntry {
  /** Stable while the toast is open, through replacements and updates. */
  readonly id: number;
  /** What the toast shows now. */
  readonly toast: TmToast;
}

/** The overflow pill: a label, and what a click on it does (none: plain text). */
export interface TmToastPill {
  /** The pill's text ("2 more"). */
  readonly label: string;
  /** Runs on a click; unset, the pill is plain text. */
  readonly run?: () => void;
}

/** What the toast host reads from, and reports to, the toasts service. */
export interface TmToastController {
  /** The toasts on show, newest first. */
  readonly visible: Signal<readonly TmToastEntry[]>;
  /** The overflow pill, while toasts are hidden. */
  readonly pill: Signal<TmToastPill | null>;
  /** The polite live region's text. */
  readonly politeText: Signal<string>;
  /** The assertive live region's text. */
  readonly assertiveText: Signal<string>;
  /** The close button or Escape closed a toast. */
  dismiss(id: number): void;
  /** The toast's action button was clicked. */
  runAction(id: number): void;
  /** The overflow pill was clicked. */
  runPill(): void;
  /** The pointer entered or left the stack, or focus entered or left it. */
  setPaused(source: 'hover' | 'focus', paused: boolean): void;
}
