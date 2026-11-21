// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The arrow keys of a group of native radios (the APG radio group, which
 * the engines map to the page direction inconsistently, so Tellma's radio
 * controls handle the arrows themselves): Up and Left (mapped) move to the
 * previous enabled radio, Down and Right (mapped) to the next, wrapping at
 * both ends. Left and Right are mapped by the host's live computed
 * direction, read at keydown, so a switch to RTL needs no reload.
 *
 * Returns the radio to focus and check, or `null` when the key is not an
 * unmodified arrow or the event did not come from one of the radios.
 *
 * @internal Shared by `tm-radio-group` and `tm-segmented`; never use directly.
 */
export function ɵtmRadioArrowTarget(
  event: KeyboardEvent,
  radios: readonly HTMLInputElement[],
  host: HTMLElement,
): HTMLInputElement | null {
  if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
    return null;
  }
  const rtl = getComputedStyle(host).direction === 'rtl';
  let step: number;
  switch (event.key) {
    case 'ArrowUp':
      step = -1;
      break;
    case 'ArrowDown':
      step = 1;
      break;
    case 'ArrowLeft':
      step = rtl ? 1 : -1;
      break;
    case 'ArrowRight':
      step = rtl ? -1 : 1;
      break;
    default:
      return null;
  }
  const current = radios.indexOf(event.target as HTMLInputElement);
  if (current === -1) {
    return null;
  }
  // Walk in the step's direction, wrapping, to the next enabled radio; a
  // group whose other radios are all disabled stays where it is.
  for (let offset = 1; offset <= radios.length; offset++) {
    const candidate = radios[(current + step * offset + radios.length * offset) % radios.length];
    if (!candidate.disabled) {
      return candidate;
    }
  }
  return null;
}
