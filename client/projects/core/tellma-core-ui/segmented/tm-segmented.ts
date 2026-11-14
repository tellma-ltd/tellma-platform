// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  booleanAttribute,
  Component,
  computed,
  contentChildren,
  DestroyRef,
  ElementRef,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChildren,
  type Signal,
} from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';

import type { TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import {
  TM_ERROR_DISPLAY,
  TM_UI_TRANSLATE,
  ɵtmControlSize,
  tmResolveFieldErrors,
  type TmBreakpoint,
  type TmControlSize,
} from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';
import { TmIcon } from '@tellma/core-ui/icon';
import { ɵtmRadioArrowTarget } from '@tellma/core-ui/private';

import { ɵTmSegmentTooltip } from './internal/tm-segment-tooltip';
import { TmSegment } from './tm-segment';

let nextUniqueId = 0;

/**
 * Picks one value from a compact track of segments: the radio group's
 * native radios in a sunken track at the field height of the resolved
 * size, declared as `tm-segment` children. The checked segment is a raised
 * card. Keyboard and semantics are the radio group's: one tab stop,
 * arrows check and move (Left/Right follow the live direction), Space
 * checks.
 *
 * `labels` decides when segments with an icon drop their label and show
 * the icon alone, with the label as a tooltip and still as the accessible
 * name: `always` never drops it, `never` always does, and a breakpoint
 * keeps it while the nearest enclosing query container (an element with
 * `container-type: inline-size`, such as `tm-page-header`) is at least that
 * wide. Outside any query container the labels show. The decision moves
 * only on resize, never on input. Segments without an icon always show
 * their label.
 *
 * Signal Forms native: implements `FormValueControl<T | null>`; reports
 * touch when focus leaves the control.
 *
 * @tmGroup form-control
 * @tmA11yNotes APG radio group over native radios: one tab stop; arrows
 *   move and check, wrapping, Left/Right mapped in RTL; Space checks. An
 *   icon-only segment keeps its label as its accessible name.
 */
@Component({
  selector: 'tm-segmented',
  imports: [TmIcon, ɵTmSegmentTooltip],
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmSegmented }],
  template: `
    <div
      class="tm-segmented__track"
      role="radiogroup"
      [id]="controlId()"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-describedby]="describedByAttr()"
      [attr.aria-required]="required() ? 'true' : null"
      [attr.aria-readonly]="readonly() ? 'true' : null"
      [attr.aria-disabled]="disabled() ? 'true' : null"
      [attr.aria-invalid]="showsInvalid() ? 'true' : null"
      [attr.aria-busy]="pending() ? 'true' : null"
      (keydown)="onKeydown($event)"
      (focusout)="onFocusOut($event)"
    >
      @for (segment of segments(); track segment) {
        <!-- One segment: the native radio stretched over it, the icon, and
             the label (which an icon segment drops when narrow). -->
        <label
          class="tm-segmented__segment"
          [class.tm-segmented__segment--checked]="isChecked(segment)"
          [class.tm-segmented__segment--disabled]="disabled() || segment.disabled()"
        >
          <input
            #native
            class="tm-segmented__native"
            type="radio"
            [name]="name"
            [checked]="isChecked(segment)"
            [disabled]="disabled() || segment.disabled()"
            [tabIndex]="tabStop() === segment ? 0 : -1"
            [attr.aria-label]="segment.icon() ? segment.label() : null"
            [tmSegmentTooltip]="segment.label()"
            [tmSegmentTooltipActive]="!!segment.icon() && iconOnly()"
            (change)="onNativeChange(segment)"
          />
          @if (segment.icon(); as icon) {
            <tm-icon class="tm-segmented__icon" [name]="icon" />
          }
          <span
            #label
            class="tm-segmented__label"
            [class.tm-segmented__label--collapsible]="!!segment.icon()"
            [attr.aria-hidden]="segment.icon() ? 'true' : null"
            >{{ segment.label() }}</span
          >
        </label>
      }
    </div>
  `,
  styleUrl: './tm-segmented.css',
  host: {
    class: 'tm-segmented',
    '[attr.data-labels]': 'labels()',
    '[class.tm-segmented--disabled]': 'disabled()',
    '[class.tm-segmented--invalid]': 'showsInvalid()',
    '[class.tm-segmented--sm]': 'resolvedSize() === "sm"',
    '[class.tm-segmented--lg]': 'resolvedSize() === "lg"',
  },
})
export class TmSegmented<T> implements TmFormFieldControl {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly uniqueId = nextUniqueId++;

  // ---- FormValueControl<T | null> + the optional state inputs ----
  /** The checked segment's value; `null` while none is checked. */
  readonly value = model<T | null>(null);
  /** Non-form usage only — the bound field is authoritative when bound via [formField]. */
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Readonly state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Required state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly required = input(false, { transform: booleanAttribute });
  /** Validity state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly invalid = input(false, { transform: booleanAttribute });
  /** Touched state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly touched = input(false, { transform: booleanAttribute });
  /** Dirty state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly dirty = input(false, { transform: booleanAttribute });
  /** Async-validation-pending state — the bound field is authoritative when bound via [formField]. */
  readonly pending = input(false, { transform: booleanAttribute });
  /** The raw framework errors, bound by [formField] and localized into `localizedErrors`. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Emits when focus leaves the control — touch reporting for the bound field. */
  readonly touch = output<void>();

