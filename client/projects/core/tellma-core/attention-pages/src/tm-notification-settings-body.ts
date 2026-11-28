// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  InjectionToken,
  Injector,
  input,
  type Signal,
  signal,
  untracked,
  viewChild,
  ViewContainerRef,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ɵTM_API_UPDATES,
  ɵTM_ROUTE_ERROR_VIEWS,
  ɵTmRouteError,
  type ɵTmRouteErrorKind,
  type TmActionResult,
  TmApiError,
  type TmContextError,
  type TmJson,
  TmLocalization,
  type TmPageAction,
  TmTelemetry,
  TmUrls,
} from '@tellma/core';
import type { TmErrorPresenter, TmErrorState } from '@tellma/core/errors';
import { TmButton } from '@tellma/core-ui/button';
import { TmCard, type TmCardHeadingLevel } from '@tellma/core-ui/card';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import { TmSwitch } from '@tellma/core-ui/switch';
import { TmTooltip } from '@tellma/core-ui/tooltip';

import { tmRegisterAttentionPagesStrings } from './tm-attention-pages-strings';
import {
  type TmNotificationCell,
  type TmNotificationRow,
  TmNotificationSettingsModel,
  type TmNotificationSettingsUser,
  type TmTestRow,
} from './tm-notification-settings-model';

// The body of the Notifications section of My settings (spec 0028 §10.3),
// in a chunk of its own that the section's shell loads, so the pages that
// share this entry point never download it: a save status, the pop-up card,
// one card per category with a type × channel grid, and, for the signed-in
// user, the test delivery card. Every change saves itself. Below md each
// type's switches stack under its label, each with its channel's name. While
// the switches are locked (offline, or a frozen tenant) every control is
// disabled with its reason as a tooltip and a description, and the values
// stay visible; while a reload runs over the shown values the switches turn
// read-only, keeping their place and their focus. The body also builds the
// pieces of the section's page context (spec 0026 §9.3) that read the model.

/** What the body reads of the error views' entry point. Internal. */
export interface TmNotificationSettingsErrors {
  /** The presenter that describes a failed read. */
  readonly TmErrorPresenter: typeof TmErrorPresenter;
  /** The error state a failed read renders. */
  readonly TmErrorState: typeof TmErrorState;
}

/** Loads the error views' entry point for a failed read. Internal: a test makes it fail. */
export const TM_NOTIFICATION_SETTINGS_ERRORS_LOAD = new InjectionToken<() => Promise<TmNotificationSettingsErrors>>('TM_NOTIFICATION_SETTINGS_ERRORS_LOAD', {
  providedIn: 'root',
  factory: () => () => import('@tellma/core/errors'),
});

/** Tells the sections on one page apart, for their elements' ids. */
let nextSection = 0;

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
 * The body of the Notifications section of My settings, which the section's
 * shell creates once its chunk loads. Internal: apps render
 * `tm-notification-settings`.
 */
