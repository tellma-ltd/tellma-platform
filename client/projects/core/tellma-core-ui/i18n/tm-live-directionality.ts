// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, Injectable, type OnDestroy } from '@angular/core';
import { Directionality, type Direction } from '@angular/cdk/bidi';

/**
 * The root `Directionality` that follows `<html dir>` and `<body dir>` LIVE.
 * Internal to the library; `provideTellmaUi()` installs it at the root.
 *
 * The CDK's own root service reads the attributes once, at construction, so
 * a runtime language switch would leave every overlay, the grid's arrow
 * mapping and every submenu on the old direction. This one watches both
 * attributes with a `MutationObserver` and, on a change, sets the inherited
 * `valueSignal` and emits `change` — so open surfaces that subscribe follow
 * at once, and connected overlays take the new direction at their next
 * attach.
 *
 * `<body dir>` is read first, then `<html dir>`, exactly as the CDK's root
 * reads them. `[dir]` islands (the CDK `Dir` directive) still override
 * locally.
 */
@Injectable()
export class ɵTmLiveDirectionality extends Directionality implements OnDestroy {
  /** Watches both attributes; null without a window to observe in. */
  private readonly observer: MutationObserver | null = null;

  constructor() {
    super();
    const document = inject(DOCUMENT);
    // The document's direction: <body dir>, else <html dir>. Anything but
    // `rtl` (an absent attribute included) reads as left-to-right.
    const read = (): Direction =>
      (document.body?.dir || document.documentElement.dir || '').toLowerCase() === 'rtl'
        ? 'rtl'
        : 'ltr';
    // The base constructor read the attributes through the CDK's document
    // token; re-read through ours so both agree on a test-swapped document.
    this.valueSignal.set(read());
    const view = document.defaultView;
    if (view !== null && typeof view.MutationObserver === 'function') {
      // Publishes the attributes' direction when it differs from the current one.
      this.observer = new view.MutationObserver(() => {
        const next = read();
        if (next !== this.valueSignal()) {
          this.valueSignal.set(next);
          this.change.emit(next);
        }
      });
      const options: MutationObserverInit = { attributes: true, attributeFilter: ['dir'] };
      this.observer.observe(document.documentElement, options);
      if (document.body) {
        this.observer.observe(document.body, options);
      }
    }
  }

  /** Stops watching the attributes and completes `change`. */
  override ngOnDestroy(): void {
    this.observer?.disconnect();
    super.ngOnDestroy();
  }
}
