// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs, warmUp } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, expect, test } from '../support/acme';
import {
  launcherField,
  launcherLanded,
  sessionButton,
  shownFrame,
  watchFrame,
} from '../support/shell';

// The launcher's instant feedback (spec 0027 §9.5, §14): Mod+K shows the
// panel, and each typed character the pages that match it, in the first frame
// painted after the key, since the pages' results are computed on the
// device and no provider's answer is awaited. The frames are a timing gate,
// so the flow runs in the nightly tier's performance gates; the report
// records each frame.

/** The latest frame the panel and the typed results may first show in: the first painted after the key. */
const LAUNCHER_FRAME = 1;

/** A query that finds Customers by a prefix of its label in each language. */
const CUSTOMERS_QUERY: Readonly<Record<AcmeLanguage, string>> = { en: 'cust', ar: 'عمل' };

/** The matched run of Customers' row in the typed state. */
const CUSTOMERS_MARK = '.tm-launcher__list--typed [href="/1/customers"] mark';

test.describe('launcher performance', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/1');
  });

  test('Mod+K shows the panel, and each key its results, in the frame after the key @perf', async ({
    page,
    lang,
  }) => {
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    const frames: number[] = [];

    // --- The panel, from Mod+K, once the chunk the shell prefetches at idle has landed ---
    await launcherLanded(page);
    await watchFrame(page, 'keydown', '[role="listbox"]');
    await page.keyboard.press('ControlOrMeta+K');
    frames.push(await shownFrame(page, 'the panel'));
    await expect(launcherField(page)).toBeFocused();

    // --- Each key of the query: Customers' row, its match marked as typed so far ---
    const query = CUSTOMERS_QUERY[lang];
    for (let length = 1; length <= query.length; length++) {
      const typed = query.slice(0, length);
      await watchFrame(page, 'input', CUSTOMERS_MARK, typed);
      await page.keyboard.type(query[length - 1]!);
      frames.push(await shownFrame(page, `the results of "${typed}"`));
    }

    test.info().annotations.push({ type: 'launcher frames', description: frames.join(', ') });
    for (const frame of frames) {
      expect(frame).toBeLessThanOrEqual(LAUNCHER_FRAME);
    }
  });
});
