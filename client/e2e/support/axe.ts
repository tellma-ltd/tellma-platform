// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The showcase suite's axe-core scan is the one every Tellma app uses: it
// ships in @tellma/core-testing/e2e, and the specs keep importing it from
// here under the names they have always used.
export {
  expectNoAxeViolations,
  tmAxeViolations as axeViolations,
  type TmAxeViolation as AxeViolation,
} from '@tellma/core-testing/e2e';
