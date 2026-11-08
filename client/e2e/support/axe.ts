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
 * assertion reads. Same-origin frames (including `about:blank` and
 * `srcdoc` ones) are scanned as before. One capability is given up:
 * `AxeBuilder` also scanned cross-origin frames, and this scan refuses a
 * cross-origin frame inside its scope with an error rather than report a
 * partial result. No story embeds one.
 */

/** The page-side shape of a document that has axe-core loaded. */
interface AxeWindow {
  /** The axe-core API, once its source has been evaluated in the document. */
  readonly axe?: typeof axeCore;
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

/** One violation as {@link axeViolations} reports it. */
export interface AxeViolation {
  /** The rule id (`color-contrast`, `button-name`, …). */
  readonly id: string;
  /** The rule's one-line description. */
  readonly help: string;
  /** The CSS selector path of every offending node (a frame's nodes are prefixed by the frame). */
  readonly targets: readonly string[];
}

/**
 * Runs the axe-core scan over the page (or only inside `selector`) and fails
 * the test on any violation, listing each rule and the nodes that break it.
 */
export async function expectNoAxeViolations(page: Page, selector?: string): Promise<void> {
  const violations = await axeViolations(page, selector);
  expect(
    violations,
    violations.map((v) => `${v.id}: ${v.help} -> ${v.targets.join(', ')}`).join('\n'),
  ).toEqual([]);
}

/**
 * Runs the axe-core scan over the page (or only inside `selector`) and
 * returns its violations, without asserting on them. Tests assert through
 * {@link expectNoAxeViolations}; this form lets a test check what a scan
 * reports.
 */
export async function axeViolations(
  page: Page,
  selector?: string,
): Promise<readonly AxeViolation[]> {
  await loadAxe(page, selector);
  const request: ScanRequest = {
    include: selector === undefined ? null : [selector],
    exclude: EXCLUDED,
    tags: WCAG_TAGS,
  };
  return page.evaluate(async ({ include, exclude, tags }: ScanRequest) => {
    const axe = (window as AxeWindow).axe;
    if (axe === undefined) {
      throw new Error('axe-core is not loaded in this document.');
    }
    const context =
      include === null
        ? { exclude: [...exclude] }
        : { include: [...include], exclude: [...exclude] };
    const results = await axe.run(context, {
      runOnly: { type: 'tag', values: [...tags] },
      // Full node detail for violations only: axe still evaluates every
      // rule on every node, and only trims the serialized passes,
      // incomplete and inapplicable groups to one node each, which is where
      // most of a large page's reporting time goes.
      resultTypes: ['violations'],
    });
    // Only what the assertion message needs crosses back to the test.
    return results.violations.map((violation): AxeViolation => ({
      id: violation.id,
      help: violation.help,
      targets: violation.nodes.map((node) => String(node.target)),
    }));
  }, request);
}

/**
 * Makes axe-core available in the page's document and in every frame inside
 * the scan's scope (the whole page, or the frames under `selector`),
 * evaluating its source only where it is not loaded yet: a reload or a
 * navigation starts a new document, which loads it again on its next scan.
 *
 * The scan runs from the top document and reaches each frame through
 * axe-core's own frame messaging, which here answers only frames of the
 * same origin. Cross-origin frames inside the scope are therefore not
 * supported: one fails the scan with an error, rather than leave a partial
 * result. Frames outside the scope are left alone, whatever their origin.
 */
async function loadAxe(page: Page, selector: string | undefined): Promise<void> {
  const main = page.mainFrame();
  // `self.origin` is the document's origin: an about:blank or srcdoc frame
  // inherits its parent's, where `location.origin` would report "null".
  const origin = await main.evaluate(() => self.origin);
  await loadAxeInto(main);
  for (const frame of page.frames()) {
    if (frame === main || frame.isDetached()) {
      continue;
    }
    if (selector !== undefined && !(await insideScope(frame, selector))) {
      continue;
    }
    const frameOrigin = await frame.evaluate(() => self.origin);
    if (frameOrigin !== origin) {
      throw new Error(
        `The frame at ${frame.url()} is cross-origin (${frameOrigin}), which the axe scan does not support.`,
      );
    }
    await loadAxeInto(frame);
  }
}

/**
 * Whether `frame` lies inside the scan scope `selector`: its outermost
 * ancestor frame under the top document is owned by an element that matches
 * `selector` or sits inside a match, shadow roots included.
 */
async function insideScope(frame: Frame, selector: string): Promise<boolean> {
  let outermost = frame;
  for (let parent = frame.parentFrame(); parent?.parentFrame(); parent = parent.parentFrame()) {
    outermost = parent;
  }
  const owner = await outermost.frameElement();
  try {
    return await owner.evaluate((element, scope) => {
      // Up through the ancestors, crossing shadow roots to their hosts, as
      // axe's own context matching does.
      for (let node: Element | null = element as Element; node !== null;) {
        if (node.matches(scope)) {
          return true;
        }
        const root = node.getRootNode();
        node = node.parentElement ?? (root instanceof ShadowRoot ? root.host : null);
      }
      return false;
    }, selector);
  } finally {
    await owner.dispose();
  }
}

/** Evaluates the axe-core source in `frame`, unless that document has it already. */
async function loadAxeInto(frame: Frame): Promise<void> {
  const loaded = await frame.evaluate(() => typeof (window as AxeWindow).axe?.run === 'function');
  if (!loaded) {
    await frame.evaluate(axeCore.source);
  }
}
