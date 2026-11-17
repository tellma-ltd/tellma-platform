// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { provideBrowserGlobalErrorListeners } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import type { TmBootResult } from '@tellma/core';

import { appConfig } from './app.config';
import { AcmeApp } from './app/acme-app';

// Acme's entry point. It starts the app only when the boot script decided to
// run it: while the script redirects or shows its boot page, nothing starts.
// It stands in for `bootstrapTellma(appConfig)`, which makes the same call
// with the framework's own root component.
const boot = (window as Window & { readonly __tmBoot?: TmBootResult }).__tmBoot;
if (boot?.state === 'run') {
  bootstrapApplication(AcmeApp, {
    providers: [provideBrowserGlobalErrorListeners(), ...appConfig.providers],
  }).catch((error: unknown) => console.error(error));
}
