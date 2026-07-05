import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for the `tmInput` directive — the typed, implementation-independent
 * automation surface for tests and agents (spec §10).
 */
export class TmInputHarness extends ComponentHarness {
  static hostSelector = 'input[tmInput]';

  async getValue(): Promise<string> {
    return (await this.host()).getProperty<string>('value');
  }

  /** Types like a user: focus, clear, send keys, leaving the input focused. */
  async setValue(value: string): Promise<void> {
    const host = await this.host();
    await host.clear();
    if (value !== '') {
      await host.sendKeys(value);
    }
  }

  async blur(): Promise<void> {
    return (await this.host()).blur();
  }

  async focus(): Promise<void> {
    return (await this.host()).focus();
  }

  async isFocused(): Promise<boolean> {
    return (await this.host()).isFocused();
  }

  async isDisabled(): Promise<boolean> {
    return (await this.host()).getProperty<boolean>('disabled');
  }

  async isReadonly(): Promise<boolean> {
    return (await this.host()).getProperty<boolean>('readOnly');
  }

  async isRequired(): Promise<boolean> {
    return (await this.host()).getProperty<boolean>('required');
  }

  async isInvalid(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-invalid')) === 'true';
  }

  async isBusy(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-busy')) === 'true';
  }

  async getPlaceholder(): Promise<string> {
    return (await this.host()).getProperty<string>('placeholder');
  }

  async getDescribedBy(): Promise<string | null> {
    return (await this.host()).getAttribute('aria-describedby');
  }
}
