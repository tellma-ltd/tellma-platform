// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import * as kbd from '@tellma/core-ui/kbd';
import * as keys from '@tellma/core-ui/keys';

import { TmKbd } from './tm-kbd';

@Component({
  imports: [TmKbd],
  template: `
    <div dir="rtl">
      <tm-kbd [keys]="keys()" [apple]="apple()" />
    </div>
  `,
})
class Host {
  readonly keys = signal('Mod+Shift+K');
  readonly apple = signal<boolean | undefined>(false);
}

describe('tm-kbd', () => {
  it('renders one <kbd> per key, modifiers first, in a dir="ltr" run', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const kbd = fixture.nativeElement.querySelector('tm-kbd') as HTMLElement;
    const caps = Array.from(kbd.querySelectorAll('kbd')).map((cap) => cap.textContent?.trim());
    expect(caps).toEqual(['Ctrl', 'Shift', 'K']);
    expect(kbd.getAttribute('dir')).toBe('ltr');
    expect(getComputedStyle(kbd).direction).toBe('ltr');
  });

  it('labels for the platform it is told, and follows changes', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const host = fixture.componentInstance;
    const caps = () =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('kbd')).map((cap) =>
        cap.textContent?.trim(),
      );

    host.apple.set(true);
    fixture.detectChanges();
    expect(caps()).toEqual(['⇧', '⌘', 'K']);

    host.keys.set('F2');
    fixture.detectChanges();
    expect(caps()).toEqual(['F2']);
  });
});

describe('@tellma/core-ui/kbd', () => {
  // The grammar has one home: kbd re-exports the keys entry point's own
  // functions rather than shipping a second copy, so an app that loads both
  // carries the parser, its cache and the platform check once.
  it('re-exports the keys grammar itself, not a copy', () => {
    expect(kbd.tmFormatKeys).toBe(keys.tmFormatKeys);
    expect(kbd.tmKeysText).toBe(keys.tmKeysText);
    expect(kbd.tmKeysMatch).toBe(keys.tmKeysMatch);
    expect(kbd.tmAriaKeyShortcuts).toBe(keys.tmAriaKeyShortcuts);
    expect(kbd.tmIsApplePlatform).toBe(keys.tmIsApplePlatform);
    expect(kbd.TmKbd).toBe(TmKbd);
  });
});
