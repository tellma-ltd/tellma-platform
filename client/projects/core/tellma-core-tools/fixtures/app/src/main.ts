// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { bootstrapApplication } from '@angular/platform-browser';

import { appConfig } from './app.config';
import { TmFixtureApp } from './app/fixture-app';

// The fixture's entry point: one root component under the app configuration.
bootstrapApplication(TmFixtureApp, appConfig).catch((error: unknown) => console.error(error));
