// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  afterRenderEffect,
  ChangeDetectorRef,
  Component,
  computed,
  contentChildren,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';

import { TM_ACTIVE_LOCALE, TM_UI_TRANSLATE, TmL10n, ɵtmControlSize } from '@tellma/core-ui';
import { TmTooltip } from '@tellma/core-ui/tooltip';

import { TM_CHIP_SET, type TmChipSetRef } from './internal/tm-chip-set-ref';
import { TmChip } from './tm-chip';

/** How a {@link TmChipSet} lays out chips that do not fit one line. */
export type TmChipSetOverflow = 'wrap' | 'collapse' | 'scroll';

/** Half a pixel: sub-pixel widths that round the same way still fit. */
const FIT_TOLERANCE = 0.5;

/**
 * Lays chips out as a list with one tab stop. The arrow keys move between
 * the chips' buttons (a label button before its remove button); an arrow
 * past either end emits `exit`, so a host input beside the set can take
 * focus back. When a chip that holds focus is removed, focus moves to the
 * next chip, else the previous one, else the set emits `exit('end')`.
 *
 * `overflow` decides what happens when the chips do not fit one line:
 * `wrap` (the default) wraps them; `collapse` keeps one line and hides the
 * chips that do not fit behind a trailing "+N" chip, whose name and tooltip
 * list them; `scroll` keeps one line that scrolls sideways with its
 * scrollbar hidden, bringing the focused chip into view. The collapse fit
 * is recomputed when the set is resized and when the chips change, never
 * on a keystroke.
 *
 * @tmGroup indicator
 * @tmA11yNotes `role="list"`, named by `ariaLabel`; each chip is a
 *   `listitem`. One tab stop: Left and Right (swapped in RTL) and Home and
 *   End move between the chips' buttons, a label button before its remove
 *   button; an arrow past either end emits `exit`. Delete or Backspace
 *   removes the focused chip and focus moves to the next chip, else the
 *   previous one, else out of the set. The "+N" chip of `collapse` is a
 *   button named "3 more: Service, Sale, Operation", with the same tooltip.
 */
@Component({
  selector: 'tm-chip-set',
  imports: [TmTooltip],
  // The chips, then (in collapse mode) the "+N" chip. Its visible count
  // shares a grid cell with a hidden "+{total}" probe, so the chip is as
  // wide as it can ever get and the fit never changes as the count does.
  template: `
    <ng-content />
    @if (overflow() === 'collapse') {
      <div
        #more
        role="listitem"
        class="tm-chip-set__more"
        [attr.data-size]="moreSize()"
        [class.tm-chip-set__more--idle]="hiddenCount() === 0"
      >
        <button
          type="button"
          class="tm-chip-set__more-button"
          [attr.aria-label]="moreName()"
          [tmTooltip]="moreName()"
        >
          <span class="tm-chip-set__more-count" dir="ltr">+{{ hiddenText() }}</span>
          <span class="tm-chip-set__more-probe" dir="ltr" aria-hidden="true"
            >+{{ totalText() }}</span
          >
        </button>
      </div>
    }
  `,
  styleUrl: './tm-chip-set.css',
  providers: [{ provide: TM_CHIP_SET, useExisting: TmChipSet }],
  host: {
    class: 'tm-chip-set',
    role: 'list',
    '[attr.aria-label]': 'ariaLabel() ?? null',
    '[attr.data-overflow]': 'overflow()',
    '(keydown)': 'onKeydown($event)',
    '(focusin)': 'onFocusin($event)',
    '(focusout)': 'onFocusout($event)',
  },
})
export class TmChipSet implements TmChipSetRef {
  /** The set's own element. */
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  private readonly injector = inject(Injector);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly l10n = inject(TmL10n);
  private readonly locale = inject(TM_ACTIVE_LOCALE);

  /** What happens when the chips do not fit one line. Default `wrap`. */
  readonly overflow = input<TmChipSetOverflow>('wrap');

  /** The list's accessible name ("Filters", "Chosen agents"). */
  readonly ariaLabel = input<string | undefined>(undefined);

  /**
   * Emits `'start'` or `'end'` when an arrow key moves past the first or
   * last chip, and `'end'` when the last chip holding focus is removed, so
   * a host can refocus its input.
   */
  readonly exit = output<'start' | 'end'>();

  /** The chips, in order. */
  private readonly chips = contentChildren(TmChip, { descendants: true });

  /** The "+N" chip of collapse mode, while rendered. */
  private readonly moreItem = viewChild<ElementRef<HTMLElement>>('more');

  /** How many leading chips show in collapse mode; every chip otherwise. */
  private readonly visibleCount = signal(Number.POSITIVE_INFINITY);

  /** How many chips the "+N" chip stands for. */
  protected readonly hiddenCount = computed(() =>
    Math.max(0, this.chips().length - this.visibleCount()),
  );

  /** The hidden count in the locale's digits. */
  protected readonly hiddenText = computed(() => this.l10n.formatNumber(this.hiddenCount()));

