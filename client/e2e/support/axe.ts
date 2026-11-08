// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import axeCore from 'axe-core';
import { expect, type Frame, type Page } from '@playwright/test';

// The static accessibility floor of spec 0002 §6.

/**
 * The axe-core static accessibility floor: one scan of the WCAG 2.1 A/AA
 * rule set over the page as it stands. Behavioral keyboard, focus and
 * announcement coverage lives in the dedicated specs.
 *
 * The scan runs axe-core directly inside the page rather than through
 * `AxeBuilder.analyze()`. That call re-injects the whole axe-core source on
 * every scan, then opens a second, blank page to merge the results and
 * closes it again; on the grid stories this bookkeeping cost more than the
 * rules themselves, and under load it pushed tests that scan several states
 * to their timeout. Here axe-core is loaded once per document and reused,
 * and every scan runs the same rules over the same nodes: only the
 * reporting of rule groups other than violations is trimmed, which no
 * assertion reads.
 */

/** The page-side shape of a document that has axe-core loaded. */
interface AxeWindow {
  /** The axe-core API, once its source has been evaluated in the document. */
  readonly axe?: typeof axeCore;
}

/** One violation as the scan reports it back to the test. */
interface ScanViolation {
  /** The rule id (`color-contrast`, `button-name`, …). */
  readonly id: string;
  /** The rule's one-line description. */
  readonly help: string;
  /** The CSS selector path of every offending node. */
  readonly targets: readonly string[];
}

/** The rule set every scan runs: WCAG 2.1 levels A and AA. */
const WCAG_TAGS: readonly string[] = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

/**
 * Elements no scan includes. Justified contrast carve-out: placeholder text
 * is supplementary — the visible label carries the accessible name and every
 * required datum — and the brand grey-400 placeholder is a deliberate
 * de-emphasis. Native ::placeholder is outside axe's reach; tm-select renders
 * its placeholder as real text, so the same decision is applied here
 * explicitly.
 */
const EXCLUDED: readonly string[] = ['.tm-select__value--placeholder'];

/**
 * What one scan runs, as plain data the page receives. `include` is `null`
 * for the whole document.
 */
interface ScanRequest {
  /** The selectors the scan is limited to, or `null` for the whole document. */
  readonly include: readonly string[] | null;
  /** The selectors the scan leaves out. */
  readonly exclude: readonly string[];
  /** The axe-core rule tags the scan runs. */
  readonly tags: readonly string[];
}

/**
 * Runs the axe-core scan over the page (or only inside `selector`) and fails
 * the test on any violation, listing each rule and the nodes that break it.
 */
export async function expectNoAxeViolations(page: Page, selector?: string): Promise<void> {
  await loadAxe(page);
  const request: ScanRequest = {
    include: selector === undefined ? null : [selector],
    exclude: EXCLUDED,
    tags: WCAG_TAGS,
  };
  const violations = await page.evaluate(async ({ include, exclude, tags }: ScanRequest) => {
    const axe = (window as AxeWindow).axe;
    if (axe === undefined) {
      throw new Error('axe-core is not loaded in this document.');
    }
    const context =
      include === null ? { exclude: [...exclude] } : { include: [...include], exclude: [...exclude] };
    const results = await axe.run(context, {
      runOnly: { type: 'tag', values: [...tags] },
      // Full node detail for violations only: axe still evaluates every
      // rule on every node, and only trims the serialized passes,
      // incomplete and inapplicable groups to one node each, which is where
      // most of a large page's reporting time goes.
      resultTypes: ['violations'],
    });
    // Only what the assertion message needs crosses back to the test.
    return results.violations.map(
      (violation): ScanViolation => ({
        id: violation.id,
        help: violation.help,
        targets: violation.nodes.map((node) => String(node.target)),
      }),
    );
  }, request);
  expect(
    violations,
    violations.map((v) => `${v.id}: ${v.help} -> ${v.targets.join(', ')}`).join('\n'),
  ).toEqual([]);
}

/**
 * Makes axe-core available in the page's document and in every frame inside
 * it, evaluating its source only where it is not loaded yet: a reload or a
 * navigation starts a new document, which loads it again on its next scan.
 *
 * The scan runs from the top document and reaches each frame through
 * axe-core's own frame messaging, which only answers frames of the same
 * origin. A cross-origin frame would silently drop out of the scan, so it
 * fails the scan instead.
 */
async function loadAxe(page: Page): Promise<void> {
  const main = page.mainFrame();
  const origin = await main.evaluate(() => location.origin);
  for (const frame of page.frames()) {
    if (frame.isDetached()) {
      continue;
    }
    if (frame !== main) {
      const frameOrigin = await frame.evaluate(() => location.origin);
      if (frameOrigin !== origin) {
        throw new Error(
          `The frame at ${frame.url()} is cross-origin (${frameOrigin}); the scan cannot reach it.`,
        );
      }
    }
    await loadAxeInto(frame);
  }
}

/** Evaluates the axe-core source in `frame`, unless that document has it already. */
async function loadAxeInto(frame: Frame): Promise<void> {
  const loaded = await frame.evaluate(() => typeof (window as AxeWindow).axe?.run === 'function');
  if (!loaded) {
    await frame.evaluate(axeCore.source);
  }
}
