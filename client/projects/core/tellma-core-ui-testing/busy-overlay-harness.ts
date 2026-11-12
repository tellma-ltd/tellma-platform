// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for the overlay `TmBusyOverlay` draws.
 *
 * The overlay's host sits on `<body>`, outside any fixture — locate it with
 * a document-root loader
 * (`TestbedHarnessEnvironment.documentRootLoader(fixture)`). It exists from
 * the moment the service is first injected.
 */
export class TmBusyOverlayHarness extends ComponentHarness {
  /** The selector for the busy overlay's host. */
  static hostSelector = '.tm-busy-overlay';

  /** Whether a run is in progress: the page is blocked. */
  async isActive(): Promise<boolean> {
    return (await this.host()).hasClass('tm-busy-overlay--active');
  }

  /** Whether the scrim, spinner and label show. */
  async isScrimShown(): Promise<boolean> {
    return (await this.host()).hasClass('tm-busy-overlay--scrim');
  }

  /** Whether the spinner is drawn. */
  async hasSpinner(): Promise<boolean> {
    return (await this.locatorForOptional('tm-spinner')()) !== null;
  }

  /** The status region's text: the label while the scrim shows, else `''`. */
  async getLabel(): Promise<string> {
    return (await (await this.locatorFor('[role="status"]')()).text()).trim();
  }
}
