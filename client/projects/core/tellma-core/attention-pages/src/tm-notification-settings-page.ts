// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, type Signal, untracked, viewChild } from '@angular/core';
import { type TmActionResult, type TmContextError, type TmJson, TmLocalization, type TmPageAction, TmPageContexts } from '@tellma/core';

import { tmRegisterAttentionPagesStrings } from './tm-attention-pages-strings';
import type { TmNotificationSettingsModel } from './tm-notification-settings-model';
import { TmNotificationSettings } from './tm-notification-settings';

// The body of the hub's Notifications section (spec 0028 §10.3), which spec
// 0032 §8's hub renders at /{t}/me/notifications. It holds the section and
// publishes its page context (spec 0026 §9.3): kind `settings`, state
// `{ category: 'notifications', mode: 'edit', dirty }`, and two actions. The
// set action saves like a click; the test action sends a test message. The
// context has no form: every change here saves itself, while a form's
// proposal must never save.

/** The set action's arguments. */
const SET_PARAMS = {
  type: 'object',
  properties: { type: { type: 'string' }, channel: { type: 'string' }, enabled: { type: 'boolean' } },
  required: ['type', 'channel', 'enabled'],
} as const;

/** The test action's arguments. */
const TEST_PARAMS = { type: 'object', properties: { channel: { type: 'string' } }, required: ['channel'] } as const;

/** A JSON object's members, or nothing for anything else. */
const members = (args: TmJson): Readonly<Record<string, TmJson>> =>
  typeof args === 'object' && args !== null && !Array.isArray(args) ? (args as Readonly<Record<string, TmJson>>) : {};

/**
 * The Notifications section of My settings as a page: the signed-in user's
 * section, and its page context with the `notifications.settings.set` and
 * `notifications.settings.test` actions. The hub renders it as the
 * section's body.
 *
 * @example
 * ```html
 * <tm-notification-settings-page />
 * ```
 */
@Component({
  selector: 'tm-notification-settings-page',
  imports: [TmNotificationSettings],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-notification-settings-page' },
  template: `<tm-notification-settings />`,
  styles: `
    :host {
      display: block;
    }
  `,
})
export class TmNotificationSettingsPage {
  /** The app's localization. */
  readonly #l10n = inject(TmLocalization);

  /** The section the page holds. */
  protected readonly section = viewChild(TmNotificationSettings);

  /** The section's model, once it renders. */
  readonly #model: Signal<TmNotificationSettingsModel | null> = computed(() => this.section()?.ɵmodel() ?? null);

  /** Creates the page, which publishes its page context while it stays. */
  constructor() {
    tmRegisterAttentionPagesStrings(this.#l10n);
    const status = computed(() => this.#model()?.status() ?? 'loading');
    const dirty = computed(() => this.#model()?.dirty() ?? false);

    // --- What is wrong: a failed read, or a save that failed ---
    const errors = computed((): readonly TmContextError[] => {
      const model = this.#model();
      const failures: TmContextError[] = [];
      const error = model?.status() === 'error' ? model.loadError() : null;
      if (model?.status() === 'error') {
        failures.push({ source: 'request', code: error?.kind ?? 'unknown', message: error?.message ?? this.#text('settings.loadFailed') });
      }
      if (model?.saveStatus() === 'failed') {
        failures.push({ source: 'request', code: 'save-failed', message: this.#text('settings.failed') });
      }
      return failures;
    });

    // --- The actions: turn a cell on or off, and send a test ---
    const set: TmPageAction = {
      id: 'notifications.settings.set',
      labelKey: 'tm.attentionPages.actions.set',
      group: 'menu',
      enabled: computed(() => status() === 'ready' && this.#model()?.lockReason() === null),
      destructive: false,
      disabledReason: computed(() => this.#model()?.lockReason() ?? null),
      params: SET_PARAMS,
      accepts: (args) => {
        const { type, channel, enabled } = members(args);
        const cell = typeof type === 'string' && typeof channel === 'string' ? untracked(this.#model)?.cell(type, channel) : undefined;
        return cell !== undefined && cell.kind !== 'none' && typeof enabled === 'boolean';
      },
      invoke: (args) => {
        const { type, channel, enabled } = members(args);
        const model = untracked(this.#model);
        const cell = model?.cell(String(type), String(channel));
        if (model === null || cell === undefined || cell.kind === 'none' || typeof enabled !== 'boolean') {
          return Promise.resolve({ outcome: 'refused', reason: 'not-found' });
        }
        if (cell.kind === 'locked') {
          // A type the user cannot mute keeps its inbox on.
          return Promise.resolve({ outcome: 'refused', reason: 'disabled', message: this.#text('settings.alwaysOn') });
        }
        const reason = untracked(model.lockReason);
        if (reason !== null) {
          return Promise.resolve({ outcome: 'refused', reason: 'disabled', message: reason });
        }
        model.set(cell, enabled);
        return Promise.resolve({ outcome: 'done' });
      },
    };
    const test: TmPageAction = {
      id: 'notifications.settings.test',
      labelKey: 'tm.attentionPages.actions.test',
      group: 'menu',
      enabled: computed(() => status() === 'ready' && this.#model()?.testReason() === null),
      destructive: false,
      disabledReason: computed(() => this.#model()?.testReason() ?? null),
      params: TEST_PARAMS,
      accepts: (args) => {
        const { channel } = members(args);
        return untracked(this.#model)?.tests.some((row) => row.channel.key === channel) ?? false;
      },
      invoke: async (args): Promise<TmActionResult> => {
        const { channel } = members(args);
        const model = untracked(this.#model);
        const row = model?.tests.find((candidate) => candidate.channel.key === channel);
        if (model === null || row === undefined) {
          return { outcome: 'refused', reason: 'not-found' };
        }
        const reason = untracked(model.testReason);
        if (reason !== null) {
          return { outcome: 'refused', reason: 'disabled', message: reason };
        }
        if (untracked(row.busy)) {
          return { outcome: 'refused', reason: 'busy' };
        }
        const result = await model.test(row);
        return result === null ? { outcome: 'failed', error: new Error(`The test message on ${row.channel.key} failed.`) } : { outcome: 'done', data: result };
      },
    };

    inject(TmPageContexts).register(
      {
        kind: 'settings',
        routeTemplate: '/:t/me/notifications',
        title: this.#l10n.ɵtext('tm.nav.me.notifications'),
        status,
        state: computed(() => ({ category: 'notifications', mode: 'edit', dirty: dirty() })),
        errors,
        actions: computed(() => [set, test]),
      },
      inject(DestroyRef),
    );
  }

  /**
   * Reads one of the section's strings.
   *
   * @param key - The key under `tm.attentionPages`.
   * @returns The text.
   */
  #text(key: string): string {
    return this.#l10n.ɵtext(`tm.attentionPages.${key}`)();
  }
}