  /** The chip count in the locale's digits: the probe that fixes the "+N" chip's width. */
  protected readonly totalText = computed(() => this.l10n.formatNumber(this.chips().length));

  /** The "+N" chip's name and tooltip: "3 more: Service, Sale, Operation". */
  protected readonly moreName = computed(() => {
    const hidden = this.chips()
      .slice(this.chips().length - this.hiddenCount())
      .map((chip) => chip.label());
    const list = new Intl.ListFormat(this.locale(), { type: 'unit', style: 'short' });
    return this.translate('chip.more', { count: hidden.length, labels: list.format(hidden) })();
  });

  /** The control that holds the set's tab stop, once one has been focused. */
  private current: HTMLElement | null = null;

  /** The chip that holds focus, kept by the focus events. */
  private focusedChip: TmChip | null = null;

  /** The chips as last rendered: a chip being destroyed is still among them. */
  private renderedChips: readonly TmChip[] = [];

  /** The size step the "+N" chip shares with chips of no explicit size. */
  protected readonly moreSize = ɵtmControlSize(signal(undefined));

  /** Measures the collapse fit; present only in collapse mode. */
  private observer: ResizeObserver | null = null;

  constructor() {
    // Collapse mode watches the set and every chip: a resize, or a chip
    // added, removed or relabelled, re-measures (observing an element
    // reports it once, so a new chip measures too). Other modes show
    // every chip and watch nothing.
    afterRenderEffect({
      read: () => {
        const collapse = this.overflow() === 'collapse';
        const chips = this.chips();
        untracked(() => {
          this.observer?.disconnect();
          if (!collapse) {
            this.observer = null;
            this.applyVisibleCount(Number.POSITIVE_INFINITY, false);
            return;
          }
          this.observer ??= new ResizeObserver(() => this.measure());
          this.observer.observe(this.host);
          for (const chip of chips) {
            this.observer.observe(chip.ɵelement);
          }
        });
      },
    });
    // The single tab stop: whenever the chips, their controls or the
    // collapse fit change, exactly one control is tabbable.
    afterRenderEffect({
      write: () => {
        const chips = this.chips();
        for (const chip of chips) {
          chip.ɵkind();
          chip.removable();
        }
        this.hiddenCount();
        untracked(() => {
          this.renderedChips = chips;
          this.updateTabStop();
        });
      },
    });
    inject(DestroyRef).onDestroy(() => this.observer?.disconnect());
  }

  /** Focuses the last visible chip (a host calls it on Backspace in its empty input). */
  focusLast(): void {
    const chips = this.chips().slice(0, this.chips().length - this.hiddenCount());
    for (let index = chips.length - 1; index >= 0; index--) {
      const target = chips[index].ɵfocusTarget();
      if (target !== null) {
        this.focusControl(target);
        return;
      }
    }
  }

  /**
   * A chip went. If it held focus, focus moves to the chip now at its
   * place (the next one), else the one before it, else out of the set
   * through `exit('end')`.
   * @internal
   */
  chipDestroyed(chip: object): void {
    if (chip !== this.focusedChip) {
      return;
    }
    this.focusedChip = null;
    // The chips as last rendered still list the one being destroyed.
    const index = this.renderedChips.indexOf(chip as TmChip);
    afterNextRender(
      () => {
        if (this.host.contains(document.activeElement) && document.activeElement !== this.host) {
          return;
        }
        const chips = this.chips().slice(0, this.chips().length - this.hiddenCount());
        const target =
          chips.slice(Math.max(index, 0)).find((c) => c.ɵfocusTarget() !== null) ??
          chips
            .slice(0, Math.max(index, 0))
            .reverse()
            .find((c) => c.ɵfocusTarget() !== null);
        if (target === undefined) {
          this.exit.emit('end');
        } else {
          this.focusControl(target.ɵfocusTarget()!);
        }
      },
      { injector: this.injector },
    );
  }

  /** The controls of the set's single tab stop, in order. */
  private controls(): HTMLElement[] {
    const chips = this.chips();
    const visible = chips.slice(0, chips.length - this.hiddenCount());
    const controls = visible.flatMap((chip) => chip.ɵcontrols());
    const more = this.moreItem()?.nativeElement.querySelector<HTMLElement>('button');
    if (more !== undefined && more !== null && this.hiddenCount() > 0) {
      controls.push(more);
    }
    return controls;
  }

  /** Makes the current control the one tab stop, falling back to the first. */
  private updateTabStop(): void {
    const controls = this.controls();
    const stop = this.current !== null && controls.includes(this.current) ? this.current : controls[0];
    for (const control of controls) {
      control.tabIndex = control === stop ? 0 : -1;
    }
    // The "+N" chip out of use is out of the tab order too.
    const more = this.moreItem()?.nativeElement.querySelector<HTMLElement>('button');
    if (more !== undefined && more !== null && !controls.includes(more)) {
      more.tabIndex = -1;
    }
  }

