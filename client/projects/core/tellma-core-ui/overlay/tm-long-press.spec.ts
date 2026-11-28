// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmObserveLongPress } from '@tellma/core-ui/overlay';

describe('ɵtmObserveLongPress', () => {
  let element: HTMLElement;
  let stop: (() => void) | undefined;

  beforeEach(() => {
    element = document.createElement('div');
    document.body.appendChild(element);
  });

  afterEach(() => {
    stop?.();
    stop = undefined;
    element.remove();
  });

  const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

  /** A primary touch pointerdown on `target` (the observed element by default), bubbling to it. */
  function touchDown(x = 50, y = 60, target: Element = element): void {
    target.dispatchEvent(
      new PointerEvent('pointerdown', {
        pointerType: 'touch',
        isPrimary: true,
        pointerId: 1,
        clientX: x,
        clientY: y,
        bubbles: true,
      }),
    );
  }

  it('fires with the press point after the delay', async () => {
    const points: { x: number; y: number }[] = [];
    stop = ɵtmObserveLongPress(element, (point) => points.push(point), { delayMs: 30 });

    touchDown(50, 60);
    await sleep(80);

    expect(points).toEqual([{ x: 50, y: 60 }]);
  });

  it('movement beyond the slop cancels the press', async () => {
    const points: { x: number; y: number }[] = [];
    stop = ɵtmObserveLongPress(element, (point) => points.push(point), {
      delayMs: 30,
      slopPx: 8,
    });

    touchDown(50, 60);
    element.dispatchEvent(
      new PointerEvent('pointermove', {
        pointerType: 'touch',
        isPrimary: true,
        pointerId: 1,
        clientX: 62, // 12px > the 8px slop
        clientY: 60,
        bubbles: true,
      }),
    );
    await sleep(80);

    expect(points).toEqual([]);
  });

  describe('scrolling', () => {
    /** The long-press delay these tests observe with; fake timers advance past it. */
    const DELAY = 30;

    /** A scroller that holds the observed element. */
    let ancestor: HTMLElement;
    /** A scroller outside the observed element's ancestry: an auto-scrolling log, say. */
    let unrelated: HTMLElement;
    /** A scroller inside the observed element that holds the pressed node. */
    let holding: HTMLElement;
    /** A scroller inside the observed element beside the pressed node. */
    let beside: HTMLElement;
    /** The node the press lands on. */
    let pressed: HTMLElement;
    /** The points the observer fired with. */
    let points: { x: number; y: number }[];

    /** A 40px-tall scroller over 200px of content, so it can scroll. */
    function scroller(): HTMLElement {
      const box = document.createElement('div');
      box.style.cssText = 'overflow: auto; height: 40px; width: 120px;';
      const content = document.createElement('div');
      content.style.height = '200px';
      box.append(content);
      return box;
    }

    /** Resolves on the next animation frame: the frame's scroll events are dispatched by then. */
    function nextFrame(): Promise<void> {
      return new Promise((resolve) => requestAnimationFrame(() => resolve()));
    }

    /**
     * Scrolls `box` to `top` and resolves once the browser has dispatched the
     * scroll event. The observer listens at window capture, so it has seen the
     * event by the time this listener on the scroller runs.
     */
    async function scrollBox(box: HTMLElement, top: number): Promise<void> {
      const dispatched = new Promise<void>((resolve) =>
        box.addEventListener('scroll', () => resolve(), { once: true }),
      );
      box.scrollTop = top;
      expect(box.scrollTop, 'the scroller moved').toBe(top);
      await dispatched;
    }

    /** Scrolls the document to `top` and resolves once the browser has dispatched the scroll event. */
    async function scrollDocument(top: number): Promise<void> {
      const dispatched = new Promise<void>((resolve) =>
        document.addEventListener('scroll', () => resolve(), { once: true }),
      );
      window.scrollTo(0, top);
      expect(window.scrollY, 'the document moved').toBe(top);
      await dispatched;
    }

    beforeEach(async () => {
      // Only the timers are fake: the browser still renders frames and
      // dispatches real scroll events, and the press's timer advances only
      // when a test says so.
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

      // Layout: ancestor > element > (holding > pressed, beside); unrelated
      // sits beside the ancestor.
      ancestor = scroller();
      unrelated = scroller();
      holding = scroller();
      beside = scroller();
      pressed = document.createElement('span');
      pressed.textContent = 'Pressed';
      holding.prepend(pressed);
      element.append(holding, beside);
      ancestor.prepend(element);
      document.body.append(ancestor, unrelated);

      // Let any scroll still pending from earlier tests land before the
      // press starts, so only each test's own scroll reaches the observer.
      await nextFrame();

      points = [];
      stop = ɵtmObserveLongPress(element, (point) => points.push(point), { delayMs: DELAY });
    });

    afterEach(() => {
      ancestor.remove();
      unrelated.remove();
      vi.useRealTimers();
    });

    it('a scroll of an unrelated scroller leaves the press alive', async () => {
      touchDown(50, 60, pressed);
      await scrollBox(unrelated, 50);
      vi.advanceTimersByTime(DELAY);

      expect(points).toEqual([{ x: 50, y: 60 }]);
    });

    it('a scroll of an ancestor of the observed element cancels the press', async () => {
      touchDown(50, 60, pressed);
      await scrollBox(ancestor, 50);
      vi.advanceTimersByTime(DELAY);

      expect(points).toEqual([]);
    });

    it('a scroll of the document cancels the press', async () => {
      // A spacer makes the document itself scrollable for this test.
      const spacer = document.createElement('div');
      spacer.style.height = '300vh';
      document.body.append(spacer);
      try {
        touchDown(50, 60, pressed);
        await scrollDocument(100);
        vi.advanceTimersByTime(DELAY);

        expect(points).toEqual([]);
      } finally {
        // Scroll back and wait for that event too, so it cannot reach a
        // later test's press.
        if (window.scrollY !== 0) {
          await scrollDocument(0);
        }
        spacer.remove();
      }
    });

    it('a scroll of the observed element itself cancels the press', async () => {
      // Its two 40px children overflow a 40px element, so it scrolls.
      element.style.cssText = 'overflow: auto; height: 40px;';
      touchDown(50, 60, pressed);
      await scrollBox(element, 20);
      vi.advanceTimersByTime(DELAY);

      expect(points).toEqual([]);
    });

    it('a scroll inside the observed element that holds the pressed node cancels the press', async () => {
      touchDown(50, 60, pressed);
      await scrollBox(holding, 50);
      vi.advanceTimersByTime(DELAY);

      expect(points).toEqual([]);
    });

    it('a scroll inside the observed element beside the pressed node leaves the press alive', async () => {
      touchDown(50, 60, pressed);
      await scrollBox(beside, 50);
      vi.advanceTimersByTime(DELAY);

      expect(points).toEqual([{ x: 50, y: 60 }]);
    });

    it('a scroll of an ancestor cancels the press after the pressed node is detached', async () => {
      touchDown(50, 60, pressed);
      // A re-render replaces the pressed node while the finger is down.
      pressed.remove();
      await scrollBox(ancestor, 50);
      vi.advanceTimersByTime(DELAY);

      expect(points).toEqual([]);
    });

    it('a scroll of the observed element itself cancels the press after the pressed node is detached', async () => {
      element.style.cssText = 'overflow: auto; height: 40px;';
      touchDown(50, 60, pressed);
      pressed.remove();
      await scrollBox(element, 20);
      vi.advanceTimersByTime(DELAY);

      expect(points).toEqual([]);
    });

    it('a scroll of a light-DOM ancestor cancels the press on an element inside a shadow root', async () => {
      // ancestor > shadow host > (shadow root) > element.
      const shadowHost = document.createElement('div');
      ancestor.prepend(shadowHost);
      shadowHost.attachShadow({ mode: 'open' }).append(element);
      touchDown(50, 60, pressed);
      await scrollBox(ancestor, 50);
      vi.advanceTimersByTime(DELAY);

      expect(points).toEqual([]);
    });
  });

  it('releasing before the delay cancels the press', async () => {
    const points: { x: number; y: number }[] = [];
    stop = ɵtmObserveLongPress(element, (point) => points.push(point), { delayMs: 30 });

    touchDown();
    element.dispatchEvent(
      new PointerEvent('pointerup', {
        pointerType: 'touch',
        isPrimary: true,
        pointerId: 1,
        bubbles: true,
      }),
    );
    await sleep(80);

    expect(points).toEqual([]);
  });

  it('mouse presses never qualify — right-click owns that path', async () => {
    const points: { x: number; y: number }[] = [];
    stop = ɵtmObserveLongPress(element, (point) => points.push(point), { delayMs: 30 });

    element.dispatchEvent(
      new PointerEvent('pointerdown', {
        pointerType: 'mouse',
        isPrimary: true,
        pointerId: 1,
        clientX: 10,
        clientY: 10,
        bubbles: true,
      }),
    );
    await sleep(80);

    expect(points).toEqual([]);
  });
});
