// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterRenderEffect,
  booleanAttribute,
  Component,
  computed,
  DestroyRef,
  type ElementRef,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
  ViewContainerRef,
} from '@angular/core';
import { AriaDescriber } from '@angular/cdk/a11y';
import { Check, Copy } from 'lucide';

import { TM_UI_TRANSLATE, type TmControlSize } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmKeysText } from '@tellma/core-ui/kbd';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';
import { TmTooltip, ɵTmTooltipController } from '@tellma/core-ui/tooltip';

/** How long the button says "Copied", in milliseconds. */
const COPIED_MS = 2000;

/**
 * Copies a value in one click. The icon turns to a check and the label (or
 * the tooltip, for the icon-only default) reads "Copied" for two seconds,
 * announced politely; the button reserves the wider of its two labels, so
 * nothing moves.
 *
 * When the clipboard is out of reach (no secure context, permission
 * denied), the text is selected in a hidden read-only input over the
 * button and a tooltip says "Press Ctrl+C to copy" ("⌘C" on a Mac); a copy
 * from there counts as a success.
 *
 * `text` may be a function, for a value that is costly to build: it runs
 * on the click, and a promise it returns is handed to the clipboard
 * without losing the click's permission.
 *
 * @tmGroup control
 * @tmA11yNotes A native button named by its label ("Copy" by default);
 *   icon-only, the label is its `aria-label` and its tooltip. "Copied" is
 *   announced through a polite live region. The manual fallback focuses a
 *   read-only input named "Press Ctrl+C to copy", with the text selected;
 *   Escape returns focus to the button.
 */
@Component({
  selector: 'tm-copy-button',
  imports: [TmButton, TmIcon, TmTooltip],
  // Icon-only, the button carries a tooltip with the current label;
  // labelled, the two labels share one grid cell so the button keeps the
  // wider one's width. The status region is always present (a region that
  // appears with its text is not reliably announced). The manual-copy
  // input exists only while the fallback is up.
  template: `
    @if (showLabel()) {
      <button
        #button
        tmButton
        variant="ghost"
        type="button"
        class="tm-copy-button__button"
        [size]="size()"
        (click)="copy()"
      >
        <tm-icon [svg]="isCopied() ? checkGlyph : copyGlyph" />
        <span class="tm-copy-button__labels">
          <span class="tm-copy-button__label" [class.tm-copy-button__label--off]="isCopied()">{{
            idleLabel()
          }}</span>
          <span class="tm-copy-button__label" [class.tm-copy-button__label--off]="!isCopied()">{{
            copiedLabel()
          }}</span>
        </span>
      </button>
    } @else {
      <button
        #button
        tmButton
        variant="ghost"
        type="button"
        class="tm-copy-button__button tm-copy-button__button--icon"
        [size]="size()"
        [attr.aria-label]="idleLabel()"
        [tmTooltip]="isCopied() ? copiedLabel() : idleLabel()"
        (click)="copy()"
      >
        <tm-icon [svg]="isCopied() ? checkGlyph : copyGlyph" />
      </button>
    }
    <span class="tm-copy-button__status" role="status">{{ announcement() }}</span>
    @if (manualText(); as text) {
      <input
        #manual
        class="tm-copy-button__manual"
        readonly
        tabindex="-1"
        [value]="text"
        [attr.aria-label]="manualPrompt()"
        (copy)="onManualCopy()"
        (blur)="closeManual()"
      />
    }
  `,
  styleUrl: './tm-copy-button.css',
  host: {
    class: 'tm-copy-button',
    '[class.tm-copy-button--copied]': 'isCopied()',
  },
})
export class TmCopyButton {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly describer = inject(AriaDescriber);
  private readonly viewContainer = inject(ViewContainerRef);

  /** What is copied: the text, or a function that builds it on the click. */
  readonly text = input.required<string | (() => string | Promise<string>)>();

  /** The accessible name, and the visible label with `showLabel`. Default "Copy". */
  readonly label = input<string | undefined>(undefined);

  /** Show the label beside the icon; by default the button is icon-only with a tooltip. */
  readonly showLabel = input(false, { transform: booleanAttribute });

  /** The size step, as for any `tmButton`. */
  readonly size = input<TmControlSize | undefined>(undefined);

  /** Emits after a successful copy, by the clipboard or by the manual fallback. */
  readonly copied = output<void>();

  /** The copy glyph (the library's own data). */
  protected readonly copyGlyph = Copy;

  /** The check glyph a copy turns the icon into. */
  protected readonly checkGlyph = Check;

  /** Whether the button says "Copied" right now. */
  protected readonly isCopied = signal(false);

  /** The label at rest: the consumer's, else "Copy". */
  protected readonly idleLabel = computed(() => this.label() ?? this.translate('copy.copy')());

  /** The label after a copy. */
  protected readonly copiedLabel = this.translate('copy.copied');

