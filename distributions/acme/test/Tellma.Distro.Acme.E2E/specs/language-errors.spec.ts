// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { test } from '../support/acme';

// The language, formats and errors flows (spec 0024 §13), and the time-zone
// flows. Each needs a surface not built yet: the language menu, the pages
// whose reads fail, Language & region, and the device-zone prompt. They are
// listed here, pending, and turn on with those surfaces.

test.describe('language, formats and errors', () => {
  // The language menu of the shells (spec 0027) and Language & region (spec 0032).
  test.fixme('a language switch re-renders in place', async () => {});
  test.fixme('language-switch: no request, and under 100 ms', async () => {});
  // A page whose read fails (specs 0030, 0031), answered through fakeProblem.
  test.fixme('an error dialog for each forbidden reason', async () => {});
  // Language & region (spec 0032).
  test.fixme('regional-formats: Acme in ar with Latin digits shows 1,234,567.89 and a Sunday-first date picker', async () => {});
  test.fixme('regional-formats: a Language & region change re-renders every tab with one request', async () => {});
});

test.describe('time zones', () => {
  // The device-zone prompt (spec 0024 §7.8) is not built yet; these turn on when it is.
  test.fixme('device-zone: a silent first run, a prompt after the idle gate, every tab re-rendered, the automatic toast', async () => {});
  test.fixme('device-zone: an override to Asia/Kolkata, reported as Asia/Calcutta, offers and saves Asia/Kolkata', async () => {});
  // Zone labels in grids (spec 0024 §7.8 over spec 0030's grids).
  test.fixme('zone-labels: labels present or absent with the device in London against a Riyadh preference @cross-engine', async () => {});
});
