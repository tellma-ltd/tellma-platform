// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The unit runner's configuration, which every test target in angular.json
// names as its runnerConfig: Vitest's default reporters, then the watchdog that
// ends a run with no progress and writes what was pending, as a hung run would
// otherwise print nothing until its CI job is cancelled; and before each
// test, the restore of every spy and stub an earlier test or file left in the
// shared page. scripts/test.mjs refuses a test target that does not name this
// file.
export { default } from '@tellma/core-tools/vitest';
