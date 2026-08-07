// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  computed,
  contentChild,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  input,
  InjectionToken,
  signal,
  viewChild,
} from '@angular/core';
import {
  CdkConnectedOverlay,
  OverlayModule,
  type ConnectedOverlayPositionChange,
} from '@angular/cdk/overlay';

import type { TmFormFieldControl } from '@tellma/core-ui/contracts';
import { TM_ERROR_DISPLAY, TM_FORM_FIELD_DEFAULTS, type TmControlSize } from '@tellma/core-ui';
import { tmCreateAnchoredOverlay, tmLogicalPositions, ɵTmErrorPopover } from '@tellma/core-ui/private';
import { TmSpinner } from '@tellma/core-ui/spinner';

/**
 * A control projects itself into `tm-form-field` by providing this token
 * (`providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: … }]`) — the
 * MatFormFieldControl seam adapted to Signal Forms.
 */
export const TM_FORM_FIELD_CONTROL = new InjectionToken<TmFormFieldControl>(
  'TM_FORM_FIELD_CONTROL',
);

let nextUniqueId = 0;

/**
 * The shared label / required-marker / hint / error scaffold every form
 * control projects into (brand FormField, `--field-*` tokens).
 *
 * Labelling is two-path: a native-input control gets `<label for>`; a
 * control with a non-labelable host implements `setLabelId` and the field
 * hands it the label id for `aria-labelledby`, forwarding label clicks to
 * the control. For a chrome-less control (`ownsChrome`) the field renders
 * only the label/hint/message scaffold, never a second box.
 *
 * Validation messages are a popover anchored to the control, shown while
 * it holds focus — never a row beneath it. A row would make every field
 * either reserve empty space forever or reflow the page the moment it goes
 * invalid, and a long form does both dozens of times as it is filled in.
 * Out of focus the field keeps its invalid border and an in-field glyph,
 * and the popover returns on refocus. The hint stays put throughout: it is
 * the field's standing instruction, and hiding it exactly when the user
 * got the field wrong takes away the sentence that explains it.
 *
 * ACCESSIBILITY: the popover is decoration. The messages live in a
 * permanently rendered `aria-live="polite"` region that is visually hidden
 * but always in the accessibility tree and always in the control's
 * `aria-describedby` — so what assistive technology gets does not depend on
 * where focus is, and the visible bubble is `aria-hidden` so nothing is
 * announced twice.
 *
 * @tmGroup form-control
 * @tmA11yNotes The error element is a persistent polite live region, always
 *   present and merged into the control's aria-describedby along with the
 *   hint; the visible popover mirrors it and is aria-hidden.
 */
