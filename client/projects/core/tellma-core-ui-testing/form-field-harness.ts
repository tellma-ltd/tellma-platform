import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for `tm-form-field` — label/required-marker/hint/error scaffold
 * assertions (spec §10).
 */
export class TmFormFieldHarness extends ComponentHarness {
  static hostSelector = 'tm-form-field';

  private readonly labelEl = this.locatorForOptional('.tm-form-field__label');
  private readonly hintEl = this.locatorForOptional('.tm-form-field__hint');
  private readonly errorEl = this.locatorFor('.tm-form-field__error');
  private readonly requiredEl = this.locatorForOptional('.tm-form-field__required');

  async getLabelText(): Promise<string | null> {
    const label = await this.labelEl();
    return label === null ? null : (await label.text()).trim();
  }

  async hasRequiredMarker(): Promise<boolean> {
    return (await this.requiredEl()) !== null;
  }

  async getHintText(): Promise<string | null> {
    const hint = await this.hintEl();
    if (hint === null || (await hint.getProperty<boolean>('hidden'))) {
      return null;
    }
    return (await hint.text()).trim();
  }

  /** The currently DISPLAYED error text, or null when none is shown. */
  async getErrorText(): Promise<string | null> {
    const text = (await (await this.errorEl()).text()).trim();
    return text === '' ? null : text;
  }

  async labelClick(): Promise<void> {
    const label = await this.labelEl();
    if (label === null) {
      throw new Error('tm-form-field has no label to click');
    }
    await label.click();
  }
}
