import { AxeBuilder } from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';

/**
 * Runs an axe-core scan (WCAG 2.1 A/AA rule set) and fails the test on any
 * violation — the static accessibility floor (spec 0002 §6); behavioral
 * keyboard/focus/announcement coverage lives in the dedicated specs.
 */
export async function expectNoAxeViolations(page: Page, selector?: string): Promise<void> {
  let builder = new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']);
  if (selector) {
    builder = builder.include(selector);
  }
  const results = await builder.analyze();
  expect(
    results.violations,
    results.violations
      .map((v) => `${v.id}: ${v.help} -> ${v.nodes.map((n) => n.target).join(', ')}`)
      .join('\n'),
  ).toEqual([]);
}
