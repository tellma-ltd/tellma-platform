// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { bootstrapTellma } from '@tellma/core';

import { appConfig } from './app.config';

// Acme's entry point: the framework's start, which runs nothing while the
// boot script redirects or shows its boot page.
bootstrapTellma(appConfig).catch((error: unknown) => console.error(error));
