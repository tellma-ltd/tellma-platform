// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import {
  TmKbd,
  tmAriaKeyShortcuts,
  tmIsApplePlatform,
  tmKeysMatch,
  tmKeysText,
  type TmKeys,
} from '@tellma/core-ui/kbd';

/** The bindings the story labels and the key tester matches. */
const BINDINGS: readonly TmKeys[] = [
  'Mod+K',
  'Mod+D',
  'Mod+Shift+D',
  'Mod+/',
  '?',
  'Mod+Alt+Plus',
  'Alt+Shift+P',
  'Mod+Enter',
  'F2',
  'Escape',
];

/**
 * tm-kbd and the kbd helpers: each binding labelled for this platform and
 * for both platforms explicitly, a hint inside a sentence (so RTL shows
 * the caps keeping keyboard order), and a key tester that reports which
 * bindings a keydown matches — the place to try a non-Latin layout.
 */
@Component({
  imports: [TmKbd],
  template: `
    <h2>Keyboard keys</h2>

    <!-- Each binding on this platform, and on both platforms forced. -->
    <section>
      <h3>Bindings</h3>
      <p>This platform: {{ apple ? 'Apple' : 'Windows / Linux' }}.</p>
      <table class="bindings" data-testid="bindings">
        <thead>
          <tr>
            <th scope="col">TmKeys</th>
            <th scope="col">This platform</th>
            <th scope="col">Windows / Linux</th>
            <th scope="col">Apple</th>
            <th scope="col">Text</th>
            <th scope="col">aria-keyshortcuts</th>
          </tr>
        </thead>
        <tbody>
          @for (keys of bindings; track keys) {
            <tr [attr.data-keys]="keys">
              <td>
                <code>{{ keys }}</code>
              </td>
              <td><tm-kbd [keys]="keys" data-testid="native" /></td>
              <td><tm-kbd [keys]="keys" [apple]="false" data-testid="windows" /></td>
              <td><tm-kbd [keys]="keys" [apple]="true" data-testid="apple" /></td>
              <td>{{ text(keys, false) }} · {{ text(keys, true) }}</td>
              <td>
                <code>{{ aria(keys) }}</code>
              </td>
            </tr>
          }
        </tbody>
      </table>
    </section>

    <!-- In prose; under ?dir=rtl the caps still read left to right. -->
    <section>
      <h3>In a sentence</h3>
      <p data-testid="sentence">
        Press <tm-kbd keys="Mod+K" data-testid="sentence-kbd" /> to open the launcher, or
        <tm-kbd keys="Mod+/" /> for every shortcut.
      </p>
    </section>

    <!-- A keydown readout: what the layout sent, and which bindings match. -->
    <section>
      <h3>Key tester</h3>
      <label for="key-tester">Press a combination here</label>
      <input
        id="key-tester"
        class="tester"
        data-testid="key-tester"
        autocomplete="off"
        (keydown)="onKeydown($event)"
      />
      <output for="key-tester" class="readout" data-testid="readout">
        @if (last(); as event) {
          key <code data-testid="last-key">{{ event.key }}</code
          >, code <code data-testid="last-code">{{ event.code }}</code
          >; matches:
          <span data-testid="matched">{{ event.matched.join(', ') || 'none' }}</span>
        } @else {
          Nothing pressed yet.
        }
      </output>
    </section>
  `,
  styles: `
    .bindings {
      border-collapse: collapse;
      font-size: 13px;
    }
    .bindings th,
    .bindings td {
      padding: 4px 10px;
      text-align: start;
      border-block-end: 1px solid var(--border-subtle);
    }
    .tester {
      display: block;
      margin-block: 6px;
      inline-size: 280px;
      padding: 6px 8px;
    }
    .readout {
      display: block;
      min-block-size: 20px;
    }
  `,
})
export class KbdStory {
  protected readonly bindings = BINDINGS;
  protected readonly apple = tmIsApplePlatform();
  protected readonly last = signal<
    { readonly key: string; readonly code: string; readonly matched: readonly TmKeys[] } | undefined
  >(undefined);

  protected text(keys: TmKeys, apple: boolean): string {
    return tmKeysText(keys, apple);
  }

  protected aria(keys: TmKeys): string {
    return tmAriaKeyShortcuts(keys);
  }

  /** Records the keydown and the bindings it matches. */
  protected onKeydown(event: KeyboardEvent): void {
    if (['Control', 'Alt', 'Shift', 'Meta', 'AltGraph'].includes(event.key)) {
      return;
    }
    const matched = this.bindings.filter((keys) => tmKeysMatch(event, keys));
    // A matched binding is the tester's to handle, not the browser's.
    if (matched.length > 0 && event.key !== 'Escape') {
      event.preventDefault();
    }
    this.last.set({ key: event.key, code: event.code, matched });
  }
}