@Component({
  selector: 'tm-notification-settings-body',
  imports: [NgTemplateOutlet, RouterLink, TmButton, TmCard, TmSkeleton, TmSwitch, TmTooltip, ɵTmRouteError],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-notification-settings__body' },
  template: `
    @if (model(); as model) {
      <!-- The save status, in a box of its own so it never shifts the cards; Try again sits outside the live region. -->
      <div class="tm-notification-settings__header">
        <p class="tm-notification-settings__status" role="status" [class.tm-notification-settings__status--failed]="model.saveStatus() === 'failed'">
          @switch (model.saveStatus()) {
            @case ('saving') {
              {{ text('settings.saving') }}
            }
            @case ('saved') {
              {{ text('settings.saved') }}
            }
            @case ('failed') {
              {{ text('settings.failed') }}
            }
          }
        </p>
        @if (model.saveStatus() === 'failed') {
          <button tmButton type="button" variant="ghost" size="sm" (click)="model.retry()">{{ text('settings.retry') }}</button>
        }
      </div>

      <!-- The reason every control is disabled, which each one's description reads. -->
      @if (model.lockReason(); as reason) {
        <span class="tm-notification-settings__hidden tm-notification-settings__lock" [id]="ids.lock">{{ reason }}</span>
      }

      @switch (model.status()) {
        @case ('loading') {
          <!-- The first read: skeleton cards in the grid's shape, as the shell drew while the chunk loaded. -->
          <div class="tm-notification-settings__loading" aria-hidden="true">
            @for (index of [0, 1]; track index) {
              <tm-card>
                <tm-skeleton width="30%" />
                <tm-skeleton [lines]="3" />
              </tm-card>
            }
          </div>
        }
        @case ('error') {
          <!-- A failed read: the error views' state; if they did not load, why, inline; else a plain line with Try again. -->
          <ng-container #errorSlot />
          @if (errorsFailure(); as kind) {
            <tm-route-error [view]="views[kind]" (retry)="model.reload()" />
          } @else if (fallbackError()) {
            <div class="tm-notification-settings__failed">
              <p>{{ text('settings.loadFailed') }}</p>
              <button tmButton type="button" size="sm" (click)="model.reload()">{{ text('settings.retry') }}</button>
            </div>
          }
        }
        @default {
          <!-- Pop-up messages: one switch, applied at once. -->
          <tm-card class="tm-notification-settings__card tm-notification-settings__popups" [heading]="text('settings.popups')" [headingLevel]="headingLevel()">
            <ng-container
              [ngTemplateOutlet]="reasoned"
              [ngTemplateOutletContext]="{ $implicit: popupSwitch, reason: model.lockReason() }"
            />
            <ng-template #popupSwitch>
              <tm-switch
                [checked]="model.popups()"
                [disabled]="model.lockReason() !== null"
                [readonly]="model.refreshing()"
                [aria-describedby]="model.lockReason() !== null ? ids.lock : null"
                (checkedChange)="model.setPopups($event)"
                >{{ text('settings.popupsLabel') }}</tm-switch
              >
            </ng-template>
          </tm-card>

          <!-- The grid: one card per category, one row per type, one column per channel. -->
          @for (card of model.cards; track card.category) {
            <tm-card class="tm-notification-settings__card" [attr.data-category]="card.category" [heading]="card.label()" [headingLevel]="headingLevel()">
              <table class="tm-notification-settings__grid">
                <thead>
                  <tr>
                    <th scope="col" class="tm-notification-settings__type">{{ text('settings.type') }}</th>
                    @for (channel of model.channels; track channel.key) {
                      <th scope="col" class="tm-notification-settings__channel">{{ channel.label() }}</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of card.rows; track row.type.key) {
                    <tr [attr.data-type]="row.type.key">
                      <th scope="row" class="tm-notification-settings__type">{{ row.label() }}</th>
                      @for (cell of row.cells; track cell.key; let index = $index) {
                        <td class="tm-notification-settings__cell" [attr.data-channel]="cell.channel" [attr.data-kind]="cell.kind">
                          @switch (cell.kind) {
                            @case ('none') {
                              <!-- A channel-only type never shows in the app. -->
                              <span class="tm-notification-settings__dash" aria-hidden="true">—</span>
                              <span class="tm-notification-settings__hidden">{{ cellName(row, index) }}: {{ text('settings.noInbox') }}</span>
                            }
                            @case ('locked') {
                              <!-- A type the user cannot mute: its inbox switch is on, disabled and described. -->
                              <tm-switch
                                [checked]="true"
                                [disabled]="true"
                                [aria-label]="cellName(row, index)"
                                [aria-describedby]="ids.always(cell)"
                                ><span class="tm-notification-settings__label">{{ model.channels[index].label() }}</span></tm-switch
                              >
                              <span class="tm-notification-settings__always" [id]="ids.always(cell)">{{ text('settings.alwaysOn') }}</span>
                            }
                            @default {
                              <ng-container
                                [ngTemplateOutlet]="reasoned"
                                [ngTemplateOutletContext]="{ $implicit: cellSwitch, reason: model.lockReason() }"
                              />
                              <ng-template #cellSwitch>
                                <tm-switch
                                  [checked]="model.value(cell)"
                                  [disabled]="model.lockReason() !== null"
                                  [readonly]="model.refreshing()"
                                  [aria-label]="cellName(row, index)"
                                  [aria-describedby]="model.lockReason() !== null ? ids.lock : null"
                                  (checkedChange)="change(cell, $event)"
                                  ><span class="tm-notification-settings__label">{{ model.channels[index].label() }}</span></tm-switch
                                >
                              </ng-template>
                            }
                          }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </tm-card>
          }

          <!-- Test delivery: the signed-in user's channels beyond the inbox. -->
          @if (model.tests.length > 0) {
            <tm-card class="tm-notification-settings__card tm-notification-settings__test-card" [heading]="text('test.heading')" [headingLevel]="headingLevel()">
              <ul class="tm-notification-settings__tests">
                @for (row of model.tests; track row.channel.key) {
                  <li class="tm-notification-settings__test" [attr.data-channel]="row.channel.key">
                    <span class="tm-notification-settings__test-channel" [id]="ids.test(row)">{{ row.channel.label() }}</span>
                    @if (row.resolvesAddress) {
                      <span class="tm-notification-settings__address" dir="auto">{{ row.address() ?? text('test.noAddress') }}</span>
                    }
                    <span class="tm-notification-settings__send">
                      <button
                        tmButton
                        type="button"
                        size="sm"
                        [pending]="row.busy()"
                        [disabledReason]="model.testReason()"
                        [attr.aria-describedby]="ids.test(row)"
                        (click)="send(row)"
                      >
                        {{ text('test.send') }}
                      </button>
                      <!-- The result, inline beside the button; the announcer reads it. -->
                      @if (row.result(); as result) {
                        <span class="tm-notification-settings__result" [class.tm-notification-settings__result--sent]="result === 'Sent'">
                          @if (result === 'NoContactAddress') {
                            <a [routerLink]="profileUrl()">{{ model.resultText(row, result) }}</a>
                          } @else {
                            {{ model.resultText(row, result) }}
                          }
                        </span>
                      }
                    </span>
                  </li>
                }
              </ul>
            </tm-card>
          }
        }
      }
    }

    <!-- A control, under its reason's tooltip while one applies: a disabled control takes no pointer, so its wrapper holds the tooltip. -->
    <ng-template #reasoned let-control let-reason="reason">
      @if (reason) {
        <span class="tm-notification-settings__reasoned" [tmTooltip]="reason"><ng-container [ngTemplateOutlet]="control" /></span>
      } @else {
        <ng-container [ngTemplateOutlet]="control" />
      }
    </ng-template>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    /* The status line keeps its box while empty, so the cards never move. */
    .tm-notification-settings__header {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      min-block-size: var(--space-8);
    }

    .tm-notification-settings__status {
      margin: 0;
      color: var(--text-muted);
      font-size: var(--text-sm);
    }

    .tm-notification-settings__status--failed {
      color: var(--error);
    }

    /* The skeleton cards stretch to the section's width, as the shell's and the loaded cards do. */
    .tm-notification-settings__loading,
    .tm-notification-settings__failed {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .tm-notification-settings__failed {
      align-items: flex-start;
    }

    .tm-notification-settings__failed p {
      margin: 0;
    }

    /* The grid: the type's label takes the room left; each channel's column is narrow and centred. */
    .tm-notification-settings__grid {
      inline-size: 100%;
      border-collapse: collapse;
    }

    .tm-notification-settings__grid th,
    .tm-notification-settings__grid td {
      padding-block: var(--space-2);
      padding-inline: var(--space-2);
      border-block-end: var(--border-width) solid var(--border-subtle);
    }

    .tm-notification-settings__grid tbody tr:last-child > * {
      border-block-end: none;
    }

    .tm-notification-settings__type {
      text-align: start;
      font-weight: var(--weight-regular);
      color: var(--text-body);
    }

    thead .tm-notification-settings__type,
    .tm-notification-settings__channel {
      color: var(--text-muted);
      font-size: var(--text-sm);
      font-weight: var(--weight-semibold);
    }

    .tm-notification-settings__channel,
    .tm-notification-settings__cell {
      inline-size: 8rem;
      text-align: center;
    }

    .tm-notification-settings__cell {
      white-space: nowrap;
    }

    .tm-notification-settings__dash {
      color: var(--text-muted);
    }

    .tm-notification-settings__always {
      display: block;
      color: var(--text-muted);
      font-size: var(--text-xs);
    }

    .tm-notification-settings__reasoned {
      display: inline-block;
    }

    /* A switch's channel name shows only where the columns' headers do not. */
    .tm-notification-settings__label {
      display: none;
    }

    /* Below md each type's switches stack under its label, each named by its channel. */
    @media (width < 768px) {
      .tm-notification-settings__grid,
      .tm-notification-settings__grid tbody,
      .tm-notification-settings__grid tr,
      .tm-notification-settings__grid th,
      .tm-notification-settings__grid td {
        display: block;
      }

      .tm-notification-settings__grid thead {
        display: none;
      }

      .tm-notification-settings__grid tr {
        padding-block: var(--space-2);
        border-block-end: var(--border-width) solid var(--border-subtle);
      }

      .tm-notification-settings__grid tbody tr:last-child {
        border-block-end: none;
      }

      .tm-notification-settings__grid th,
      .tm-notification-settings__grid td {
        inline-size: auto;
        padding-block: var(--space-1);
        border-block-end: none;
        text-align: start;
      }

      .tm-notification-settings__label {
        display: inline;
      }

      /* A channel-only type's inbox says nothing in a stack; its screen-reader line stays. */
      .tm-notification-settings__dash {
        display: none;
      }

      .tm-notification-settings__always {
        display: inline;
        margin-inline-start: var(--space-2);
      }
    }

    /* The test card: a channel, its address, then the button and its result. */
    .tm-notification-settings__tests {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .tm-notification-settings__test {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2) var(--space-4);
    }

    .tm-notification-settings__test-channel {
      font-weight: var(--weight-semibold);
      color: var(--text-body);
    }

    .tm-notification-settings__address {
      color: var(--text-muted);
      overflow-wrap: anywhere;
    }

    .tm-notification-settings__send {
      display: inline-flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      margin-inline-start: auto;
    }

    .tm-notification-settings__result {
      color: var(--text-muted);
      font-size: var(--text-sm);
    }

    .tm-notification-settings__result--sent {
      color: var(--success);
    }

    .tm-notification-settings__hidden {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `,
})
export class TmNotificationSettingsBody {
  /** The app's localization. */
  readonly #l10n = inject(TmLocalization);

