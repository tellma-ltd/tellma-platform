// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { computed, type Injector, type Signal, signal, untracked, type WritableSignal } from '@angular/core';
import {
  TM_CLIENT_METADATA,
  TmApi,
  TmApiError,
  TmLaunchCache,
  TmLocalization,
  type TmNotificationTypeMetadata,
  TmPreferences,
  TmServerText,
  TmTelemetry,
  TmUrls,
} from '@tellma/core';
import { TmAttention, tmNotificationToastsKey } from '@tellma/core/attention';

import { TmLatestSaver, type TmSaveOutcome, type TmSaveState } from './tm-latest-saver';

// The engine of the Notifications section of My settings (spec 0028 §10.3).
// It lays the metadata's types out as one card per category, one row per
// type and one cell per channel, holds the user's values, and saves the
// whole set 600 ms after the last change, one save at a time. For another
// user (spec 0032 §4.8) it reads and saves through that user's routes and
// has no test delivery. The component renders it; the page publishes its
// page context from it.

/** Whose notification settings a section edits: the signed-in user's, or another user's by id. */
export type TmNotificationSettingsUser = 'me' | number;

/** One stored notification preference: a type's channel, on or off. */
export interface TmNotificationPreference {
  /** The notification type's key. */
  readonly type: string;
  /** The channel's key. */
  readonly channel: string;
  /** Whether the channel delivers the type. */
  readonly enabled: boolean;
}

/** How a test message went: sent, no address to send it to, or a channel the server has not set up. */
export type TmTestNotificationResult = 'Sent' | 'NoContactAddress' | 'NotConfigured';

/** What a grid cell holds: a switch, the locked inbox of a type the user cannot mute, or nothing for a channel-only type's inbox. */
export type TmNotificationCellKind = 'switch' | 'locked' | 'none';

/** One cell of the grid: a type on a channel. Internal. */
export interface TmNotificationCell {
  /** The cell's key, `type\nchannel`. */
  readonly key: string;
  /** The notification type's key. */
  readonly type: string;
  /** The channel's key. */
  readonly channel: string;
  /** What the cell holds. */
  readonly kind: TmNotificationCellKind;
  /** The channel's default, which a switch shows until the user deviates from it. */
  readonly defaultEnabled: boolean;
}

/** One row of the grid: a notification type and its cells, in channel order. Internal. */
export interface TmNotificationRow {
  /** The type. */
  readonly type: TmNotificationTypeMetadata;
  /** The type's label, from the server's text. */
  readonly label: Signal<string>;
  /** The row's cells, one per channel. */
  readonly cells: readonly TmNotificationCell[];
}

/** One card of the grid: a category and its types. Internal. */
export interface TmNotificationCard {
  /** The category's key. */
  readonly category: string;
  /** The category's label, from the server's text. */
  readonly label: Signal<string>;
  /** The category's rows, in the metadata's order. */
  readonly rows: readonly TmNotificationRow[];
}

/** A channel of the grid's columns and the test card's rows. Internal. */
export interface TmNotificationChannelView {
  /** The channel's key. */
  readonly key: string;
  /** The channel's label, from the server's text. */
  readonly label: Signal<string>;
}

/** One row of the test card: a channel other than the inbox. Internal. */
export interface TmTestRow {
  /** The channel. */
  readonly channel: TmNotificationChannelView;
  /** The address the channel would use, or `null` when it has none or the channel resolves none here. */
  readonly address: Signal<string | null>;
  /** Whether the channel resolves an address here, so the row says "No address" when it has none. */
  readonly resolvesAddress: boolean;
  /** Whether a test is in flight; the model writes it. */
  readonly busy: WritableSignal<boolean>;
  /** The last test's result, or `null`; the model writes it. */
  readonly result: WritableSignal<TmTestNotificationResult | null>;
}

/** The combined save status the header shows. */
export type TmNotificationSaveStatus = 'idle' | 'saving' | 'saved' | 'failed';