@Component({
  selector: 'tm-form-field',
  imports: [TmSpinner, OverlayModule, ɵTmErrorPopover],
  template: `
    @if (label() !== '') {
      <label
        class="tm-form-field__label"
        [id]="labelId"
        [attr.for]="labelFor()"
        (click)="onLabelClick()"
      >
        {{ label() }}
        @if (showRequiredMarker()) {
          <span class="tm-form-field__required" aria-hidden="true">{{ requiredMarker }}</span>
        }
      </label>
    }

    <div
      #box
      class="tm-form-field__box"
      [class.tm-form-field__box--chromeless]="chromeless()"
      (click)="onContainerClick($event)"
    >
      <ng-content select="[tmPrefix]" />
      <ng-content />
      <ng-content select="[tmSuffix]" />
      @if (pending() && !chromeless()) {
        <tm-spinner class="tm-form-field__spinner" />
      }
      <!-- The in-field mark of invalidity: the one part of the error state
           that survives blur, so a field that has scrolled out of focus
           still says something is wrong. A control with a trailing button
           draws this glyph itself (ownsErrorIcon), because appended here
           it would sit AFTER that button and shove it sideways whenever an
           error came and went. -->
      @if (showError() && !chromeless() && !ownsErrorIcon()) {
        <svg
          class="tm-form-field__error-icon"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.75"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="10" />
          <line x1="12" x2="12" y1="8" y2="12" />
          <line x1="12" x2="12.01" y1="16" y2="16" />
        </svg>
      }
    </div>
    <!-- Persistent polite live region: exists whether or not it holds text, so
         empty->message (or message->message) is announced once. Visually
         hidden — the popover below is what the sighted user reads — but never
         removed, so the description is stable whatever focus is doing. -->
    <div class="tm-form-field__error" [id]="errorId" aria-live="polite" aria-atomic="true">
      @for (message of errorTexts(); track $index) {
        <span class="tm-form-field__error-line">{{ message }}</span>
      }
    </div>
    <div class="tm-form-field__hint" [id]="hintId" [hidden]="!showHint()">{{ hint() }}</div>

    <!-- Pure decoration, aria-hidden: the live region above already carries
         these words. If the control has its own dropdown open it will cover
         this, which is the right order — the user is working in the list. -->
    <ng-template
      [cdkConnectedOverlay]="anchoredError.overlayConfig()"
      [cdkConnectedOverlayOpen]="showErrorPopover()"
      (attach)="anchoredError.handleAttach()"
      (detach)="anchoredError.handleDetach()"
      (positionChange)="onErrorPositionChange($event)"
    >
      <tm-error-popover
        aria-hidden="true"
        [messages]="errorTexts()"
        [above]="errorPopoverAbove()"
      />
    </ng-template>
  `,
  styleUrl: './tm-form-field.css',
  host: {
    class: 'tm-form-field',
    '[class.tm-form-field--invalid]': 'showError()',
    '[class.tm-form-field--disabled]': 'control()?.disabled() ?? false',
    '[class.tm-form-field--readonly]': 'control()?.readonly() ?? false',
    '[class.tm-form-field--sm]': 'size() === "sm"',
    '[class.tm-form-field--lg]': 'size() === "lg"',
    '(focusin)': 'onFocusIn()',
    '(focusout)': 'onFocusOut($event)',
  },
})
export class TmFormField {
  private readonly defaults = inject(TM_FORM_FIELD_DEFAULTS);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly hostElement = inject(ElementRef).nativeElement as HTMLElement;
  private readonly document = inject(DOCUMENT);
  private readonly uniqueId = nextUniqueId++;

  /** The visible label text; omit for a label-less (adorned-only) field. */
  readonly label = input('');
  /** Supporting text under the control. Stays visible while an error shows. */
  readonly hint = input('');
  /**
   * Plain error text for non-form usage: shown whenever the control itself
   * displays no error (unbound controls always qualify). A [formField]-bound
   * control's own errors take precedence while displayed.
   */
  readonly error = input('');
  /** Size step, mapping to the --field-height/-font-size/-padding-x tokens. */
  readonly size = input<TmControlSize>(this.defaults.size);

  /** The projected control, discovered through the TM_FORM_FIELD_CONTROL token. */
  protected readonly control = contentChild(TM_FORM_FIELD_CONTROL);

  /** Stable id of the label element, handed to non-labelable controls for aria-labelledby. */
  protected readonly labelId = `tm-ff-label-${this.uniqueId}`;
  /** Stable id of the hint element, merged into the control's aria-describedby. */
  protected readonly hintId = `tm-ff-hint-${this.uniqueId}`;
  /** Stable id of the error live region, merged into the control's aria-describedby. */
  protected readonly errorId = `tm-ff-error-${this.uniqueId}`;

  /** `<label for>` only associates with labelable elements. */
  protected readonly labelFor = computed(() => {
    const control = this.control();
    return control && !control.setLabelId ? control.controlId() : null;
  });

  /** The configured visual marker rendered next to a required field's label. */
  protected readonly requiredMarker = this.defaults.requiredMarker;
  /** Whether the required marker is rendered — mirrors the control's required state. */
  protected readonly showRequiredMarker = computed(() => this.control()?.required() ?? false);