  /** The app's URLs. */
  readonly #urls = inject(TmUrls);

  /** The component's injector, which the model and the error views resolve from. */
  readonly #injector = inject(Injector);

  /** The model of the current user, replaced when the input changes. */
  readonly #model = signal<TmNotificationSettingsModel | null>(null);

  /** Counts the error state's renders, so only the latest one shows. */
  #errorRun = 0;

  /** Why the error views did not load after a failed read, or `null`. */
  readonly #errorsFailure = signal<ɵTmRouteErrorKind | null>(null);

  /** Whose settings to edit: `'me'`, the signed-in user, or another user's id. */
  readonly user = input<TmNotificationSettingsUser>('me');

  /** The cards' heading level: 2 on the hub's page, 3 inside a dialog. */
  readonly headingLevel = input<TmCardHeadingLevel>(2);

  /** The section's model; `null` before the first render. */
  protected readonly model: Signal<TmNotificationSettingsModel | null> = this.#model.asReadonly();

  /** Where the error views render a failed read. */
  protected readonly errorSlot = viewChild('errorSlot', { read: ViewContainerRef });

  /** Why the error views did not load after a failed read, or `null`. */
  protected readonly errorsFailure: Signal<ɵTmRouteErrorKind | null> = this.#errorsFailure.asReadonly();