/** The answer of `users/preferences/get`: another user's preference bag, each value a JSON text. */
interface UserPreferencesResult {
  /** The bag, keyed by preference key. */
  readonly preferences?: Readonly<Record<string, string>>;
}

/** The grid's values: every switch cell's value, keyed by cell key. */
type Values = ReadonlyMap<string, boolean>;

/** The wait between the last change and the save. */
const SAVE_WAIT_MS = 600;

/** The cell key of a type on a channel. */
const cellKey = (type: string, channel: string): string => `${type}\n${channel}`;

/** Whether two value sets agree. */
const same = (a: Values, b: Values): boolean => a.size === b.size && [...a].every(([key, value]) => b.get(key) === value);

/** The telemetry bucket of a save's number of changed cells. */
const bucket = (changes: number): string => (changes <= 1 ? '1' : changes <= 5 ? '2-5' : '6+');

/**
 * The Notifications section's engine for one user: the grid's layout and
 * values, their saving, the pop-up switch, the test delivery and the reasons
 * that disable them. Internal; create it in an injection context's
 * component, and dispose of it when the component leaves.
 */
export class TmNotificationSettingsModel {
  /** The app's API. */
  readonly #api: TmApi;

  /** The section's tenant, fixed when the model is created. */
  readonly #tenantId: number | undefined;

  /** The launch documents: the tenant's state and name, and the user's addresses. */
  readonly #launch: TmLaunchCache;

  /** The app's localization. */
  readonly #l10n: TmLocalization;

  /** The signed-in user's preferences, for the pop-up switch of `me`. */
  readonly #preferences: TmPreferences;

  /** The app's telemetry. */
  readonly #telemetry: TmTelemetry;

  /** The polite announcer of test results. */
  readonly #announcer: LiveAnnouncer;

  /** The injector the error views resolve their presenter from. */
  readonly #injector: Injector;

  /** Aborts the reads when the model leaves. */
  readonly #abort = new AbortController();

  /** The grid's cells, by key. */
  readonly #cells = new Map<string, TmNotificationCell>();

  /** The values the switches show. */
  readonly #current = signal<Values>(new Map());

  /** The values last read or saved. */
  #saved: Values = new Map();

  /** Whether the set has loaded, failed to, or is loading. */
  readonly #status = signal<'loading' | 'ready' | 'error'>('loading');

  /** The failed read, while the section shows its error state. */
  readonly #loadError = signal<TmApiError | null>(null);

  /** Whether a read runs over values already shown. */
  readonly #refreshing = signal(false);

  /** Counts reads, so only the latest one applies. */
  #loadRun = 0;

  /** Another user's pop-up value; unused for `me`. */
  readonly #otherPopups = signal(true);

  /** Saves the grid's values. */
  readonly #gridSaver: TmLatestSaver<Values>;

  /** Saves another user's pop-up value; `null` for `me`, whose value is the preferences'. */
  readonly #popupSaver: TmLatestSaver<boolean> | null;

  /** Whose settings the model edits. */
  readonly user: TmNotificationSettingsUser;

  /** The grid's columns, in registration order. */
  readonly channels: readonly TmNotificationChannelView[];

  /** The grid's cards, one per category, in the order of each category's first type. */
  readonly cards: readonly TmNotificationCard[];

  /** The test card's rows: one per channel other than the inbox, for `me` only. */
  readonly tests: readonly TmTestRow[];

  /** Whether the set has loaded, failed to, or is loading. */
  readonly status: Signal<'loading' | 'ready' | 'error'> = this.#status.asReadonly();

  /** The failed read, while the section shows its error state. */
  readonly loadError: Signal<TmApiError | null> = this.#loadError.asReadonly();

  /**
   * Whether a read runs over values already shown: the section keeps them,
   * and its switches turn read-only, until the read replaces them.
   */
  readonly refreshing: Signal<boolean> = this.#refreshing.asReadonly();

