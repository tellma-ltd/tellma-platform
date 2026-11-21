// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmFeature, type TmFeature } from './tm-feature';

// The platform's implicit `core` feature (spec 0024 §2.3). It merges before
// every feature a distribution lists, and holds the stock pages and sections
// of the platform's page frameworks: each framework adds its declarations to
// it as the framework lands.

/** The key of the platform's implicit feature. */
export const TM_CORE_FEATURE_KEY = 'core';

/** The platform's implicit feature, merged before every feature an app lists. */
export const TM_CORE_FEATURE: TmFeature = tmFeature({ key: 'core' });