  /** The inline views of an entry point that did not load. */
  protected readonly views = ɵTM_ROUTE_ERROR_VIEWS;

  /** Whether a failed read shows the plain line: its failure is not an API error the error views can describe. */
  protected readonly fallbackError = computed(() => this.#model()?.loadError() === null);

  /** The profile page, where the user adds a contact address. */
  protected readonly profileUrl = computed(() => (this.#urls.tenantId() === null ? null : this.#urls.page('me/profile')));

  /** The ids of the section's described elements. */
  protected readonly ids = ((prefix: string) => ({
    lock: `${prefix}-lock`,
    always: (cell: TmNotificationCell): string => `${prefix}-always-${cell.type}`,
    test: (row: TmTestRow): string => `${prefix}-test-${row.channel.key}`,
  }))(`tm-notification-settings-${nextSection++}`);

  /** What is wrong, for the page's context: a failed read, or a save that failed. */
  readonly ɵerrors: Signal<readonly TmContextError[]> = computed(() => {
    const model = this.#model();
    const failures: TmContextError[] = [];
    if (model?.status() === 'error') {
      const error = model.loadError();
      failures.push({ source: 'request', code: error?.kind ?? 'unknown', message: error?.message ?? this.text('settings.loadFailed') });
    }
    if (model?.saveStatus() === 'failed') {
      failures.push({ source: 'request', code: 'save-failed', message: this.text('settings.failed') });
    }
    return failures;
  });

  /** The page's actions: turn a cell on or off as a click does, and send a test message. */
  readonly ɵactions: readonly TmPageAction[] = [
    {
      id: 'notifications.settings.set',
      labelKey: 'tm.attentionPages.actions.set',
      group: 'menu',
      enabled: computed(() => this.#model()?.status() === 'ready' && this.#model()?.lockReason() === null),
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
          return Promise.resolve({ outcome: 'refused', reason: 'disabled', message: this.text('settings.alwaysOn') });
        }
        const reason = untracked(model.lockReason);
        if (reason !== null) {
          return Promise.resolve({ outcome: 'refused', reason: 'disabled', message: reason });
        }
        // A reload over the shown values refuses changes until it ends.
        return Promise.resolve(model.set(cell, enabled) ? { outcome: 'done' } : { outcome: 'refused', reason: 'busy' });
      },
    },
    {
      id: 'notifications.settings.test',
      labelKey: 'tm.attentionPages.actions.test',
      group: 'menu',
      enabled: computed(() => this.#model()?.status() === 'ready' && this.#model()?.testReason() === null),
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
    },
  ];

  /** Creates the body, which reads the user's settings and follows the user input. */
  constructor() {
    tmRegisterAttentionPagesStrings(this.#l10n);

    // --- The model: one per user, created when the input is known; leaving sends what still waits ---
    effect((onCleanup) => {
      const user = this.user();
      const model = untracked(() => new TmNotificationSettingsModel(this.#injector, user));
      this.#model.set(model);
      onCleanup(() => model.dispose());
    });

    // --- A failed read: the error views' state in its slot, loaded on first use ---
    effect(() => {
      const model = this.#model();
      const slot = this.errorSlot();
      const error = model?.status() === 'error' ? model.loadError() : null;
      void this.#showError(model, slot, error);
    });
  }

  /** The section's model, for the shell and the page that host it; not for apps. */
  get ɵmodel(): Signal<TmNotificationSettingsModel | null> {
    return this.model;
  }

  /**
   * Reads one of the section's strings.
   *
   * @param key - The key under `tm.attentionPages`.
   * @param params - The message's arguments.
   * @returns The text.
   */
  protected text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.attentionPages.${key}`, params)();
  }

  /**
   * A cell's switch name, "{type}, {channel}".
   *
   * @param row - The cell's row.
   * @param index - The cell's column.
   * @returns The name.
   */
  protected cellName(row: TmNotificationRow, index: number): string {
    return this.text('settings.cell', { type: row.label(), channel: this.#model()!.channels[index].label() });
  }

  /**
   * Applies a switch's change.
   *
   * @param cell - The cell.
   * @param enabled - The switch's new value.
   */
  protected change(cell: TmNotificationCell, enabled: boolean): void {
    this.#model()?.set(cell, enabled);
  }

  /**
   * Sends a test message on a row's channel.
   *
   * @param row - The row.
   */
  protected send(row: TmTestRow): void {
    void this.#model()?.test(row);
  }

  /**
   * Renders a failed read's error state, with Try again reading again. When
   * the error views do not load, the worker's update check picks the inline
   * view that says why, whose Try again reads again.
   *
   * @param model - The model whose read failed.
   * @param slot - The state's slot, while it renders.
   * @param error - The failure, or `null`.
   */
  async #showError(model: TmNotificationSettingsModel | null, slot: ViewContainerRef | undefined, error: TmApiError | null): Promise<void> {
    const run = ++this.#errorRun;
    slot?.clear();
    this.#errorsFailure.set(null);
    if (model === null || slot === undefined || error === null) {
      return;
    }
    let errors: TmNotificationSettingsErrors;
    try {
      errors = await this.#injector.get(TM_NOTIFICATION_SETTINGS_ERRORS_LOAD)();
    } catch (failure) {
      // Spec 0024 §4.4: an update the worker has, or failed to get, means the client is outdated.
      this.#injector.get(TmTelemetry).error(failure, { source: 'tm-notification-settings' });
      const update = await this.#injector.get(ɵTM_API_UPDATES).check();
      if (run === this.#errorRun) {
        this.#errorsFailure.set(update === 'ready' || update === 'failed' ? 'client-outdated' : 'offline');
      }
      return;
    }
    if (run !== this.#errorRun) {
      return;
    }
    const { TmErrorPresenter, TmErrorState } = errors;
    const view = this.#injector.get(TmErrorPresenter).describe(error, { kind: 'read' });
    const state = slot.createComponent(TmErrorState, { injector: this.#injector });
    state.setInput('view', view);
    state.setInput('size', 'panel');
    state.instance.retry.subscribe(() => void model.reload());
  }
}
