// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, input, type Signal } from '@angular/core';
import { TmKbd } from '@tellma/core-ui/kbd';

import type { TmStringTree } from '../app/tm-app-config';
import { TmLocalization } from '../l10n/tm-localization';

// What shows when the support entry point cannot load, as offline before the
// worker cached it (spec 0026 §8.4, §12.2): one line naming Copy
// diagnostics' shortcut, which works without it. The line ships with the
// primary's support host chunk, which the startup preloads at idle and the
// worker caches with the shell, not with @tellma/core/support, since that is
// the chunk that did not load; so do the support dialog's title and Close,
// which the dialog shows before that chunk lands. Nothing on the first frame
// imports this file. The strings register under `tm.supportHost` when the
// line or the support host is first created.

/** The support host's English strings, under `tm.supportHost`. */
export const TM_SUPPORT_HOST_STRINGS_EN = {
  title: 'Report a problem',
  loading: 'Loading',
  close: 'Close',
  dialog: "This isn't available offline. Press {keys} to copy diagnostics.",
  diagnostics: "Diagnostics aren't available offline. Press {keys} to copy them.",
} as const satisfies TmStringTree;

/** Copy diagnostics' shortcut, as the app's shortcut registry binds it. */
const COPY_KEYS = 'Mod+Alt+Shift+D';

/** Stands for the keys in the message, so the line can split around them; a private-use character no translation holds. */
const KEYS_MARK = '';

/**
 * Registers the support host's strings, once.
 *
 * @param l10n - The localization service.
 */
export function tmRegisterSupportHostStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings('supportHost', TM_SUPPORT_HOST_STRINGS_EN);
}

/**
 * The line that shows when the support entry point cannot load: the support
 * dialog's (`dialog`) or the About dialog's Diagnostics tab's
 * (`diagnostics`), with Copy diagnostics' keys. Internal.
 */
@Component({
  selector: 'tm-support-offline',
  imports: [TmKbd],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .tm-support-offline {
      margin: 0;
      color: var(--text-body);
      overflow-wrap: anywhere;
    }
  `,
  template: `
    <!-- The message, its keys rendered as keycaps where the translation places them. -->
    <p class="tm-support-offline" role="status">{{ parts()[0] }}<tm-kbd [keys]="keys" />{{ parts()[1] }}</p>
  `,
})
export class ɵTmSupportOffline {
  readonly #l10n = inject(TmLocalization);

  /** Which surface failed to load: the support dialog's content, or the Diagnostics panel. */
  readonly kind = input<'dialog' | 'diagnostics'>('dialog');

  /** Copy diagnostics' keys. */
  protected readonly keys = COPY_KEYS;

  /** The message's text before and after the keys. */
  protected readonly parts: Signal<readonly [string, string]> = computed(() => {
    const text = this.#l10n.ɵtext(`tm.supportHost.${this.kind()}`, { keys: KEYS_MARK })();
    const at = text.indexOf(KEYS_MARK);
    return at < 0 ? [text, ''] : [text.slice(0, at), text.slice(at + KEYS_MARK.length)];
  });

  /** Registers the strings. */
  constructor() {
    tmRegisterSupportHostStrings(this.#l10n);
  }
}