  /** The arrow keys, Home and End move between the controls. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) {
      return;
    }
    const controls = this.controls();
    const index = controls.indexOf(event.target as HTMLElement);
    if (index === -1) {
      return;
    }
    let next: number;
    switch (event.key) {
      case 'ArrowLeft':
      case 'ArrowRight': {
        // Mapped: Right moves forward in LTR and backward in RTL, read live.
        const rtl = getComputedStyle(this.host).direction === 'rtl';
        next = index + ((event.key === 'ArrowRight') !== rtl ? 1 : -1);
        break;
      }
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = controls.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    if (next < 0) {
      this.exit.emit('start');
    } else if (next >= controls.length) {
      this.exit.emit('end');
    } else {
      this.focusControl(controls[next]);
    }
  }

  /**
   * Focus inside the set: the focused control takes the tab stop, and in
   * scroll mode its chip scrolls into view along the line.
   */
  protected onFocusin(event: FocusEvent): void {
    const target = event.target as HTMLElement;
    const controls = this.controls();
    if (!controls.includes(target)) {
      return;
    }
    this.current = target;
    this.focusedChip = this.chips().find((chip) => chip.ɵelement.contains(target)) ?? null;
    for (const control of controls) {
      control.tabIndex = control === target ? 0 : -1;
    }
    if (this.overflow() === 'scroll') {
      this.scrollIntoLine(target.closest<HTMLElement>('[role="listitem"]') ?? target);
    }
  }

  /**
   * Focus leaving the set. A chip taken out of the page may report this as
   * it goes, so the check waits a microtask, until after the chip's
   * destruction has been reported; only a move away from a control still
   * in the page forgets the focused chip.
   */
  protected onFocusout(event: FocusEvent): void {
    const to = event.relatedTarget;
    if (to instanceof Node && this.host.contains(to)) {
      return;
    }
    const from = event.target as HTMLElement;
    queueMicrotask(() => {
      if (from.isConnected && this.focusedChip?.ɵelement.contains(from) === true) {
        this.focusedChip = null;
      }
    });
  }

  /** Focuses a control without the browser scrolling the line its own way. */
  private focusControl(control: HTMLElement): void {
    control.focus({ preventScroll: this.overflow() === 'scroll' });
  }

  /**
   * Scrolls the line by the least amount that shows `item` whole (the
   * `nearest` alignment), without scrolling anything around the set.
   */
  private scrollIntoLine(item: HTMLElement): void {
    const style = getComputedStyle(this.host);
    const box = this.host.getBoundingClientRect();
    const start = box.left + this.host.clientLeft + parseFloat(style.paddingLeft);
    const end = box.left + this.host.clientLeft + this.host.clientWidth - parseFloat(style.paddingRight);
    const rect = item.getBoundingClientRect();
    if (rect.left < start) {
      this.host.scrollBy({ left: rect.left - start, behavior: 'instant' });
    } else if (rect.right > end) {
      this.host.scrollBy({ left: Math.min(rect.right - end, rect.left - start), behavior: 'instant' });
    }
  }

  /**
   * Collapse mode's fit: every chip is measured at its natural width (up
   * to the set's), and as many leading chips show as fit one line beside
   * the "+N" chip. At least one chip always shows; it ellipsizes when even
   * it does not fit. The result depends on natural widths only, so showing
   * or hiding chips never feeds back into it.
   */
  private measure(): void {
    const chips = this.chips();
    if (this.overflow() !== 'collapse' || chips.length === 0) {
      this.applyVisibleCount(Number.POSITIVE_INFINITY);
      return;
    }
    const style = getComputedStyle(this.host);
    const gap = parseFloat(style.columnGap) || 0;
    const available =
      this.host.clientWidth - parseFloat(style.paddingInlineStart) - parseFloat(style.paddingInlineEnd);
    const elements = chips.map((chip) => chip.ɵelement);
    for (const element of elements) {
      element.classList.add('tm-chip--measuring');
    }
    const widths = elements.map((element) => element.getBoundingClientRect().width);
    for (const element of elements) {
      element.classList.remove('tm-chip--measuring');
    }
    const all = widths.reduce((sum, width) => sum + width, 0) + gap * (widths.length - 1);
    if (all <= available + FIT_TOLERANCE) {
      this.applyVisibleCount(chips.length);
      return;
    }
    let used = this.moreItem()?.nativeElement.getBoundingClientRect().width ?? 0;
    let count = 0;
    while (count < widths.length && used + widths[count] + gap <= available + FIT_TOLERANCE) {
      used += widths[count] + gap;
      count++;
    }
    this.applyVisibleCount(Math.max(count, 1));
  }

  /**
   * Shows the first `count` chips and hides the rest. The classes are set
   * here rather than bound, and (from a resize) the set's own view is
   * refreshed at once, so a resize never paints a frame with the old fit.
   */
  private applyVisibleCount(count: number, refresh = true): void {
    const chips = this.chips();
    chips.forEach((chip, index) =>
      chip.ɵelement.classList.toggle('tm-chip--collapsed', index >= count),
    );
    if (count === this.visibleCount()) {
      return;
    }
    this.visibleCount.set(count);
    if (refresh) {
      this.changeDetector.detectChanges();
    }
  }
}
