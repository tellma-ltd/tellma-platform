// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for a `button[tmFilePicker]` trigger. */
export class TmFilePickerHarness extends ComponentHarness {
  /** The selector for a picker button. */
  static hostSelector = 'button[tmFilePicker]';

  /** The button's visible label, trimmed. */
  async getLabel(): Promise<string> {
    return (await (await this.host()).text()).trim();
  }

  /** Clicks the button (opens the OS file dialog in a real browser). */
  async click(): Promise<void> {
    return (await this.host()).click();
  }
}

/** Harness for a `tm-dropzone` region. */
export class TmDropzoneHarness extends ComponentHarness {
  /** The selector for the dropzone host. */
  static hostSelector = 'tm-dropzone';

  /** The hint line, including the browse affordance text. */
  async getHintText(): Promise<string> {
    return (await (await this.locatorFor('.tm-dropzone__hint')()).text()).trim();
  }

  /** The meta lines (accepted types, size limit), in display order. */
  async getMetaLines(): Promise<string[]> {
    const lines = await this.locatorForAll('.tm-dropzone__meta')();
    return Promise.all(lines.map(async (line) => (await line.text()).trim()));
  }

  /** Focuses the region (the keyboard + paste surface). */
  async focus(): Promise<void> {
    return (await this.host()).focus();
  }
}
