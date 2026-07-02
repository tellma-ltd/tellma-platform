import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-checkbox` (spec §10). */
export class TmCheckboxHarness extends ComponentHarness {
  static hostSelector = 'tm-checkbox';

  private readonly native = this.locatorFor('input[type="checkbox"]');
  private readonly labelEl = this.locatorFor('.tm-checkbox__label');

  async isChecked(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('checked');
  }

  async isIndeterminate(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('indeterminate');
  }

  async isDisabled(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('disabled');
  }

  async isRequired(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('required');
  }

  async getLabelText(): Promise<string> {
    return (await (await this.labelEl()).text()).trim();
  }

  /** Toggles by clicking the native input (as the user would). */
  async toggle(): Promise<void> {
    return (await this.native()).click();
  }

  async focus(): Promise<void> {
    return (await this.native()).focus();
  }

  async blur(): Promise<void> {
    return (await this.native()).blur();
  }
}
