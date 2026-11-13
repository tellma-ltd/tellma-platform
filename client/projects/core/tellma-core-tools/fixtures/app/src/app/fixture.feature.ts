// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmFeature, tmPage } from '@tellma/core';

/** The fixture's one feature: a page whose layout is a lazy chunk of its own. */
export const fixtureFeature = tmFeature({
  key: 'fixture',
  pages: [tmPage('review', { component: () => import('./review-page'), titleKey: 'Fixture_Review' })],
});
