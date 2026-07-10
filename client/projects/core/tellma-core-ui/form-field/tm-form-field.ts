import {
  Component,
  computed,
  contentChild,
  effect,
  inject,
  input,
  InjectionToken,
} from '@angular/core';

import type { TmFormFieldControl } from '@tellma/core-ui/contracts';
import { TM_ERROR_DISPLAY, TM_FORM_FIELD_DEFAULTS } from '@tellma/core-ui';

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
 * the control. Hint and error are SEPARATE persistent elements — the error
 * element is a permanent `aria-live="polite"` region so empty→message is
 * announced cleanly; the display policy toggles visibility, never swaps a
 * shared node. For a chrome-less control (`ownsChrome`) the field renders
 * only the label/hint/error scaffold, never a second box.
 *
 * @tmGroup form-control
 * @tmA11yNotes The error element is a persistent polite live region; hint
 *   and error ids are merged into the control's aria-describedby.
 */
@Component({
  selector: 'tm-form-field',
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
      class="tm-form-field__box"
      [class.tm-form-field__box--chromeless]="chromeless()"
      (click)="onContainerClick($event)"
    >
      <ng-content select="[tmPrefix]" />
      <ng-content />
      <ng-content select="[tmSuffix]" />
      @if (pending() && !chromeless()) {
        <svg class="tm-form-field__spinner" viewBox="0 0 16 16" fill="none" aria-hidden="true">
          <circle cx="8" cy="8" r="6.5" stroke="currentColor" stroke-opacity="0.25" />
          <path d="M8 1.5 A 6.5 6.5 0 0 1 14.5 8" stroke="currentColor" stroke-linecap="round" />
        </svg>
      }
    </div>
    <!-- Persistent polite live region: exists whether or not it holds text, so
         empty->message (or message->message) is announced once. -->
    <div class="tm-form-field__error" [id]="errorId" aria-live="polite" aria-atomic="true">
      @if (showError()) {
        {{ errorText() }}
      }
    </div>
    <div class="tm-form-field__hint" [id]="hintId" [hidden]="!showHint()">{{ hint() }}</div>
  `,
  styleUrl: './tm-form-field.css',
  host: {
    class: 'tm-form-field',
    '[class.tm-form-field--invalid]': 'showError()',
    '[class.tm-form-field--disabled]': 'control()?.disabled() ?? false',
    '[class.tm-form-field--readonly]': 'control()?.readonly() ?? false',
    '[class.tm-form-field--sm]': 'size() === "sm"',
    '[class.tm-form-field--lg]': 'size() === "lg"',
  },
})
export class TmFormField {
  private readonly defaults = inject(TM_FORM_FIELD_DEFAULTS);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly uniqueId = nextUniqueId++;

  /** The visible label text; omit for a label-less (adorned-only) field. */
  readonly label = input('');
  /** Supporting text shown while no error is displayed. */
  readonly hint = input('');
  /**
   * Plain error text for NON-form usage only; a [formField]-bound control's
   * errors come from the field state and take precedence.
   */
  readonly error = input('');
  /** Height/density variant mapping to the --field-height* tokens. */
  readonly size = input<'sm' | 'md' | 'lg'>(this.defaults.size);

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

  /** Whether the error element shows text — the display policy plus available errors. */
  protected readonly showError = computed(() => {
    const control = this.control();
    if (!control) {
      return this.error() !== '';
    }
    return (
      this.errorDisplay({
        invalid: control.invalid(),
        touched: control.touched(),
        dirty: control.dirty(),
        pending: control.pending(),
      }) && control.localizedErrors().length > 0
    );
  });

  /** The displayed error text: the control's first localized error, else the `error` input. */
  protected readonly errorText = computed(() => {
    const control = this.control();
    if (!control) {
      return this.error();
    }
    return control.localizedErrors()[0]?.message ?? '';
  });

  /** Whether the hint is shown — a displayed error hides it. */
  protected readonly showHint = computed(() => this.hint() !== '' && !this.showError());

  /** Whether the control reports async validation in progress — drives the spinner. */
  protected readonly pending = computed(() => this.control()?.pending() ?? false);

  /** ownsChrome controls render their own box — the field's collapses. */
  protected readonly chromeless = computed(() => this.control()?.ownsChrome ?? true);

  constructor() {
    // Feed the hint/error ids into the control's aria-describedby (merge,
    // not clobber — the control appends them to its own ids) and hand the
    // label id to non-labelable hosts.
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
    if (event.target === event.currentTarget) {
      this.control()?.onContainerClick?.();
    }
  }
}