  /** The live region's text: "Copied" for the two seconds after a copy. */
  protected readonly announcement = computed(() => (this.isCopied() ? this.copiedLabel() : ''));

  /** "Press Ctrl+C to copy", with the platform's keys. */
  protected readonly manualPrompt = computed(() =>
    this.translate('copy.manual', { keys: tmKeysText('Mod+C') })(),
  );

  /** The text the manual fallback offers, or `null` while it is down. */
  protected readonly manualText = signal<string | null>(null);

  /** The manual-copy input while it is rendered. */
  private readonly manualInput = viewChild<ElementRef<HTMLInputElement>>('manual');

  /** The button, labelled or icon-only, for returning focus to it. */
  private readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('button');

  /** Ends the "Copied" state. */
  private copiedTimer: ReturnType<typeof setTimeout> | undefined;

  /** The tooltip pointing at the manual-copy input, while it is up. */
  private manualTooltip: ɵTmTooltipController | null = null;

  /** Releases the fallback's place on the shared Escape stack. */
  private releaseEscape: (() => void) | null = null;

  constructor() {
    // The fallback, once rendered: focus the input, select its text and
    // point at it with the tooltip.
    afterRenderEffect(() => {
      const element = this.manualInput()?.nativeElement;
      if (element === undefined) {
        return;
      }
      untracked(() => {
        element.focus();
        element.select();
        this.manualTooltip?.destroy();
        this.manualTooltip = new ɵTmTooltipController(
          element,
          () => this.viewContainer,
          this.describer,
        );
        // No description: the input's name already says it.
        this.manualTooltip.setText(this.manualPrompt(), []);
        this.manualTooltip.show();
        // Escape belongs to the fallback, above its own tooltip: one press
        // returns focus to the button, which takes both down.
        this.releaseEscape?.();
        this.releaseEscape = tmPushEscapeDismissal(() => this.returnFocus());
      });
    });
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.copiedTimer);
      this.dropManualTooltip();
    });
  }

  /**
   * Copies the text. A string goes to `writeText` inside the click; a
   * function's promise goes to the clipboard as a pending item where the
   * browser takes one, so the click's permission is not lost while it
   * resolves. Any refusal brings up the manual fallback.
   */
  protected copy(): void {
    const source = this.text();
    let produced: string | Promise<string>;
    try {
      produced = typeof source === 'string' ? source : source();
    } catch {
      // The consumer's builder failed: there is nothing to copy.
      return;
    }
    const clipboard = typeof navigator === 'undefined' ? undefined : navigator.clipboard;

    // A ready string: write it now, in the click.
    if (typeof produced === 'string') {
      this.write(clipboard, produced);
      return;
    }

    // A pending string: hand the promise to the clipboard if it takes one.
    const pending = produced;
    const fallBackLater = (): void => {
      pending.then((text) => this.fallBack(text)).catch(() => undefined);
    };
    if (clipboard?.write !== undefined && typeof ClipboardItem !== 'undefined') {
      try {
        const item = new ClipboardItem({
          'text/plain': pending.then((text) => new Blob([text], { type: 'text/plain' })),
        });
        clipboard.write([item]).then(() => this.succeed(), fallBackLater);
      } catch {
        // Some engines throw from the constructor or from write itself.
        fallBackLater();
      }
      return;
    }
    pending.then((text) => this.write(clipboard, text)).catch(() => undefined);
  }

  /** Writes plain text, falling back to the manual copy on any refusal. */
  private write(clipboard: Clipboard | undefined, text: string): void {
    if (clipboard?.writeText === undefined) {
      this.fallBack(text);
      return;
    }
    try {
      clipboard.writeText(text).then(
        () => this.succeed(),
        () => this.fallBack(text),
      );
    } catch {
      this.fallBack(text);
    }
  }

  /** Shows "Copied" for two seconds, announces it, and emits `copied`. */
  private succeed(): void {
    clearTimeout(this.copiedTimer);
    this.isCopied.set(true);
    this.copiedTimer = setTimeout(() => this.isCopied.set(false), COPIED_MS);
    this.copied.emit();
  }

  /** Brings up the manual fallback with `text` selected. */
  private fallBack(text: string): void {
    this.manualText.set(text);
  }

  /**
   * A copy from the manual input is a success. The input goes after the
   * event has run its course, so the browser copies the selection first.
   */
  protected onManualCopy(): void {
    setTimeout(() => {
      this.returnFocus();
      this.succeed();
    });
  }

  /** Returns focus to the button, which takes the manual input down. */
  protected returnFocus(): void {
    this.button().nativeElement.focus();
    this.closeManual();
  }

  /** Takes the manual fallback down. */
  protected closeManual(): void {
    this.dropManualTooltip();
    this.manualText.set(null);
  }

  /** Tears down the manual input's tooltip and its Escape registration. */
  private dropManualTooltip(): void {
    this.releaseEscape?.();
    this.releaseEscape = null;
    this.manualTooltip?.destroy();
    this.manualTooltip = null;
  }
}
