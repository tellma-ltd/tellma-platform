// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

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
