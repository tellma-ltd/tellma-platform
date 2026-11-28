// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';

/**
 * A recorder of every CSS animation and transition a page starts, so a
 * spec can check what moves. Installed before the page loads, it listens
 * for `animationstart` and `transitionrun` on the whole document and keeps
 * one record per run.
 *
 * Only motion counts: an animation or transition that changes nothing but
 * appearance in place (a hover tint, a flash that fades a background, a
 * focus glow, a switch of visibility) is not motion and is recorded with
 * `motion: false`. A CSS animation counts as motion when any of its
 * keyframes animates a property beyond those.
 */

/** One animation or transition the page started. */
export interface MotionRecord {
  /** `animation` for a CSS animation, `transition` for a CSS transition. */
  readonly kind: 'animation' | 'transition';
  /** The `@keyframes` name of an animation (unscoped), or the property a transition runs on. */
  readonly name: string;
  /** The element that moved, as `tag.class.class`. */
  readonly target: string;
  /** Whether anything moves: a change beyond colour, the focus glow and visibility. */
  readonly motion: boolean;
}

/** The page-side record. */
interface MotionWindow {
  /** Every run recorded so far. */
  tmMotion?: MotionRecord[];
}

/**
 * Installs the recorder on every document the page loads from now on.
 * Call it before the first `goto`.
 */
export async function recordMotion(page: Page): Promise<void> {
  await page.addInitScript(() => {
    // Properties whose change moves nothing: colours, the focus glow (a
    // shadow that fades in where it stands) and visibility.
    const APPEARANCE = new Set([
      'color',
      'background-color',
      'border-color',
      'border-top-color',
      'border-right-color',
      'border-bottom-color',
      'border-left-color',
      'border-block-start-color',
      'border-block-end-color',
      'border-inline-start-color',
      'border-inline-end-color',
      'outline-color',
      'text-decoration-color',
      'fill',
      'stroke',
      'caret-color',
      'column-rule-color',
      'accent-color',
      'box-shadow',
      'visibility',
    ]);
    const records: MotionRecord[] = [];
    (window as MotionWindow).tmMotion = records;

    /** A short description of an element: its tag and classes. */
    const describe = (element: Element): string =>
      [element.localName, ...Array.from(element.classList)].join('.');

    /** The CSS property name as a stylesheet spells it. */
    const kebab = (name: string): string => name.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`);

    document.addEventListener(
      'animationstart',
      (event) => {
        const target = event.target as Element;
        // The running animation's keyframes say what it changes.
        const animation = target
          .getAnimations()
          .find((candidate) => (candidate as CSSAnimation).animationName === event.animationName);
        const properties = (animation?.effect as KeyframeEffect | undefined)
          ?.getKeyframes()
          .flatMap((frame) =>
            Object.keys(frame).filter(
              (key) => !['offset', 'computedOffset', 'easing', 'composite'].includes(key),
            ),
          )
          .map(kebab);
        records.push({
          kind: 'animation',
          // Emulated encapsulation scopes a component's @keyframes as
          // `_ngcontent-<id>_<name>`; the record keeps the name as written.
          name: event.animationName.replace(/^_ngcontent-[^_]+_/, ''),
          target: describe(target),
          // Unknown keyframes count as motion: the safe side.
          motion: properties === undefined || properties.some((name) => !APPEARANCE.has(name)),
        });
      },
      { capture: true },
    );
    document.addEventListener(
      'transitionrun',
      (event) => {
        records.push({
          kind: 'transition',
          name: event.propertyName,
          target: describe(event.target as Element),
          motion: !APPEARANCE.has(event.propertyName),
        });
      },
      { capture: true },
    );
  });
}

/** What the recorder has seen since the page loaded. */
export function recordedMotion(page: Page): Promise<MotionRecord[]> {
  return page.evaluate(() => [...((window as MotionWindow).tmMotion ?? [])]);
}

// Spec 0023 §2.2 lists the first four entries as the only motion of the
// additions; the rest belong to the older components they compose.
/**
 * The motion a library story may show, and why, each as a test on a
 * record: the switch knob, the progress sweep, the progress ring's spin
 * and the centred modal's fade (which confirm and error-dialog inherit),
 * then the motion of the components the stories compose: the spinner's
 * spin, the tooltip and popover entrances, and the select caret's turn.
 * Every one of them is static under `prefers-reduced-motion: reduce`.
 */
export const ALLOWED_MOTION: readonly {
  /** Why this motion is allowed. */
  readonly reason: string;
  /** Whether a record is this motion. */
  readonly matches: (record: MotionRecord) => boolean;
}[] = [
  {
    reason: 'the switch knob travels along its track',
    matches: (record) =>
      record.kind === 'transition' && record.target.split('.').includes('tm-switch__knob'),
  },
  {
    reason: 'the progress bar sweeps while its share is unknown',
    matches: (record) =>
      record.kind === 'animation' &&
      (record.name === 'tm-progress-sweep' || record.name === 'tm-progress-sweep-rtl'),
  },
  {
    reason: 'the progress ring spins while its share is unknown',
    matches: (record) => record.kind === 'animation' && record.name === 'tm-progress-ring-spin',
  },
  {
    reason: 'the centred modal fades in, and its backdrop fades',
    matches: (record) =>
      (record.kind === 'animation' && record.name === 'tm-modal-enter') ||
      (record.kind === 'transition' &&
        record.name === 'opacity' &&
        record.target.split('.').some((name) => name.includes('backdrop'))),
  },
  {
    reason: 'the spinner spins (the busy overlay, and a lazy tree or tiles row)',
    matches: (record) => record.kind === 'animation' && record.name === 'tm-spinner-spin',
  },
  {
    reason: 'a tooltip fades in (copy, chip, segmented)',
    matches: (record) => record.kind === 'animation' && record.name === 'tm-tooltip-enter',
  },
  {
    reason: 'a popover panel fades in (the date and select popups)',
    matches: (record) => record.kind === 'animation' && record.name === 'tm-popover-enter',
  },
  {
    reason: 'the select caret turns over as its panel opens (the pager size)',
    matches: (record) =>
      record.kind === 'transition' &&
      record.name === 'transform' &&
      record.target.split('.').some((name) => name.startsWith('tm-select')),
  },
];
