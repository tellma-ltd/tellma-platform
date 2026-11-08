// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, input } from '@angular/core';

import { tmFormatKeys, tmIsApplePlatform, type TmKeys } from './tm-keys';

/**
 * Shows a key combination as key caps: one `<kbd>` per key, modifiers
 * first, labelled for the platform ("Ctrl" "K" on Windows and Linux, "⌘"
 * "K" on Apple platforms).
 *
 * The caps are 18px tall and always run left to right with Latin key
 * names, in every language and direction, so a hint reads the way the
 * keyboard does. For a combination inside a sentence or a tooltip, use
 * `tmKeysText` instead; to match a keydown against the same combination,
 * `tmKeysMatch`.
 *
 * @tmGroup indicator
 * @tmA11yNotes Plain `<kbd>` text in a dir="ltr" run; no role. Put
 *   `tmAriaKeyShortcuts(keys)` in `aria-keyshortcuts` on the control the
 *   shortcut triggers.
 */
@Component({
  selector: 'tm-kbd',
  // One cap per key; the separator is the gap, not a "+" glyph. (The note
  // lives here rather than in the markup: the template ships in the bundle.)
  template: `@for (cap of caps(); track $index) {
    <kbd class="tm-kbd__key">{{ cap }}</kbd>
  }`,
  styleUrl: './tm-kbd.css',
  host: { class: 'tm-kbd', dir: 'ltr' },
})
export class TmKbd {
  /** The combination, e.g. `'Mod+K'` or `'Alt+Shift+P'`. */
  readonly keys = input.required<TmKeys>();

  /**
   * Labels for an Apple platform (`true`) or for Windows and Linux
   * (`false`), whatever the current platform; unset, the current one.
   */
  readonly apple = input<boolean | undefined>(undefined);

  /** The cap labels, one per key. */
  protected readonly caps = computed(() =>
    tmFormatKeys(this.keys(), this.apple() ?? tmIsApplePlatform()),
  );
}
