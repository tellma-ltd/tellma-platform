import {
  booleanAttribute,
  Component,
  computed,
  input,
  signal,
  TemplateRef,
  viewChild,
} from '@angular/core';

/**
 * One selectable option of a `tm-select`. Displays one property,
 * captures another: `value` is what lands in the model; the projected
 * content is what the user sees.
 *
 * tm-option holds DATA + the content template; the real `[ngOption]` row is
 * rendered by `tm-select` inside its listbox (aria's Option directive must
 * be a listbox descendant in the SAME view — DI does not cross content
 * projection). Typeahead reads ONLY the `label` input (aria has no
 * textContent fallback), so when `label` is omitted tm-select derives it
 * from the rendered row text.
 *
 * @tmGroup form-control
 */
@Component({
  selector: 'tm-option',
  template: `<ng-template #content><ng-content /></ng-template>`,
})
export class TmOption<T> {
  /** The domain value captured into the model when this option is chosen. */
  readonly value = input.required<T>();
  /**
   * Explicit typeahead/search label; derived from the rendered row
   * text when omitted. Providing it also lets a CLOSED trigger resolve its
   * label before the panel ever rendered.
   */
  readonly label = input<string | undefined>(undefined);
  /** Disables the option: it stays visible but cannot be activated. */
  readonly disabled = input(false, { transform: booleanAttribute });

  /** The projected display content, stamped into the panel by tm-select. */
  readonly contentTemplate = viewChild.required<TemplateRef<unknown>>('content');

  /** Written by tm-select after the row renders (textContent fallback). */
  readonly derivedText = signal('');

  /** The label in effect for typeahead and trigger resolution: `label`, else the derived text. */
  readonly effectiveLabel = computed(() => this.label() ?? this.derivedText());
}
