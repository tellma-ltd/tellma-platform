import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-checkbox`. */
export class TmCheckboxHarness extends ComponentHarness {
  /** The selector for the `tm-checkbox` host element. */
  static hostSelector = 'tm-checkbox';

  private readonly native = this.locatorFor('input[type="checkbox"]');
  private readonly labelEl = this.locatorFor('.tm-checkbox__label');

  /** Whether the checkbox is checked. */
  async isChecked(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('checked');
  }

  /** Whether the checkbox is in the indeterminate (mixed) state. */
  async isIndeterminate(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('indeterminate');
  }

  /** Whether the checkbox is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('disabled');
  }

  /** Whether the checkbox is required. */
  async isRequired(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('required');
  }

  /** Gets the projected label text, trimmed. */
  async getLabelText(): Promise<string> {
    return (await (await this.labelEl()).text()).trim();
  }

  /** Toggles by clicking the native input (as the user would). */
  async toggle(): Promise<void> {
    return (await this.native()).click();
  }

  /** Focuses the native checkbox input. */
  async focus(): Promise<void> {
    return (await this.native()).focus();
  }

  /** Blurs the native checkbox input. */
  async blur(): Promise<void> {
    return (await this.native()).blur();
  }
}
