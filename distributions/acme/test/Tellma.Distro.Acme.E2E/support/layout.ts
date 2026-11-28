// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Page } from '@playwright/test';

// The layout-shift record the stable-layout flows share: every shift the
// browser reports that no input caused, with the elements that moved and
// their boxes before and after, for the failure message.

/** One layout shift the page reported, as the failure message shows it. */
export interface AcmeShift {
  /** The shift's score. */
  readonly value: number;
  /** When it happened, in milliseconds from the document's start. */
  readonly startTime: number;
  /** The elements that moved, up to the five the browser names. */
  readonly sources: readonly {
    /** A short selector path: the node, then up to three of its ancestors. */
    readonly node: string;
    /** Its box before the shift, as `x,y widthxheight`. */
    readonly previousRect: string;
    /** Its box after the shift, as `x,y widthxheight`. */
    readonly currentRect: string;
  }[];
}

/**
 * Lists the shifts, one line per source, so a failure can be read from the
 * log alone.
 *
 * @param shifts - The shifts the page reported.
 * @returns The failure message.
 */
export function describeShifts(shifts: readonly AcmeShift[]): string {
  const lines = shifts.flatMap((shift) => [
    `shift ${shift.value} at ${shift.startTime.toFixed(1)} ms`,
    ...shift.sources.map(
      (source) => `  ${source.node}: ${source.previousRect} -> ${source.currentRect}`,
    ),
  ]);
  return ['the page shifted:', ...lines].join('\n');
}

/**
 * Records every layout shift of the page's next documents, from the first
 * frame on, with the elements that moved and their boxes before and after.
 *
 * @param page - The page.
 * @returns A promise that settles once the recording is installed.
 */
export async function recordShifts(page: Page): Promise<void> {
  await page.context().addInitScript(() => {
    // The node's tag, id and first two classes, then its ancestors', up to four in all.
    const path = (node: Node | null): string => {
      const names: string[] = node !== null && !(node instanceof Element) ? [node.nodeName] : [];
      let element = node instanceof Element ? node : (node?.parentElement ?? null);
      while (element !== null && names.length < 4) {
        const id = element.id === '' ? '' : `#${element.id}`;
        const classes = Array.from(element.classList)
          .slice(0, 2)
          .map((name) => `.${name}`)
          .join('');
        names.push(`${element.localName}${id}${classes}`);
        element = element.parentElement;
      }
      return names.length === 0 ? '(removed node)' : names.join(' < ');
    };
    const rect = (box: DOMRectReadOnly): string => `${box.x},${box.y} ${box.width}x${box.height}`;
    const shifts: AcmeShift[] = ((window as { __acmeShifts?: AcmeShift[] }).__acmeShifts = []);
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as (PerformanceEntry & {
        value: number;
        hadRecentInput: boolean;
        sources?: readonly {
          node: Node | null;
          previousRect: DOMRectReadOnly;
          currentRect: DOMRectReadOnly;
        }[];
      })[]) {
        if (!entry.hadRecentInput) {
          shifts.push({
            value: entry.value,
            startTime: entry.startTime,
            sources: (entry.sources ?? []).map((source) => ({
              node: path(source.node),
              previousRect: rect(source.previousRect),
              currentRect: rect(source.currentRect),
            })),
          });
        }
      }
    }).observe({ type: 'layout-shift', buffered: true });
  });
}

/**
 * The shifts the page's current document reported, read two frames on: a
 * shift is reported in the frame after it happens.
 *
 * @param page - The page.
 * @returns The shifts.
 */
export function settledShifts(page: Page): Promise<AcmeShift[]> {
  return page.evaluate(
    () =>
      new Promise<AcmeShift[]>((settle) =>
        requestAnimationFrame(() =>
          requestAnimationFrame(() =>
            settle((window as { __acmeShifts?: AcmeShift[] }).__acmeShifts ?? []),
          ),
        ),
      ),
  );
}
