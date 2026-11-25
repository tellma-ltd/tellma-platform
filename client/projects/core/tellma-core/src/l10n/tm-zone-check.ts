// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, effect, inject, Injectable, Injector, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { tmFormatZoneName, tmFormatZoneOffset, ɵtmZoneOffset } from '@tellma/core-ui/l10n';
import { ɵtmOpenModals, ɵtmWatchModals } from '@tellma/core-ui/overlay';
import { TmToasts } from '@tellma/core-ui/toast';

import { TmApi } from '../api/tm-api';
import { TmApiError } from '../api/tm-api-error';
import { TM_API_HOST } from '../api/tm-api-host';
import type { TmStringTree } from '../app/tm-app-config';
import { TM_NOW } from '../app/tm-now';
import { TM_TOAST_FEEDBACK_MS } from '../attention/tm-toast-durations';
import { TmInputIdle } from '../attention/tm-input-idle';
import { TmEntities } from '../entity/tm-entities';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import type { TmMeLocaleResult } from '../state/launch/tm-launch-docs';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmCacheRefresher } from '../state/sync/tm-cache-refresher';
import { TmLocks } from '../state/sync/tm-locks';
import { TmUrls } from '../urls/tm-urls';
import { TmDeviceZone, type TmZoneDoc } from './tm-device-zone';
import { TmLocalization } from './tm-localization';
import { TM_ZONE_PARTITION, ɵtmZoneDocWith, ɵtmZoneUser } from './tm-zone-doc';
import type { TmZoneAnswer } from './tm-zone-prompt';

// The device-zone check (spec 0024 §7.8), a chunk the app loads at idle
// after the first render. It checks now, on each tenant entry and whenever
// the tab comes into view, in a visible tab of a tenant whose launch
// documents are present, once the Entities chunk, which carries the zone
// aliases, has loaded. The steps, against the device zone this tenant last
// handled (`seen`):
//
// 1. none yet: record the device zone, as a new device or tenant never asks;
// 2. no change since: stop;
// 3. the device zone is the display zone already: record;
// 4. a frozen tenant, which refuses the write: record;
// 5. the option on: the automatic update, then the toast;
// 6. the same offset now, so nothing visible would change: record;
// 7. the prompt, in one tab of the user's: once the user's hands are off the
//    keyboard and pointer and no modal is open, the steps run again, and
//    the dialog asks. The answer is recorded; a tab closed first records
//    nothing, so the next check asks again.
//
// The automatic update and the prompt both run under the user's
// `zone-prompt` lock, taken only if no tab holds it: two visible tabs never
// write twice or ask twice. A tab that finds the lock taken waits for it
// once, in the background, and then checks again: the holder may give up
// without asking, as when it turns hidden before the user is idle. Telemetry carries the step and the answer,
// never a zone or an offset, since either tells where the user is.

/** The device-zone check's English strings, under `tm.zone`. */
export const TM_ZONE_STRINGS_EN = {
  changed: 'Times now show in {zone} ({offset}).',
  change: 'Change',
} as const satisfies TmStringTree;

/** The route of the write. */
const ROUTE = 'users/me/locale';

/** The validation code of a zone outside the server's set. */
const ZONE_UNKNOWN = 'Users.TimeZoneUnknown';

/** The store's device partition, where the zone document lives. */
const DEVICE = TM_ZONE_PARTITION;

/** The zone document's name in the device partition. */
const ZONE = 'zone';

/** What one check found, for the tenant it runs in. */
interface TmZoneContext {
  /** The user's subject identifier. */
  readonly sub: string;
  /** The tenant's id. */
  readonly tenantId: number;
  /** Whether the tenant is frozen, so it refuses the write. */
  readonly frozen: boolean;
  /** The device's zone, through the aliases. */
  readonly device: string;
  /** The display zone, through the aliases. */
  readonly display: string;
  /** The device zone this tenant last handled, if any. */
  readonly seen: string | undefined;
  /** Whether the option is on for the user on this device. */
  readonly auto: boolean;
  /** Now, in epoch milliseconds. */
  readonly at: number;
}