  // ---- Own API ----
  /** The control's accessible name (required: the segments alone do not say what is chosen). */
  readonly ariaLabel = input.required<string>();
  /**
   * When icon segments show their label: `always`, `never`, or while the
   * nearest query container is at least this breakpoint wide. Default
   * `always`.
   */
  readonly labels = input<'always' | 'never' | TmBreakpoint>('always');
  /** The size step; unset, the workspace default (the touch height on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);
  /**
   * Author-supplied describedby ids (space-separated). Preserved — the
   * enclosing field's hint/error ids are merged after them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  /** Stable generated id of the radiogroup element. */
  readonly controlId = signal(`tm-segmented-${this.uniqueId}`).asReadonly();
  /** The `name` the segments' native radios share. */
  protected readonly name = `tm-segmented-${this.uniqueId}`;
  /** The size the track renders at. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);

  /** The declared segments, in order. */
  protected readonly segments = contentChildren<TmSegment<T>>(TmSegment);
  private readonly natives = viewChildren<ElementRef<HTMLInputElement>>('native');
  private readonly labelElements = viewChildren<ElementRef<HTMLElement>>('label');

  /** Whether icon segments currently show their icon alone (their label is hidden by the stylesheet). */
  protected readonly iconOnly = signal(false);

  /** The segment that holds the control's one tab stop: the checked one, else the first enabled one. */
  protected readonly tabStop = computed(() => {
    const segments = this.segments();
    const enabled = (segment: TmSegment<T>) => !segment.disabled();
    return (
      segments.find((segment) => enabled(segment) && this.isChecked(segment)) ??
      segments.find(enabled) ??
      null
    );
  });

  // ---- TmFormFieldControl ----
  /** Renders its own track; an enclosing field adds only its label, hint and error. */
  readonly ownsChrome = true;
  private readonly fieldDescribedBy = signal<readonly string[]>([]);
  /** Every exposed describedby id: author-supplied first, then the field's hint/error ids. */
  readonly describedByIds: Signal<readonly string[]> = computed(() => [
    ...(this.ariaDescribedby()?.split(/\s+/).filter(Boolean) ?? []),
    ...this.fieldDescribedBy(),
  ]);
  /** Already-localized error messages resolved from `errors` — read by the enclosing field. */
  readonly localizedErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(
    this.errors,
    this.translate,
  );
  /** The merged aria-describedby attribute value, or null when no ids apply. */
  protected readonly describedByAttr = computed(() => this.describedByIds().join(' ') || null);
  /** Whether the enclosing field displays an error this control must mark. */
  private readonly fieldError = signal(false);
  /** Whether invalidity is surfaced — the display policy, or the enclosing field's displayed error. */
  protected readonly showsInvalid = computed(
    () =>
      this.fieldError() ||
      this.errorDisplay({
        invalid: this.invalid(),
        touched: this.touched(),
        dirty: this.dirty(),
        pending: this.pending(),
      }),
  );

  constructor() {
    // Whether the labels are showing is the stylesheet's decision (a
    // container query); the tooltips follow it. Hiding or showing a label
    // changes the control's own width, so a resize observer on the host
    // sees every flip — and runs only on resize, never on input.
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const observer = new ResizeObserver(() => this.readIconOnly());
      observer.observe(this.hostElement);
      destroyRef.onDestroy(() => observer.disconnect());
      this.readIconOnly();
    });
  }

  /** Receives the field's hint/error ids and exposes them via aria-describedby. */
  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  /** Receives whether the enclosing field displays an error (its plain `error` input included). */
  setFieldError(showsError: boolean): void {
    this.fieldError.set(showsError);
  }

  /** Focuses the control's tab stop when the user clicks the field's label or chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Focuses the segment holding the tab stop; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    const stop = untracked(this.tabStop);
    const index = stop === null ? -1 : untracked(this.segments).indexOf(stop);
    untracked(this.natives)[index]?.nativeElement.focus(options);
  }

  /** Whether a segment's value is the control's value. */
  protected isChecked(segment: TmSegment<T>): boolean {
    return Object.is(segment.value(), this.value());
  }

  /** A native check (pointer, Space): written to the model, or reverted while readonly. */
  protected onNativeChange(segment: TmSegment<T>): void {
    if (this.readonly()) {
      const segments = this.segments();
      this.natives().forEach((ref, index) => {
        ref.nativeElement.checked = this.isChecked(segments[index]);
      });
      return;
    }
    this.value.set(segment.value());
  }

  /** The arrow keys: move to the previous or next enabled segment, wrapping, and check it. */
  protected onKeydown(event: KeyboardEvent): void {
    const natives = this.natives().map((ref) => ref.nativeElement);
    const target = ɵtmRadioArrowTarget(event, natives, this.hostElement);
    if (target === null) {
      return;
    }
    event.preventDefault();
    target.focus();
    if (!this.readonly()) {
      this.value.set(this.segments()[natives.indexOf(target)].value());
    }
  }

  /** Reports touch when focus leaves the control (not when it moves between segments). */
  protected onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (!(next instanceof Node) || !this.hostElement.contains(next)) {
      this.touch.emit();
    }
  }

  /** Reads whether the stylesheet currently hides the icon segments' labels. */
  private readIconOnly(): void {
    const collapsible = this.labelElements()
      .map((ref) => ref.nativeElement)
      .find((element) => element.classList.contains('tm-segmented__label--collapsible'));
    this.iconOnly.set(
      collapsible !== undefined && getComputedStyle(collapsible).display === 'none',
    );
  }
}
