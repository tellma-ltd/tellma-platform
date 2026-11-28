// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { bootstrapTellma } from '@tellma/core';

import { appConfig } from './app.config';

// The fixture's entry point, as a distribution's scaffold writes it.
bootstrapTellma(appConfig).catch((error: unknown) => console.error(error));