  /**
   * Why every switch is disabled: offline, else a frozen tenant; `null` while
   * the user can change them.
   */
  readonly lockReason: Signal<string | null>;

  /** Why "Send a test" is disabled: the lock's reason, else a read-only tenant; `null` while a test can be sent. */
  readonly testReason: Signal<string | null>;

  /** Whether the pop-up switch is on. */
  readonly popups: Signal<boolean>;

  /** The combined save status: a failure wins, then a save in flight or waiting, then a finished save. */
  readonly saveStatus: Signal<TmNotificationSaveStatus>;

  /** Whether a change is not saved yet: waiting, in flight, or failed. */
  readonly dirty: Signal<boolean>;

  /**
   * Creates the model and starts reading the user's set.
   *
   * @param injector - The component's injector.
   * @param user - Whose settings to edit.
   */
  constructor(injector: Injector, user: TmNotificationSettingsUser) {
    this.#injector = injector;
    this.#api = injector.get(TmApi);
    this.#launch = injector.get(TmLaunchCache);
    this.#l10n = injector.get(TmLocalization);
    this.#preferences = injector.get(TmPreferences);
    this.#telemetry = injector.get(TmTelemetry);
    this.#announcer = injector.get(LiveAnnouncer);
    this.#tenantId = untracked(injector.get(TmUrls).tenantId) ?? undefined;
    this.user = user;
    const attention = injector.get(TmAttention);
    const server = injector.get(TmServerText);
    const metadata = injector.get(TM_CLIENT_METADATA);

    // --- The layout: channels in registration order, cards by category ---
    this.channels = metadata.notificationChannels.map((channel) => ({
      key: channel.key,
      label: server.message(`NotificationChannel_${channel.key}`),
    }));
    const byCategory = new Map<string, TmNotificationRow[]>();
    for (const type of metadata.notificationTypes) {
      const cells = metadata.notificationChannels.map((channel): TmNotificationCell => {
        // The inbox column: nothing for a channel-only type, locked on for one the user cannot mute.
        const kind: TmNotificationCellKind = channel.key !== 'inbox' ? 'switch' : !type.inbox ? 'none' : !type.mutable ? 'locked' : 'switch';
        return { key: cellKey(type.key, channel.key), type: type.key, channel: channel.key, kind, defaultEnabled: channel.defaultEnabled };
      });
      cells.forEach((cell) => this.#cells.set(cell.key, cell));
      const rows = byCategory.get(type.category) ?? [];
      rows.push({ type, label: server.message(`NotificationType_${type.key.replaceAll('.', '_')}`), cells });
      byCategory.set(type.category, rows);
    }
    this.cards = [...byCategory].map(([category, rows]) => ({ category, label: server.message(`NotificationCategory_${category}`), rows }));
    this.#current.set(this.#defaults());
    this.#saved = untracked(this.#current);

    // --- The test card: `me` only, one row per channel beyond the inbox ---
    this.tests =
      user === 'me'
        ? this.channels
            .filter((channel) => channel.key !== 'inbox')
            .map((channel) => {
              // Spec 0021 §4.4: the email channel sends to the contact email, else the
              // sign-in email. The same section names ContactMobile for an SMS
              // channel, but no SMS channel key is registered yet, so only email
              // resolves an address.
              const resolvesAddress = channel.key === 'email';
              const address = computed(() => {
                const profile = this.#launch.docs.me()?.user;
                return resolvesAddress ? (profile?.contactEmail ?? profile?.email ?? null) : null;
              });
              return { channel, address, resolvesAddress, busy: signal(false), result: signal<TmTestNotificationResult | null>(null) };
            })
        : [];

    // --- Reasons: offline, else frozen, lock everything; read-only refuses a test ---
    const tenantName = (): string => {
      const names = this.#launch.docs.settings()?.names ?? [];
      return names[this.#l10n.contentSlot() - 1] ?? names[0] ?? '';
    };
    this.lockReason = computed(() => {
      if (attention.writable()) {
        return null;
      }
      return this.#api.online()
        ? this.#l10n.ɵtext('tm.entity.tenantFrozen', { tenant: tenantName() })()
        : this.#l10n.ɵtext('tm.errors.offline.title')();
    });
    this.testReason = computed(
      () =>
        this.lockReason() ??
        (this.#launch.docs.tenant()?.state === 'ReadOnly' ? this.#l10n.ɵtext('tm.entity.tenantReadOnly', { tenant: tenantName() })() : null),
    );

    // --- Saving: the grid's whole set, and another user's pop-up value ---
    this.#gridSaver = new TmLatestSaver((values) => this.#saveGrid(values));
    this.#popupSaver = user === 'me' ? null : new TmLatestSaver((value) => this.#savePopups(user, value));
    this.popups = user === 'me' ? this.#preferences.get(tmNotificationToastsKey) : this.#otherPopups.asReadonly();
    const states = (): TmSaveState[] => [this.#gridSaver.state(), this.#popupSaver?.state() ?? 'idle'];
    this.saveStatus = computed(() => {
      const all = states();
      return all.includes('failed') ? 'failed' : all.some((state) => state === 'saving' || state === 'pending') ? 'saving' : all.includes('saved') ? 'saved' : 'idle';
    });
    this.dirty = computed(() => states().some((state) => state === 'pending' || state === 'saving' || state === 'failed'));

    void this.reload();
  }

  /**
   * The value a cell shows: the user's value for a switch, on for a locked
   * inbox, off for a channel-only type's inbox.
   *
   * @param cell - The cell.
   * @returns Whether it is on.
   */
  value(cell: TmNotificationCell): boolean {
    return cell.kind === 'switch' ? (this.#current().get(cell.key) ?? cell.defaultEnabled) : cell.kind === 'locked';
  }

  /**
   * The cell of a type on a channel.
   *
   * @param type - The type's key.
   * @param channel - The channel's key.
   * @returns The cell, or `undefined` when the metadata has no such type or channel.
   */
  cell(type: string, channel: string): TmNotificationCell | undefined {
    return this.#cells.get(cellKey(type, channel));
  }

  /**
   * Turns a cell's channel on or off; it applies at once and saves with the
   * whole set after the wait. Nothing happens to a cell that is not a
   * switch, before the set has loaded, while a reload runs over it, or while
   * the switches are locked.
   *
   * @param cell - The cell.
   * @param enabled - Whether the channel delivers the type.
   * @returns Whether the change applied.
   */
  set(cell: TmNotificationCell, enabled: boolean): boolean {
    if (cell.kind !== 'switch' || !this.#changeable()) {
      return false;
    }
    const values = new Map(untracked(this.#current));
    values.set(cell.key, enabled);
    this.#current.set(values);
    this.#gridSaver.want(values, SAVE_WAIT_MS);
    return true;
  }

  /**
   * Turns arrival pop-ups on or off: for `me` through the preferences, which
   * apply at once; for another user through their route, saved at once.
   * Nothing happens before the set has loaded, while a reload runs over it,
   * or while the switches are locked.
   *
   * @param enabled - Whether arrivals pop up.
   */
  setPopups(enabled: boolean): void {
    if (!this.#changeable()) {
      return;
    }
    if (this.#popupSaver === null) {
      this.#preferences.set(tmNotificationToastsKey, enabled);
      return;
    }
    this.#otherPopups.set(enabled);
    this.#popupSaver.want(enabled, 0);
  }

  /** Sends again what a failed save left unsaved: Try again. */
  retry(): void {
    this.#gridSaver.retry();
    this.#popupSaver?.retry();
  }

  /**
   * Sends a test message on a channel and shows its result inline,
   * announced politely; a failed call shows the action's error dialog.
   *
   * @param row - The test card's row.
   * @returns The result, or `null` when the test did not run or failed.
   */
  async test(row: TmTestRow): Promise<TmTestNotificationResult | null> {
    if (untracked(row.busy) || untracked(this.testReason) !== null) {
      return null;
    }
    const { busy, result } = row;
    busy.set(true);
    result.set(null);
    try {
      const answer = await this.#api.write<TmTestNotificationResult>('users/me/test-notification', { channel: row.channel.key }, { tenantId: this.#tenantId });
      result.set(answer);
      void this.#announcer.announce(this.resultText(row, answer), 'polite');
      return answer;
    } catch (error) {
      // The dialog stays while the button is free again.
      void this.#present(error, 'action');
      return null;
    } finally {
      busy.set(false);
    }
  }

  /**
   * The text of a test's result on a row.
   *
   * @param row - The row.
   * @param result - The result.
   * @returns The text.
   */
  resultText(row: TmTestRow, result: TmTestNotificationResult): string {
    switch (result) {
      case 'Sent':
        return this.#l10n.ɵtext('tm.attentionPages.test.sent')();
      case 'NoContactAddress':
        return this.#l10n.ɵtext(row.channel.key === 'email' ? 'tm.attentionPages.test.noEmail' : 'tm.attentionPages.test.noContact')();
      default:
        return this.#l10n.ɵtext('tm.attentionPages.test.notConfigured', { channel: row.channel.label() })();
    }
  }

  /**
   * Reads the user's set again, replacing the switches' values. The first
   * read, and one after a failed read, shows the loading state; a read over
   * values already shown keeps them in place until it ends.
   */
  async reload(): Promise<void> {
    const run = ++this.#loadRun;
    if (untracked(this.#status) === 'ready') {
      this.#refreshing.set(true);
    } else {
      this.#status.set('loading');
    }
    this.#loadError.set(null);
    const options = { signal: this.#abort.signal, tenantId: this.#tenantId };
    try {
      const user = this.user;
      // Another user's set and pop-up value come from that user's routes, read together.
      const [rows, bag] = await Promise.all([
        user === 'me'
          ? this.#api.read<readonly TmNotificationPreference[]>('notification-preferences/get', {}, options)
          : this.#api.read<readonly TmNotificationPreference[]>('users/notification-preferences/get', { userId: user }, options),
        user === 'me' ? Promise.resolve(null) : this.#api.read<UserPreferencesResult>('users/preferences/get', { userId: user }, options),
      ]);
      if (run !== this.#loadRun) {
        return;
      }
      this.#apply(rows);
      if (bag !== null) {
        this.#otherPopups.set(this.#parsePopups(bag.preferences?.[tmNotificationToastsKey.key]));
      }
      this.#status.set('ready');
      this.#refreshing.set(false);
    } catch (error) {
      if (run !== this.#loadRun || this.#abort.signal.aborted) {
        return;
      }
      // A failed read shows the error state, even over shown values, which it can no longer vouch for.
      this.#loadError.set(error instanceof TmApiError ? error : null);
      this.#status.set('error');
      this.#refreshing.set(false);
    }
  }

  /** Sends what is still waiting and stops the reads: the section is leaving. */
  dispose(): void {
    this.#gridSaver.dispose();
    this.#popupSaver?.dispose();
    this.#abort.abort();
  }

  /** Whether a switch may change: the set has loaded, no reload runs over it, and nothing locks the switches. */
  #changeable(): boolean {
    return untracked(this.#status) === 'ready' && !untracked(this.#refreshing) && untracked(this.lockReason) === null;
  }

  /** Every switch cell at its channel's default. */
  #defaults(): Map<string, boolean> {
    return new Map([...this.#cells.values()].filter((cell) => cell.kind === 'switch').map((cell) => [cell.key, cell.defaultEnabled]));
  }

  /**
   * Applies a read set: each stored row a switch cell knows replaces its
   * default; rows for cells that are not switches are ignored.
   *
   * @param rows - The stored rows.
   */
  #apply(rows: readonly TmNotificationPreference[]): void {
    const values = this.#defaults();
    for (const row of rows) {
      const key = cellKey(row.type, row.channel);
      if (values.has(key)) {
        values.set(key, row.enabled);
      }
    }
    this.#current.set(values);
    this.#saved = values;
  }

  /**
   * Saves the grid's whole set, omitting cells at their defaults. A `422`
   * (a type gone after a deployment) reloads the set and shows the save's
   * error dialog.
   *
   * @param values - The values to save.
   * @returns How the save ended.
   */
  async #saveGrid(values: Values): Promise<TmSaveOutcome> {
    if (same(values, this.#saved)) {
      return 'saved';
    }
    const changes = [...values].filter(([key, value]) => this.#saved.get(key) !== value).length;
    const preferences: TmNotificationPreference[] = [];
    for (const [key, enabled] of values) {
      const cell = this.#cells.get(key)!;
      if (enabled !== cell.defaultEnabled) {
        preferences.push({ type: cell.type, channel: cell.channel, enabled });
      }
    }
    try {
      const user = this.user;
      await (user === 'me'
        ? this.#api.write('notification-preferences/save', preferences, { tenantId: this.#tenantId })
        : this.#api.write('users/notification-preferences/save', { id: user, arguments: { preferences } }, { tenantId: this.#tenantId }));
    } catch (error) {
      this.#telemetry.event('tm.attention.settings-saved', { changes: bucket(changes), outcome: 'failed' });
      if (error instanceof TmApiError && error.kind === 'validation') {
        await this.reload();
        void this.#present(error, 'save');
        return 'reset';
      }
      throw error;
    }
    this.#telemetry.event('tm.attention.settings-saved', { changes: bucket(changes), outcome: 'ok' });
    this.#saved = values;
    return 'saved';
  }

  /**
   * Saves another user's pop-up value through their preferences routes: the
   * key's fallback deletes the stored value, as the signed-in user's own
   * preferences do, so a later change to the fallback reaches the user.
   *
   * @param userId - The user.
   * @param enabled - The value.
   * @returns How the save ended.
   */
  async #savePopups(userId: number, enabled: boolean): Promise<TmSaveOutcome> {
    const { key, fallback } = tmNotificationToastsKey;
    const options = { tenantId: this.#tenantId };
    // Spec 0025 §9.1: a value deep-equal to the fallback deletes the key.
    await (enabled === fallback
      ? this.#api.write('users/preferences/delete', { id: userId, arguments: { keys: [key] } }, options)
      : this.#api.write('users/preferences/set', { id: userId, arguments: { items: [{ key, value: JSON.stringify(enabled) }] } }, options));
    return 'saved';
  }

  /**
   * Reads a stored pop-up value: a JSON boolean, else the key's fallback.
   *
   * @param text - The stored JSON text, if any.
   * @returns The value.
   */
  #parsePopups(text: string | undefined): boolean {
    try {
      const value: unknown = text === undefined ? undefined : JSON.parse(text);
      return typeof value === 'boolean' ? value : tmNotificationToastsKey.fallback;
    } catch {
      return tmNotificationToastsKey.fallback;
    }
  }

  /**
   * Shows a failed call's error dialog, loading the error views on first use.
   *
   * @param error - The failure.
   * @param kind - What failed: a save or an action.
   */
  async #present(error: unknown, kind: 'save' | 'action'): Promise<void> {
    if (!(error instanceof TmApiError)) {
      return;
    }
    const { TmErrorPresenter } = await import('@tellma/core/errors');
    await this.#injector.get(TmErrorPresenter).present(error, { kind, tenantId: this.#tenantId });
  }
}