/** A step's outcome: stop, record (with the step's telemetry action), update automatically, or ask. */
type TmZoneStep = 'stop' | 'first-run' | 'matches' | 'frozen' | 'same-offset' | 'auto' | 'prompt';

/**
 * Whether two zones show the same wall clock at an instant; a zone the
 * browser cannot format shows nothing alike.
 *
 * @param at - The instant, in epoch milliseconds.
 * @param a - One zone.
 * @param b - The other.
 * @returns Whether their offsets are equal then.
 */
function sameOffset(at: number, a: string, b: string): boolean {
  try {
    return ɵtmZoneOffset(at, a) === ɵtmZoneOffset(at, b);
  } catch {
    return false;
  }
}

/**
 * The steps before the prompt, in order, over one check's facts.
 *
 * @param context - The facts.
 * @returns What the check does.
 */
function stepOf(context: TmZoneContext): TmZoneStep {
  if (context.seen === undefined) {
    return 'first-run';
  }
  if (context.device === context.seen) {
    return 'stop';
  }
  if (context.device === context.display) {
    return 'matches';
  }
  if (context.frozen) {
    return 'frozen';
  }
  if (context.auto) {
    return 'auto';
  }
  return sameOffset(context.at, context.device, context.display) ? 'same-offset' : 'prompt';
}

/**
 * Whether a write failed because the server's zone set lacks the zone.
 *
 * @param error - The failure.
 * @returns Whether it is that validation error.
 */
function zoneUnknown(error: unknown): boolean {
  if (!(error instanceof TmApiError) || error.kind !== 'validation') {
    return false;
  }
  const errors = error.problem?.['errors'];
  return Array.isArray(errors) && errors.some((item: { readonly code?: unknown } | null) => item?.code === ZONE_UNKNOWN);
}

// The kinds spec 0026 §4.1 has the error views report.
/**
 * Whether the error views would report a failure to telemetry when they
 * show it; the prompt and the automatic update describe failures without
 * showing those views, so they report them themselves.
 *
 * @param error - The failure.
 * @returns Whether it is a server, availability or partial failure, or an outcome the client cannot know.
 */
function reported(error: TmApiError): boolean {
  return (
    error.kind === 'server' ||
    error.kind === 'unavailable' ||
    error.kind === 'partial-failure' ||
    (error.kind === 'offline' && error.code === 'outcome-unknown')
  );
}

/**
 * The device-zone check: its triggers, the steps, the automatic update with
 * its toast, and the prompt. Internal: `TmDeviceZone` loads it at idle
 * after the first render and calls it.
 */
@Injectable({ providedIn: 'root' })
export class TmZoneCheck {
  readonly #injector = inject(Injector);
  readonly #app = inject(DestroyRef);
  readonly #host = inject(TM_API_HOST);
  readonly #api = inject(TmApi);
  readonly #l10n = inject(TmLocalization);
  readonly #store = inject(TmClientStore);
  readonly #launch = inject(TmLaunchCache);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #entities = inject(TmEntities);
  readonly #telemetry = inject(TmTelemetry);
  readonly #now = inject(TM_NOW);

  /** The device's zone document. */
  readonly #doc = this.#store.doc<TmZoneDoc>(DEVICE, ZONE);

  /** Whether the triggers are started. */
  #started = false;
  /** The routed tenant's partition key the entry trigger last checked, or `null` outside a tenant. */
  #entered: string | null = null;
  /** The last check queued, which every later one waits for. */
  #tail: Promise<void> = Promise.resolve();
  /** The check queued and not started yet, which calls made meanwhile share. */
  #next: Promise<void> | null = null;
  /** The change whose offline update waits to try once more, as `<sub>:<tenantId>:<zone>`. */
  #retrying: string | null = null;
  /** The change whose offline update was tried once more already. */
  #retried: string | null = null;
  /** The prompt locks this tab waits for, in the background, to check again once another tab frees them. */
  readonly #waiting = new Set<string>();

  /** Registers the strings. */
  constructor() {
    this.#l10n.ɵregisterStrings('zone', TM_ZONE_STRINGS_EN);
  }

