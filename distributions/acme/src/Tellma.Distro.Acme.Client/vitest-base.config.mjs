// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The unit runner's configuration, which the test target in angular.json
// names as its runnerConfig: Vitest's default reporters, then the watchdog that
// ends a run with no progress and writes what was pending, as a hung run would
// otherwise print nothing until its CI job is cancelled.
export { default } from '@tellma/core-tools/vitest';
