// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

declare global {
  interface Window {
    /**
     * Resolves once the application is stable: no change detection is
     * scheduled and no pending task (navigation, HTTP request) remains.
     * The Playwright suite awaits it where the DOM alone cannot tell a
     * settled page from one whose next render is still queued — a
     * keystroke's effects land on the following render, and until then the
     * DOM still shows the previous state.
     */
    tmShowcaseWhenStable?: () => Promise<void>;
  }
}

bootstrapApplication(App, appConfig)
  .then((appRef) => {
    // The browser-test stability hook (see the declaration above).
    window.tmShowcaseWhenStable = () => appRef.whenStable();
  })
  .catch((err) => console.error(err));
