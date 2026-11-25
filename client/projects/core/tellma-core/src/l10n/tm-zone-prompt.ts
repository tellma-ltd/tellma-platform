// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  Injector,
  signal,
  untracked,
  type Signal,
  type WritableSignal,
} from '@angular/core';
import { TmAlert } from '@tellma/core-ui/alert';
import { TmButton } from '@tellma/core-ui/button';
import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { tmFormatZoneName, tmFormatZoneOffset } from '@tellma/core-ui/l10n';
import { TM_MODAL_DATA, TmModal, TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';

import { TmApiError } from '../api/tm-api-error';
import { TM_ERRORS_LOAD } from '../api/tm-api-seams';
import type { TmStringTree } from '../app/tm-app-config';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmLocalization } from './tm-localization';

// The device-zone prompt (spec 0024 §7.8), a chunk of its own that the check
// loads only when a change of the device's zone asks: a small dialog that
// names both zones with their offsets, the per-device option with its hint,
// Keep current and Change time zone. Change time zone turns busy in the
// click's frame and stays busy until the write settles; meanwhile nothing
// dismisses the dialog. A zone outside the server's set, or any other
// failure, shows inline, and the dialog stays.

/** The prompt's English strings, under `tm.zonePrompt`. */
export const TM_ZONE_PROMPT_STRINGS_EN = {
  title: "Use your device's time zone?",
  message:
    'Your device is now on {deviceZone} ({deviceOffset}). {displayName} shows times in {displayZone} ({displayOffset}).',
  auto: 'Change it automatically next time',
  autoHint: 'Only on this device. You can turn this off in My settings.',
  change: 'Change time zone',
  keep: 'Keep current',
  unknown: "{displayName} doesn't support your device's time zone. Choose one in My settings.",
  failed: "Couldn't change your time zone. Try again.",
} as const satisfies TmStringTree;

/** What the check asks the prompt. */
export interface TmZoneAsk {
  /** The device's zone, through the aliases. */
  readonly device: string;
  /** The display zone. */
  readonly display: string;
  /** The instant the names and offsets are those of, in epoch milliseconds. */
  readonly at: number;
  /**
   * Changes the user's time zone to the device's, and turns the option on
   * when the box is ticked.
   *
   * @param auto - Whether the box is ticked.
   * @returns `ok`, or `unknown` when the server's zone set lacks the zone.
   * @throws The write's failure otherwise.
   */
  readonly change: (auto: boolean) => Promise<'ok' | 'unknown'>;
}

/** The user's answer to the prompt. */
export interface TmZoneAnswer {
  /**
   * `change` once the zone changed, `keep` for Keep current, `dismiss` for
   * Escape or the close button, and `null` when history navigation closed
   * the dialog first.
   */
  readonly answer: 'change' | 'keep' | 'dismiss' | null;
  /** Whether the box was ticked when the user answered. */
  readonly auto: boolean;
}

/** What the inline failure shows: a heading, when the failure has one, a message, and the server's errors. */
interface TmZoneFailure {
  /** The heading, or `undefined` for a message alone. */
  readonly heading: string | undefined;
  /** The message. */
  readonly message: string;
  /** The server's errors, each in the user's language; empty when it named none. */
  readonly items: readonly string[];
}

/** What the dialog's content reads and shares with its opener. */
interface TmZonePromptData {
  /** What the check asks. */
  readonly ask: TmZoneAsk;
  /** The id of the message, which describes the dialog. */
  readonly messageId: string;
  /** Whether Change time zone is running, which keeps the dialog open. */
  readonly busy: WritableSignal<boolean>;
  /** Whether the box is ticked. */
  readonly auto: WritableSignal<boolean>;
}

let nextId = 0;

/**
 * The device-zone prompt's content: the message, the per-device option,
 * the inline failure, and the two answers. Internal.
 */