  /** Whether the field is displaying any error. */
  protected readonly showError = computed(() => this.errorTexts().length > 0);

  /**
   * EVERY displayed error, in the control's own order, while the display
   * policy shows them; else the plain `error` input — which is what a
   * control WITHOUT a bound field (every projected control provides the
   * token, bound or not) falls through to.
   *
   * All of them, not the first: the popover has room, and a field that
   * reports one problem, gets corrected, then reports the next reads as the
   * form moving the goalposts.
   */
  protected readonly errorTexts = computed<readonly string[]>(() => {
    const control = this.control();
    if (
      control &&
      this.errorDisplay({
        invalid: control.invalid(),
        touched: control.touched(),
        dirty: control.dirty(),
        pending: control.pending(),
      }) &&
      control.localizedErrors().length > 0
    ) {
      return control.localizedErrors().map((error) => error.message);
    }
    return this.error() === '' ? [] : [this.error()];
  });

  /**
   * Whether the hint is shown. An error no longer hides it: the hint is the
   * standing instruction for the field, and it is most useful precisely
   * when the value is wrong. Nothing reflows either way now that the error
   * is a popover.
   */
  protected readonly showHint = computed(() => this.hint() !== '');

  /** Whether focus is anywhere inside the field. */
  private readonly focused = signal(false);

  /**
   * The reader is holding the bubble open by interacting with it — pressing
   * into it to select the message. The bubble is not focusable (it is
   * decoration), so that press blurs the control, and without this the
   * bubble would vanish out from under the drag.
   */
  private readonly bubbleHeld = signal(false);

  /**
   * The visible bubble follows focus — or the reader's grip on it, while
   * they are selecting the text out of it.
   */
  protected readonly showErrorPopover = computed(
    () => this.showError() && (this.focused() || this.bubbleHeld()),
  );

  /** Set when the overlay flips above the control, to turn the arrow over. */
  protected readonly errorPopoverAbove = signal(false);

  /** Whether the control reports async validation in progress — drives the spinner. */
  protected readonly pending = computed(() => this.control()?.pending() ?? false);

  /** ownsChrome controls render their own box — the field's collapses. */
  protected readonly chromeless = computed(() => this.control()?.ownsChrome ?? true);

  /** Controls with a trailing affordance draw the invalid glyph themselves. */
  protected readonly ownsErrorIcon = computed(() => this.control()?.ownsErrorIcon ?? false);

  private readonly boxRef = viewChild.required<ElementRef<HTMLElement>>('box');
  private readonly errorOverlay = viewChild(CdkConnectedOverlay);
  private readonly bubbleRef = viewChild(ɵTmErrorPopover, { read: ElementRef<HTMLElement> });

  /**
   * The bubble points at the control, so it anchors to the field's own box
   * — except for a chrome-owning control, whose box is `display: contents`
   * and therefore has no rectangle to measure. There the whole field is the
   * anchor, which puts the bubble under the control and its hint together.
   */
  protected readonly anchoredError = tmCreateAnchoredOverlay({
    overlay: () => this.errorOverlay(),
    origin: () => (this.chromeless() ? this.hostElement : this.boxRef().nativeElement),
    positions: tmLogicalPositions('block-end', 'start'),
    // The bubble is a message, not a menu: near the window edge it should
    // slide back into view rather than be clipped mid-sentence.
    keepOnScreen: true,
    viewportMargin: 8,
    // The messages render one pass after the CDK attaches, so a flip-up
    // would otherwise measure an empty bubble.
    remeasure: 'afterNextRender',
  });

