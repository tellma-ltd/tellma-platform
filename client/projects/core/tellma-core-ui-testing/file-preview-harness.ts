// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for an open `TmFilePreview` viewer (the modal body).
 *
 * The viewer renders in the CDK overlay container at the document root —
 * locate it with a document-root loader
 * (`TestbedHarnessEnvironment.documentRootLoader(fixture)`).
 */
export class TmFilePreviewHarness extends ComponentHarness {
  /** The selector for the preview content element. */
  static hostSelector = 'tm-file-preview-content';

  /** Whether the lazy-source loading state is showing. */
  async isLoading(): Promise<boolean> {
    const region = await this.locatorFor('.tm-preview')();
    return (await region.getAttribute('aria-busy')) === 'true';
  }

  /** The unsupported/error card's title, or null when a renderer is up. */
  async getCardTitle(): Promise<string | null> {
    const title = await this.locatorForOptional('.tm-preview__card-title')();
    return title === null ? null : (await title.text()).trim();
  }

  /** The rendered plain-text content, or null for other kinds. */
  async getTextContent(): Promise<string | null> {
    const pre = await this.locatorForOptional('.tm-preview__text')();
    return pre === null ? null : pre.text();
  }

  /** The download link's target file name, or null while resolving. */
  async getDownloadName(): Promise<string | null> {
    const link = await this.locatorForOptional('[data-tm-preview-action="download"]')();
    return link === null ? null : link.getAttribute('download');
  }

  /** Whether the Print action is offered (images only). */
  async hasPrintButton(): Promise<boolean> {
    return (await this.locatorForOptional('[data-tm-preview-action="print"]')()) !== null;
  }
}