@Component({
  selector: 'tm-zone-prompt',
  imports: [TmAlert, TmButton, TmCheckbox, TmModalFooter],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .tm-zone-prompt {
      display: grid;
      gap: var(--space-3);
      color: var(--text-body);
      font-size: var(--modal-message-font-size);
      overflow-wrap: anywhere;
    }
    .tm-zone-prompt p {
      margin: 0;
    }
    .tm-zone-prompt__items {
      margin: var(--space-1) 0 0;
      padding-inline-start: var(--space-5);
    }
    .tm-zone-prompt__hint {
      margin-block-start: calc(-1 * var(--space-2));
      padding-inline-start: var(--space-6);
      color: var(--text-secondary);
      font-size: var(--field-hint-font-size);
    }
  `,
  template: `
    <div class="tm-zone-prompt">
      <!-- The message, which describes the dialog: both zones, each with its offset now. -->
      <p [id]="data.messageId">{{ message() }}</p>
      <!-- The per-device option, described by its hint. -->
      <tm-checkbox [(checked)]="data.auto" [disabled]="data.busy()" [aria-describedby]="hintId">{{ text.auto() }}</tm-checkbox>
      <p class="tm-zone-prompt__hint" [id]="hintId">{{ text.autoHint() }}</p>
      <!-- Why Change time zone failed, announced at once. -->
      @if (failure(); as failure) {
        <tm-alert kind="error" live="assertive" [heading]="failure.heading">
          {{ failure.message }}
          <!-- The server's errors, as the error views list them. -->
          @if (failure.items.length > 0) {
            <ul class="tm-zone-prompt__items">
              @for (item of failure.items; track $index) {
                <li>{{ item }}</li>
              }
            </ul>
          }
        </tm-alert>
      }
    </div>
    <!-- Keep current, and Change time zone, which takes the focus and turns busy at once. -->
    <div tmModalFooter>
      <button tmButton type="button" variant="secondary" [disabled]="data.busy()" (click)="keep()">{{ text.keep() }}</button>
      <button tmButton type="button" variant="primary" cdkFocusInitial [pending]="data.busy()" (click)="change()">
        {{ text.change() }}
      </button>
    </div>
  `,
})
export class ɵTmZonePrompt {
  /** What the dialog shows and shares. */
  protected readonly data = inject<TmZonePromptData>(TM_MODAL_DATA);

  /** The dialog, closed with the answer. */
  readonly #ref = inject<TmModalRef<'change' | 'keep'>>(TmModalRef);
  readonly #l10n = inject(TmLocalization);
  readonly #errors = inject(TM_ERRORS_LOAD);
  readonly #telemetry = inject(TmTelemetry);
  readonly #injector = inject(Injector);

  /** The id of the option's hint. */
  protected readonly hintId = `${this.data.messageId}-hint`;

  /** The labels. */
  protected readonly text = {
    auto: this.#l10n.ɵtext('tm.zonePrompt.auto'),
    autoHint: this.#l10n.ɵtext('tm.zonePrompt.autoHint'),
    change: this.#l10n.ɵtext('tm.zonePrompt.change'),
    keep: this.#l10n.ɵtext('tm.zonePrompt.keep'),
  };

  /** The message, with both zones' names and offsets in the active locale. */
  protected readonly message: Signal<string> = computed(() => {
    const { device, display, at } = this.data.ask;
    const locale = this.#l10n.locale();
    return this.#l10n.ɵtext('tm.zonePrompt.message', {
      deviceZone: tmFormatZoneName(device, locale, at) || device,
      deviceOffset: tmFormatZoneOffset(at, locale, device),
      displayName: this.#l10n.displayName(),
      displayZone: tmFormatZoneName(display, locale, at) || display,
      displayOffset: tmFormatZoneOffset(at, locale, display),
    })();
  });

  /** The inline failure, while one shows. */
  protected readonly failure = signal<TmZoneFailure | null>(null);

  /** Keep current: closes, never turning the option on. */
  protected keep(): void {
    if (!untracked(this.data.busy)) {
      this.#ref.close('keep');
    }
  }

  /** Change time zone: busy at once, then the write; the dialog closes once it lands, or shows why not. */
  protected async change(): Promise<void> {
    if (untracked(this.data.busy)) {
      return;
    }
    this.data.busy.set(true);
    this.failure.set(null);
    try {
      const outcome = await this.data.ask.change(untracked(this.data.auto));
      if (outcome === 'ok') {
        this.#ref.close('change');
        return;
      }
      this.failure.set({
        heading: undefined,
        message: untracked(this.#l10n.ɵtext('tm.zonePrompt.unknown', { displayName: untracked(this.#l10n.displayName) })),
        items: [],
      });
    } catch (error) {
      this.failure.set(await this.#describe(error));
    } finally {
      this.data.busy.set(false);
    }
  }

  /**
   * A failure's inline text: the error views' description of a save that
   * failed, with the server's errors, or the prompt's own line when the
   * failure is not the server's or the error views do not load.
   *
   * @param error - The failure.
   * @returns The heading, the message and the server's errors.
   */
  async #describe(error: unknown): Promise<TmZoneFailure> {
    const fallback: TmZoneFailure = { heading: undefined, message: untracked(this.#l10n.ɵtext('tm.zonePrompt.failed')), items: [] };
    if (!(error instanceof TmApiError)) {
      this.#telemetry.error(error, { where: 'tm.l10n.zone-prompt' });
      return fallback;
    }
    try {
      const { TmErrorPresenter } = await this.#errors();
      const view = this.#injector.get(TmErrorPresenter).describe(error, { kind: 'save' });
      const message = untracked(view.message);
      const items = untracked(view.items);
      return message === ''
        ? { heading: undefined, message: untracked(view.title), items }
        : { heading: untracked(view.title), message, items };
    } catch {
      return fallback;
    }
  }
}

/**
 * Asks whether to switch the user's time zone to the device's: opens the
 * prompt and settles with the answer once it closes. Internal: the
 * device-zone check calls it, under the user's prompt lock.
 *
 * @param injector - The app's injector.
 * @param ask - The zones, the instant and the change.
 * @returns The answer, and whether the box was ticked then.
 */
export async function ɵtmAskZone(injector: Injector, ask: TmZoneAsk): Promise<TmZoneAnswer> {
  const l10n = injector.get(TmLocalization);
  l10n.ɵregisterStrings('zonePrompt', TM_ZONE_PROMPT_STRINGS_EN);
  const messageId = `tm-zone-prompt-${nextId++}`;
  const busy = signal(false);
  const auto = signal(false);
  const title = l10n.ɵtext('tm.zonePrompt.title');
  const ref = injector.get(TmModal).open<'change' | 'keep'>(ɵTmZonePrompt, {
    size: 'sm',
    role: 'dialog',
    title: untracked(title),
    describedBy: messageId,
    // Keep current, Escape and the close button answer; a stray click beside the dialog does not.
    backdropDismiss: false,
    // A running change keeps the dialog open until it settles.
    canDismiss: () => !untracked(busy),
    data: { ask, messageId, busy, auto } satisfies TmZonePromptData,
  });
  // --- The title follows a language switch while the dialog is open ---
  const retitle = effect(() => ref.ɵsetTitle(title()), { injector });
  const result = await ref.closed;
  retitle.destroy();
  const ticked = untracked(auto);
  if (result.via !== 'api') {
    return { answer: 'dismiss', auto: ticked };
  }
  return { answer: result.value ?? null, auto: ticked };
}