  constructor() {
    // Feed the hint/error ids into the control's aria-describedby (merge,
    // not clobber — the control appends them to its own ids) and hand the
    // label id to non-labelable hosts. Both ids go in whenever their element
    // holds text: the error is described even while its popover is closed,
    // because assistive technology must not depend on where focus happens
    // to be, and the hint no longer drops out when an error appears.
    effect(() => {
      const control = this.control();
      if (!control) {
        return;
      }
      const ids: string[] = [];
      if (this.showHint()) {
        ids.push(this.hintId);
      }
      if (this.showError()) {
        ids.push(this.errorId);
      }
      control.setDescribedByIds(ids);
      control.setLabelId?.(this.label() !== '' ? this.labelId : null);
    });

    // Where the next press lands decides whether the bubble stays. Pressing
    // INTO it is the reader reaching for the message text, which has to
    // survive the blur that press causes; anywhere else means they have
    // moved on. Judged from the press TARGET rather than from a latch the
    // bubble sets on itself: the listener is only attached while the bubble
    // is up, and a latch set before it attaches would swallow the press
    // that was meant to dismiss it.
    //
    // Attached only while the bubble is up, so a form of forty fields does
    // not carry forty standing document listeners.
    effect((onCleanup) => {
      if (!this.showErrorPopover()) {
        return;
      }
      const bubble = this.bubbleRef()?.nativeElement;
      const onPointerDown = (event: Event) => {
        const target = event.target;
        if (!(target instanceof Node)) {
          return;
        }
        if (bubble?.contains(target) === true) {
          this.bubbleHeld.set(true);
          return;
        }
        if (this.hostElement.contains(target)) {
          this.bubbleHeld.set(false);
          return;
        }
        this.bubbleHeld.set(false);
        // The blur a grip suppressed never took effect — focus really did
        // leave when the reader first pressed into the bubble. Without this
        // the field would still believe it is focused.
        if (!this.hostElement.contains(this.document.activeElement)) {
          this.focused.set(false);
        }
      };
      this.document.addEventListener('pointerdown', onPointerDown);
      onCleanup(() => this.document.removeEventListener('pointerdown', onPointerDown));
    });
  }

  /** Forwards label clicks to controls that `<label for>` cannot reach. */
  protected onLabelClick(): void {
    // <label for> handles native inputs; forward for non-labelable hosts so
    // click-to-focus still works (§3.1).
    this.control()?.onContainerClick?.();
  }

  /** Lets the control grab focus on clicks that land on the container chrome itself. */
  protected onContainerClick(event: MouseEvent): void {
    // Clicks on the container chrome (padding/border), not on the control
    // itself — let the control grab focus (§2.1).
    const target = event.target;
    if (!(target instanceof Element)) {
      return;
    }
    // A press on a real control is that control's own business; anything
    // else in the box is chrome. This is what makes the invalid glyph work:
    // it is pointer-transparent, so a press on the mark of the problem
    // surfaces on the box (or on a chrome-owning control's host) and lands
    // here — and reaching for that mark and having nothing happen is the
    // one thing a reader will certainly try.
    if (target.closest('input, textarea, select, button, a, [tabindex]') !== null) {
      return;
    }
    this.control()?.onContainerClick?.();
  }

  /** Focus entered the field — the error popover may show. */
  protected onFocusIn(): void {
    this.focused.set(true);
    this.bubbleHeld.set(false);
  }

  /**
   * Focus left an element inside the field. Focus moving BETWEEN the
   * control and, say, its own calendar toggle fires focusout too, so the
   * popover would flicker if every focusout closed it; the incoming element
   * decides. A press on the bubble itself also blurs the control — that one
   * keeps the bubble up, so the press can turn into a text selection.
   */
  protected onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (next instanceof Node && this.hostElement.contains(next)) {
      return;
    }
    if (this.bubbleHeld()) {
      return;
    }
    this.focused.set(false);
  }

  /**
   * The overlay reports where it actually landed. A bubble that flipped
   * above the control must turn its arrow over, or it points away from the
   * thing it is describing.
   */
  protected onErrorPositionChange(change: ConnectedOverlayPositionChange): void {
    this.errorPopoverAbove.set(change.connectionPair.overlayY === 'bottom');
  }
}