  /** Starts the triggers, once: a check on each tenant entry, the current one included, and whenever the tab comes into view. */
  ɵstart(): void {
    if (this.#started || this.#app.destroyed) {
      return;
    }
    this.#started = true;

    // --- Each tenant entry, once its `me`, `settings` and facts are present ---
    effect(
      () => {
        const partition = this.#refresher.ɵpartition();
        const ready =
          this.#launch.docs.me() !== undefined && this.#launch.docs.settings() !== undefined && this.#launch.docs.tenant() !== undefined;
        untracked(() => {
          const key = partition === null ? null : `${partition.sub}:${partition.tenantId}`;
          if (key === null) {
            this.#entered = null;
          } else if (ready && key !== this.#entered) {
            this.#entered = key;
            void this.check();
          }
        });
      },
      { injector: this.#injector },
    );

    // --- The tab comes into view; the localization service has re-read the device's zone by then ---
    const stop = this.#host.listen('document', 'visibilitychange', () => {
      if (this.#host.visible()) {
        void this.check();
      }
    });
    this.#app.onDestroy(stop);
  }

  /**
   * Checks now, after the check in progress, if any; calls made before a
   * queued check starts share it.
   *
   * @returns A promise that resolves once the check is done; it never rejects.
   */
  check(): Promise<void> {
    if (this.#next === null) {
      const next = this.#tail
        .then(() => {
          this.#next = null;
          return this.#run();
        })
        .catch((error: unknown) => this.#telemetry.error(error, { where: 'tm.l10n.zone-check' }));
      this.#next = next;
      this.#tail = next;
    }
    return this.#next;
  }

  /** One check: the steps, and the automatic update or the prompt under the user's lock. */
  async #run(): Promise<void> {
    const context = await this.#ready();
    if (context === null) {
      return;
    }
    const step = stepOf(context);
    if (step === 'stop') {
      return;
    }
    if (step !== 'auto' && step !== 'prompt') {
      await this.#record(context, step);
      return;
    }
    // Another tab holding the lock asks, or updates, for every tab of the user.
    const slug = this.#injector.get(TmClientMetadataStore).peek()?.distribution.slug ?? '';
    const name = `tm:${slug}:${context.sub}:zone-prompt`;
    if (!(await this.#injector.get(TmLocks).tryRun(name, () => this.#locked()))) {
      this.#awaitLock(name);
    }
  }

  /**
   * Waits for a prompt lock another tab holds, in the background, and then
   * runs the steps again under it: they stop when the holder handled the
   * change, and ask when it gave up first. One wait per lock at a time.
   *
   * @param name - The lock's name.
   */
  #awaitLock(name: string): void {
    if (this.#waiting.has(name) || this.#app.destroyed) {
      return;
    }
    this.#waiting.add(name);
    void this.#injector
      .get(TmLocks)
      .run(name, async () => {
        // The holder's record may not have reached this tab's mirror yet.
        await this.#store.readAll(DEVICE);
        await this.#locked();
      })
      .catch((error: unknown) => this.#telemetry.error(error, { where: 'tm.l10n.zone-check' }))
      .finally(() => this.#waiting.delete(name));
  }

  /** Under the lock: the steps again, then the automatic update, or the prompt once the user is idle and no modal is open. */
  async #locked(): Promise<void> {
    let prompt: typeof import('./tm-zone-prompt') | undefined;
    for (;;) {
      const context = await this.#ready();
      if (context === null) {
        return;
      }
      const step = stepOf(context);
      switch (step) {
        case 'stop':
          return;
        case 'auto':
          return this.#auto(context);
        case 'prompt':
          if (prompt === undefined) {
            // The dialog's chunk loads first, so the dialog opens right after the steps run again.
            prompt = await import('./tm-zone-prompt');
            await this.#quiet();
            continue;
          }
          return this.#prompt(context, prompt);
        default:
          return this.#record(context, step);
      }
    }
  }

  /**
   * The check's facts, once the Entities chunk has loaded: in a visible tab
   * of a tenant whose `me`, `settings` and facts are present.
   *
   * @returns The facts, or `null` when the check does nothing now.
   */
  async #ready(): Promise<TmZoneContext | null> {
    if (!this.#present()) {
      return null;
    }
    try {
      await this.#entities.load();
    } catch {
      // The aliases are missing: the next trigger tries again.
      return null;
    }
    if (this.#app.destroyed || !this.#present()) {
      return null;
    }
    return untracked(() => {
      const partition = this.#refresher.ɵpartition()!;
      const aliases = this.#entities.enum('core.TimeZone')?.aliases;
      const canonical = (zone: string): string => aliases?.[zone] ?? zone;
      const user = ɵtmZoneUser(this.#doc()?.data, partition.sub);
      const seen = user?.seen[partition.tenantId];
      return {
        sub: partition.sub,
        tenantId: partition.tenantId,
        frozen: this.#launch.docs.tenant()?.state === 'Frozen',
        device: canonical(this.#l10n.deviceTimeZone()),
        display: canonical(this.#l10n.timeZone()),
        seen: typeof seen === 'string' ? seen : undefined,
        auto: user?.auto === true,
        at: this.#now(),
      };
    });
  }

  /** Whether the tab is visible in a tenant whose `me`, `settings` and facts are present. */
  #present(): boolean {
    return untracked(
      () =>
        this.#host.visible() &&
        this.#refresher.ɵpartition() !== null &&
        this.#launch.docs.me() !== undefined &&
        this.#launch.docs.settings() !== undefined &&
        this.#launch.docs.tenant() !== undefined,
    );
  }

  /** Waits until the user's hands are off the keyboard and pointer and no modal is open, at once. */
  async #quiet(): Promise<void> {
    const idle = this.#injector.get(TmInputIdle);
    for (;;) {
      await idle.whenIdle();
      if (this.#app.destroyed || ɵtmOpenModals().length === 0) {
        return;
      }
      await new Promise<void>((resolve) => {
        const done = (): void => {
          stop();
          forget();
          resolve();
        };
        const stop = ɵtmWatchModals(() => ɵtmOpenModals().length === 0 && done());
        const forget = this.#app.onDestroy(done);
      });
    }
  }

  /**
   * Records the device zone as handled in the tenant.
   *
   * @param context - The check's facts.
   * @param action - The step, reported to telemetry; none for a record that follows an answer or an update.
   */
  async #record(context: TmZoneContext, action?: TmZoneStep): Promise<void> {
    if (action !== undefined) {
      this.#telemetry.event('tm.l10n.zone-change', { action });
    }
    await this.#store.patch<TmZoneDoc>(DEVICE, ZONE, (doc) =>
      ɵtmZoneDocWith(doc, context.sub, this.#now(), { seen: { tenantId: context.tenantId, zone: context.device } }),
    );
  }

  /**
   * Writes the device zone as the user's in the tenant and adopts the
   * result, so every tab re-renders its times.
   *
   * @param context - The check's facts.
   */
  async #write(context: TmZoneContext): Promise<void> {
    const result = await this.#api.write<TmMeLocaleResult>(ROUTE, { timeZone: context.device }, { tenantId: context.tenantId });
    if (!this.#app.destroyed) {
      this.#refresher.adopt(context.tenantId, { locale: result });
    }
  }

  /**
   * The automatic update: the write in the background, then the record and
   * the toast. An offline write tries once more when the server is
   * reachable again; a zone outside the server's set is recorded, so it is
   * never tried again until the device moves; any other failure records
   * nothing, so the next check tries again.
   *
   * @param context - The check's facts.
   */
  async #auto(context: TmZoneContext): Promise<void> {
    this.#telemetry.event('tm.l10n.zone-change', { action: 'auto' });
    const change = `${context.sub}:${context.tenantId}:${context.device}`;
    try {
      await this.#write(context);
    } catch (error) {
      if (error instanceof TmApiError && error.kind === 'offline') {
        this.#telemetry.event('tm.l10n.zone-auto', { outcome: 'offline' });
        // A write whose outcome is unknown is reported as the error views would, and tried again too.
        if (reported(error)) {
          this.#telemetry.error(error, { source: 'api' });
        }
        this.#retryOnline(change);
        return;
      }
      this.#telemetry.event('tm.l10n.zone-auto', { outcome: 'rejected' });
      if (zoneUnknown(error)) {
        await this.#record(context);
      } else if (!(error instanceof TmApiError)) {
        throw error;
      } else if (reported(error)) {
        this.#telemetry.error(error, { source: 'api' });
      }
      return;
    }
    this.#telemetry.event('tm.l10n.zone-auto', { outcome: 'ok' });
    await this.#record(context);
    this.#toast(context);
  }

  /**
   * Tries an offline update once more, through a whole check, once the
   * server is reachable: once per change of the device's zone.
   *
   * @param change - The change, as `<sub>:<tenantId>:<zone>`.
   */
  #retryOnline(change: string): void {
    if (this.#retried === change || this.#retrying === change || this.#app.destroyed) {
      return;
    }
    this.#retrying = change;
    const watch = effect(
      () => {
        const online = this.#api.online();
        untracked(() => {
          if (online && this.#retrying === change) {
            watch.destroy();
            this.#retrying = null;
            this.#retried = change;
            void this.check();
          } else if (this.#retrying !== change) {
            // A later change took over the retry.
            watch.destroy();
          }
        });
      },
      { injector: this.#injector },
    );
  }

  /**
   * The toast of an automatic update: the zone and its offset now, and
   * Change, which opens the Language & region section of My settings.
   *
   * @param context - The check's facts.
   */
  #toast(context: TmZoneContext): void {
    if (this.#app.destroyed) {
      return;
    }
    const locale = untracked(this.#l10n.locale);
    const zone = tmFormatZoneName(context.device, locale, context.at) || context.device;
    const offset = tmFormatZoneOffset(context.at, locale, context.device);
    const target = this.#injector.get(TmUrls).forTenant(context.tenantId).page('me/region');
    this.#injector.get(TmToasts).show({
      tone: 'neutral',
      title: untracked(this.#l10n.ɵtext('tm.zone.changed', { zone, offset })),
      action: {
        label: untracked(this.#l10n.ɵtext('tm.zone.change')),
        run: () => void this.#injector.get(Router).navigateByUrl(target),
      },
      duration: TM_TOAST_FEEDBACK_MS,
      key: 'tm.zone.changed',
    });
  }

  /**
   * The prompt: the dialog, then the answer's record. Change time zone
   * writes, adopts and records from inside the dialog, and turns the
   * option on when the box is ticked; Keep current and a dismissal record
   * the device zone as handled; a dialog that history navigation closed
   * records nothing, so the next check asks again.
   *
   * @param context - The check's facts.
   * @param prompt - The dialog's chunk.
   */
  async #prompt(context: TmZoneContext, prompt: typeof import('./tm-zone-prompt')): Promise<void> {
    this.#telemetry.event('tm.l10n.zone-change', { action: 'prompt' });
    const answer: TmZoneAnswer = await prompt.ɵtmAskZone(this.#injector, {
      device: context.device,
      display: context.display,
      at: context.at,
      change: (auto) => this.#change(context, auto),
    });
    if (answer.answer === null) {
      return;
    }
    this.#telemetry.event('tm.l10n.zone-prompt', { answer: answer.answer, auto: answer.auto });
    if (answer.answer !== 'change' && !this.#app.destroyed) {
      await this.#record(context);
    }
  }

  /**
   * Change time zone: the write, the adoption and the record, and the
   * option turned on when the box is ticked.
   *
   * @param context - The check's facts.
   * @param auto - Whether the box is ticked.
   * @returns `ok`, or `unknown` when the server's zone set lacks the zone.
   * @throws The write's failure otherwise, which the dialog describes.
   */
  async #change(context: TmZoneContext, auto: boolean): Promise<'ok' | 'unknown'> {
    try {
      await this.#write(context);
    } catch (error) {
      if (zoneUnknown(error)) {
        return 'unknown';
      }
      if (error instanceof TmApiError && reported(error)) {
        this.#telemetry.error(error, { source: 'api' });
      }
      throw error;
    }
    // The user's zone changed: a record or option the store refuses now is
    // reported, never shown as the change failing.
    try {
      if (!this.#app.destroyed) {
        await this.#record(context);
        if (auto) {
          await this.#injector.get(TmDeviceZone).setAuto(true, 'prompt');
        }
      }
    } catch (error) {
      this.#telemetry.error(error, { where: 'tm.l10n.zone-check' });
    }
    return 'ok';
  }
}
