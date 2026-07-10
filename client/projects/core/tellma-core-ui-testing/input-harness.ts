import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for the `tmInput` directive — the typed, implementation-independent
 * automation surface for tests and agents.
 */
export class TmInputHarness extends ComponentHarness {
  /** The selector for the `input[tmInput]` host element. */
  static hostSelector = 'input[tmInput]';

  /** Gets the current value of the input. */
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

  /** Blurs the input. */
  async blur(): Promise<void> {
    return (await this.host()).blur();
  }

  /** Focuses the input. */
  async focus(): Promise<void> {
    return (await this.host()).focus();
  }

  /** Whether the input is focused. */
  async isFocused(): Promise<boolean> {
    return (await this.host()).isFocused();
  }

  /** Whether the input is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await this.host()).getProperty<boolean>('disabled');
  }

  /** Whether the input is readonly. */
  async isReadonly(): Promise<boolean> {
    return (await this.host()).getProperty<boolean>('readOnly');
  }

  /** Whether the input is required. */
  async isRequired(): Promise<boolean> {
    return (await this.host()).getProperty<boolean>('required');
  }

  /** Whether the input currently announces itself as invalid (aria-invalid). */
  async isInvalid(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-invalid')) === 'true';
  }

  /** Whether the input announces pending async validation (aria-busy). */
  async isBusy(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-busy')) === 'true';
  }

  /** Gets the input's placeholder text. */
  async getPlaceholder(): Promise<string> {
    return (await this.host()).getProperty<string>('placeholder');
  }

  /** Gets the input's aria-describedby attribute, or null when absent. */
  async getDescribedBy(): Promise<string | null> {
    return (await this.host()).getAttribute('aria-describedby');
  }
}
