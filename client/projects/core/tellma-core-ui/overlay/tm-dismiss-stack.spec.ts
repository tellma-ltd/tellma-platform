// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';

/** Dispatches a cancelable Escape (or another key) at the document body. */
function press(key = 'Escape'): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  document.body.dispatchEvent(event);
  return event;
}

describe('tmPushEscapeDismissal', () => {
  it('one Escape dismisses only the most recently pushed layer, and is consumed', () => {
    const calls: string[] = [];
    const releaseLower = tmPushEscapeDismissal(() => calls.push('lower'));
    const releaseUpper = tmPushEscapeDismissal(() => calls.push('upper'));
    try {
      const event = press();
      expect(calls).toEqual(['upper']);
      expect(event.defaultPrevented).toBe(true);

      // The upper layer closed and released; the next Escape reaches the lower one.
      releaseUpper();
      press();
      expect(calls).toEqual(['upper', 'lower']);
    } finally {
      releaseUpper();
      releaseLower();
    }
  });

  it('ignores other keys and leaves them unconsumed', () => {
    const calls: string[] = [];
    const release = tmPushEscapeDismissal(() => calls.push('layer'));
    try {
      const event = press('Enter');
      expect(calls).toEqual([]);
      expect(event.defaultPrevented).toBe(false);
    } finally {
      release();
    }
  });

  it('a release is idempotent and removes only its own layer', () => {
    const calls: string[] = [];
    const releaseLower = tmPushEscapeDismissal(() => calls.push('lower'));
    const releaseUpper = tmPushEscapeDismissal(() => calls.push('upper'));
    try {
      // Releasing the lower layer out of order keeps the upper one on top.
      releaseLower();
      releaseLower();
      press();
      expect(calls).toEqual(['upper']);
    } finally {
      releaseUpper();
    }
  });

  it('holds no document listener while the stack is empty', () => {
    const release = tmPushEscapeDismissal(() => undefined);
    release();
    // With nothing pushed, an Escape is neither consumed nor stopped.
    const event = press();
    expect(event.defaultPrevented).toBe(false);
  });
});
