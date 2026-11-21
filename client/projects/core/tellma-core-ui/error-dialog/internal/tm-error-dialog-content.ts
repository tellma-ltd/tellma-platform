// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterRenderEffect,
  Component,
  computed,
  ElementRef,
  ErrorHandler,
  inject,
  linkedSignal,
  type Signal,
  untracked,
} from '@angular/core';
import { ChevronDown, ChevronRight } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmButton, type TmButtonVariant } from '@tellma/core-ui/button';
import { TmReference } from '@tellma/core-ui/copy';
import { TmDescriptionList } from '@tellma/core-ui/description-list';
import { TmIcon } from '@tellma/core-ui/icon';
import { TM_MODAL_DATA, TmModalFooter } from '@tellma/core-ui/modal';

import type {
  TmErrorDialogAction,
  TmErrorDialogButton,
  TmErrorDialogOptions,
} from '../tm-error-dialog';

/** How many items show before the rest are counted. */
const ITEM_CAP = 10;

/** The footer button that closed the dialog. @internal */
export type ɵTmErrorDialogOutcome = 'primary' | 'secondary' | 'close';

/**
 * What the dialog shows, and which `show` it belongs to: the generation
 * moves on every replacement, so a run from an earlier one is ignored.
 * @internal
 */
export interface ɵTmErrorDialogState {
  /** The failure on screen. */
  readonly options: TmErrorDialogOptions;
  /** Counts the replacements. */
  readonly generation: number;
}

/**
 * What `TmErrorDialog` hands its dialog content.
 * @internal
 */
export interface ɵTmErrorDialogData {
  /** What the dialog shows; replaced in place by a later `show`. */
  readonly state: Signal<ɵTmErrorDialogState>;
  /** The message element's id, the dialog's `aria-describedby`. */
  readonly messageId: string;
  /** Closes the dialog with the footer button that answered it. */
  readonly close: (outcome: ɵTmErrorDialogOutcome) => void;
}

/** One footer button as rendered. */
interface FooterButton {
  /** What the dialog resolves to when the button closes it. */
  readonly key: ɵTmErrorDialogOutcome;
  /** The button's look. */
  readonly variant: TmButtonVariant;
  /** The button's text. */
  readonly label: string;
  /** The button's name while its run is pending, if it differs from the text. */
  readonly pendingLabel: string | undefined;
  /** The caller's run, if any. */
  readonly run: TmErrorDialogButton['run'];
}

/** A run in flight: which button, and which `show` started it. */
interface PendingRun {
  /** The button whose run it is. */
  readonly key: ɵTmErrorDialogOutcome;
  /** The generation the run belongs to. */
  readonly generation: number;
}

let nextRegionId = 0;

/**
 * The body and footer of a `TmErrorDialog` dialog: the message, the
 * items, the reference row and the Details disclosure, then the footer
 * buttons with their pending runs.
 *
 * @internal Opened exclusively by `TmErrorDialog`; never use directly.
 */
