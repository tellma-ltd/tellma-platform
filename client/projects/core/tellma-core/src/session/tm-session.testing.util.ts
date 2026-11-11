// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal, type Provider } from '@angular/core';

import { TM_NOW } from '../app/tm-now';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import type { TmBootResult } from '../boot/tm-boot-types';
import { TmTelemetry, type TmTelemetryItem, type TmTelemetryProps } from '../observe/tm-telemetry';
import { TmFakeStorage } from '../state/tm-storage.testing.util';
import { TM_SESSION_HOST, type TmSessionHost } from './tm-session-host';

// The session's browser, faked for the specs: a cookie jar the spec rewrites,
// an in-memory session storage, and an address bar that records where the
// tab was sent.

/** A faked browser behind the session. */
export class TmFakeSessionHost implements TmSessionHost {
  /** The cookie string the next read returns. */
  cookieJar = '';

  /** The tab's session storage. */
  readonly sessionStorage = new TmFakeStorage();

  /** The URLs `assign` went to. */
  readonly assigned: string[] = [];

  /** The URLs `replace` went to. */
  readonly replaced: string[] = [];

  /** How many times the page reloaded. */
  reloads = 0;

  /** The cookies written, in order. */
  readonly cookieWrites: string[] = [];

  /** The address bar. */
  readonly location = {
    pathname: '/17/centers',
    search: '?x=1',
    hash: '#d=abc',
    assign: (url: string): void => void this.assigned.push(url),
    replace: (url: string): void => void this.replaced.push(url),
    reload: (): void => void this.reloads++,
  };

  /** The document's language. */
  lang = 'ar';

  /** The cookie string now. */
  cookies(): string {
    return this.cookieJar;
  }

  /** Records a cookie write. */
  writeCookie(cookie: string): void {
    this.cookieWrites.push(cookie);
  }

  /** The document's language now. */
  language(): string {
    return this.lang;
  }
}

/** One recorded telemetry event: its name and properties. */
export interface TmRecordedEvent {
  /** The event's name. */
  readonly name: string;
  /** The event's properties. */
  readonly props: TmTelemetryProps | undefined;
}

/** Telemetry that records its events. */
@Injectable()
export class TmRecordingTelemetry extends TmTelemetry {
  /** The events, in order. */
  readonly events: TmRecordedEvent[] = [];

  /** Nothing is kept for the diagnostics view. */
  override readonly recent = signal<readonly TmTelemetryItem[]>([]).asReadonly();

  /** Records an event. */
  override event(name: string, props?: TmTelemetryProps): void {
    this.events.push({ name, props });
  }

  /** Ignores a timing. */
  override timing(): void {}

  /** Ignores an error. */
  override error(): void {}

  /** The number of events recorded at each flush, in order. */
  readonly flushes: number[] = [];

  /** Records a flush. */
  override flush(): void {
    this.flushes.push(this.events.length);
  }
}

/**
 * The providers of a session spec: the faked browser, a fixed clock, the
 * recording telemetry and the boot script's result.
 *
 * @param host - The faked browser.
 * @param boot - The boot script's result, or `null` for none.
 * @param now - The clock's time.
 * @returns The providers.
 */
export function tmSessionTestingProviders(host: TmFakeSessionHost, boot: Partial<TmBootResult> | null, now: number): Provider[] {
  return [
    { provide: TM_SESSION_HOST, useValue: host },
    { provide: TM_NOW, useValue: () => now },
    { provide: TmTelemetry, useClass: TmRecordingTelemetry },
    { provide: TM_BOOT_RESULT, useValue: boot },
  ];
}