@Component({
  selector: 'tm-error-dialog-content',
  imports: [TmButton, TmDescriptionList, TmIcon, TmModalFooter, TmReference],
  template: `
    <div class="tm-error-dialog">
      <!-- The message, the dialog's description. -->
      <p class="tm-error-dialog__message" [id]="data.messageId">{{ options().message }}</p>
      <!-- The affected items: the first ten, then a count of the rest. -->
      @if (shownItems().length > 0) {
        <ul class="tm-error-dialog__items">
          @for (item of shownItems(); track $index) {
            <li>{{ item }}</li>
          }
        </ul>
        @if (moreLabel(); as more) {
          <p class="tm-error-dialog__more">{{ more }}</p>
        }
      }
      @if (options().reference; as reference) {
        <tm-reference class="tm-error-dialog__reference" [value]="reference" />
      }
      <!-- The Details disclosure: the details, then the actions. -->
      @if (hasDetails()) {
        <div class="tm-error-dialog__details">
          <button
            type="button"
            class="tm-error-dialog__toggle"
            [attr.aria-expanded]="detailsOpen()"
            [attr.aria-controls]="detailsOpen() ? regionId : null"
            (click)="detailsOpen.set(!detailsOpen())"
          >
            <tm-icon
              class="tm-error-dialog__chevron"
              [svg]="detailsOpen() ? glyphs.open : glyphs.closed"
            />
            {{ detailsLabel() }}
          </button>
          @if (detailsOpen()) {
            <div class="tm-error-dialog__region" [id]="regionId">
              @if (options().details?.length) {
                <dl tmDescriptionList layout="inline">
                  @for (detail of options().details; track $index) {
                    <div>
                      <dt>{{ detail.label }}</dt>
                      <dd>{{ detail.value }}</dd>
                    </div>
                  }
                </dl>
              }
              @if (options().actions?.length) {
                <div class="tm-error-dialog__actions">
                  @for (action of options().actions; track $index) {
                    <button tmButton type="button" variant="ghost" (click)="runAction(action)">
                      @if (action.icon; as icon) {
                        <tm-icon [name]="icon" />
                      }
                      {{ action.label }}
                    </button>
                  }
                </div>
              }
            </div>
          }
        </div>
      }
    </div>
    <!-- The footer: the caller's buttons, then Close. Focus starts on the
         primary-styled button (Close when no primary is given). Each label
         cell also holds the pending name, invisibly, so the button is as
         wide as the wider of the two and never resizes when its run
         starts. -->
    <div tmModalFooter (focusin)="onFooterFocus($event)">
      @for (button of footer(); track $index) {
        <button
          tmButton
          type="button"
          class="tm-error-dialog__button"
          [attr.data-key]="button.key"
          [variant]="button.variant"
          [pending]="pending()?.key === button.key"
          [disabled]="pending() !== null && pending()?.key !== button.key && button.key !== 'close'"
          [attr.aria-label]="
            pending()?.key === button.key ? (button.pendingLabel ?? button.label) : null
          "
          [attr.cdkFocusInitial]="button.variant === 'primary' ? '' : null"
          (click)="activate(button)"
        >
          <span class="tm-error-dialog__label">
            <span>{{ button.label }}</span>
            @if (button.pendingLabel) {
              <span class="tm-error-dialog__label-reserve" aria-hidden="true">{{
                button.pendingLabel
              }}</span>
            }
          </span>
        </button>
      }
    </div>
  `,
  // The Details list's look comes with the dialog, scoped to this view, so
  // an app that uses only the error dialog needs no global stylesheet.
  styleUrls: ['./tm-error-dialog-content.css', '../../styles/tm-description-list.css'],
})
export class ɵTmErrorDialogContent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorHandler = inject(ErrorHandler);

  /** The dialog's data from the service. */
  protected readonly data = inject<ɵTmErrorDialogData>(TM_MODAL_DATA);

  /** The failure on screen. */
  protected readonly options = computed(() => this.data.state().options);

  /** The disclosure's region id. */
  protected readonly regionId = `tm-error-dialog-details-${nextRegionId++}`;

  /** The disclosure's glyphs: pointing down when open, along the line when closed. */
  protected readonly glyphs = {
    open: ChevronDown,
    closed: { node: ChevronRight, mirrorInRtl: true },
  };

  /** The disclosure's label. */
  protected readonly detailsLabel = this.translate('errorDialog.details');

  /** Whether the Details disclosure is open; every replacement closes it. */
  protected readonly detailsOpen = linkedSignal({
    source: this.data.state,
    computation: () => false,
  });

  /** The run in flight, if any; a replacement drops it. */
  protected readonly pending = linkedSignal<ɵTmErrorDialogState, PendingRun | null>({
    source: this.data.state,
    computation: () => null,
  });

  /** The footer position that last held focus. */
  private footerFocus = 0;

  /** The items listed. */
  protected readonly shownItems = computed(() => (this.options().items ?? []).slice(0, ITEM_CAP));

  /** "and N more" for the items past the cap, or null when none are. */
  protected readonly moreLabel = computed(() => {
    const rest = (this.options().items?.length ?? 0) - ITEM_CAP;
    return rest > 0 ? this.translate('errorDialog.more', { count: rest })() : null;
  });

  /** Whether the disclosure has anything to hold. */
  protected readonly hasDetails = computed(
    () => (this.options().details?.length ?? 0) > 0 || (this.options().actions?.length ?? 0) > 0,
  );

  /** The footer: primary, secondary, then Close, which is primary-styled when no primary is given. */
  protected readonly footer = computed<readonly FooterButton[]>(() => {
    const { primary, secondary } = this.options();
    const buttons: FooterButton[] = [];
    if (primary !== undefined) {
      buttons.push(footerButton('primary', 'primary', primary));
    }
    if (secondary !== undefined) {
      buttons.push(footerButton('secondary', 'secondary', secondary));
    }
    buttons.push({
      key: 'close',
      variant: primary === undefined ? 'primary' : 'secondary',
      label: this.translate('errorDialog.close')(),
      pendingLabel: undefined,
      run: undefined,
    });
    return buttons;
  });

  constructor() {
    // After a replacement renders, focus that fell out of the dialog (its
    // button was removed) returns to the same footer position, or the
    // last one when the footer got shorter.
    afterRenderEffect(() => {
      this.data.state();
      untracked(() => this.restoreFocus());
    });
  }

  /** Remembers which footer position holds focus. */
  protected onFooterFocus(event: FocusEvent): void {
    const buttons = this.footerButtons();
    const index = buttons.indexOf(event.target as HTMLElement);
    if (index >= 0) {
      this.footerFocus = index;
    }
  }

  /**
   * A footer click: Close and a button without `run` close at once; a
   * button with `run` turns pending until the run settles, then closes on
   * `true` or returns to idle on `false`. A run from an earlier `show`,
   * or one that settles after the dialog closed, changes nothing.
   */
  protected activate(button: FooterButton): void {
    if (this.pending()?.key === button.key) {
      return;
    }
    const run = button.run;
    if (run === undefined) {
      this.data.close(button.key);
      return;
    }
    const generation = this.data.state().generation;
    this.pending.set({ key: button.key, generation });
    // Whether this run still speaks for the dialog on screen.
    const current = (): boolean => {
      const pending = this.pending();
      return pending !== null && pending.key === button.key && pending.generation === generation;
    };
    let result: Promise<boolean>;
    try {
      result = run();
    } catch (error) {
      this.pending.set(null);
      this.errorHandler.handleError(error);
      return;
    }
    result.then(
      (closes) => {
        if (!current()) {
          return;
        }
        if (closes) {
          this.data.close(button.key);
        } else {
          this.pending.set(null);
        }
      },
      (error: unknown) => {
        if (current()) {
          this.pending.set(null);
        }
        this.errorHandler.handleError(error);
      },
    );
  }

  /** A Details action: runs, and closes the dialog when it says so. */
  protected runAction(action: TmErrorDialogAction): void {
    try {
      action.run();
    } catch (error) {
      this.errorHandler.handleError(error);
    }
    if (action.closes === true) {
      this.data.close('close');
    }
  }

  /** The footer's buttons, in order. */
  private footerButtons(): HTMLElement[] {
    return Array.from(this.host.querySelectorAll<HTMLElement>('.tm-error-dialog__button'));
  }

  /** Returns lost focus to the footer position that last held it. */
  private restoreFocus(): void {
    const active = this.host.ownerDocument.activeElement;
    const panel = this.host.closest('[role="alertdialog"]') ?? this.host;
    if (active !== null && active !== this.host.ownerDocument.body && panel.contains(active)) {
      return;
    }
    if (active !== null && active !== this.host.ownerDocument.body) {
      // Focus is somewhere else entirely (another layer): leave it.
      return;
    }
    const buttons = this.footerButtons();
    buttons[Math.min(this.footerFocus, buttons.length - 1)]?.focus();
  }
}

/** A caller's footer button as rendered. */
function footerButton(
  key: 'primary' | 'secondary',
  variant: TmButtonVariant,
  button: TmErrorDialogButton,
): FooterButton {
  return {
    key,
    variant,
    label: button.label,
    pendingLabel:
      button.pendingLabel !== undefined && button.pendingLabel !== button.label
        ? button.pendingLabel
        : undefined,
    run: button.run,
  };
}
