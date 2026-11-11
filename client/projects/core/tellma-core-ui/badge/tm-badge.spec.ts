// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CircleAlert } from 'lucide';

import { provideTellmaUi } from '@tellma/core-ui';
import { provideTellmaIcons } from '@tellma/core-ui/icon';

import { TmBadge, type TmBadgeTone } from './tm-badge';

@Component({
  imports: [TmBadge],
  template: `<tm-badge [tone]="tone()" [dot]="dot()" [icon]="icon()">Held</tm-badge>`,
})
class Host {
  readonly tone = signal<TmBadgeTone>('neutral');
  readonly dot = signal(false);
  readonly icon = signal<string | undefined>(undefined);
}

function setup() {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), provideTellmaIcons({ 'circle-alert': CircleAlert })],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const element = fixture.nativeElement.querySelector('tm-badge') as HTMLElement;
  return { fixture, host: fixture.componentInstance, element };
}

describe('tm-badge', () => {
  it('renders the projected text with no role, neutral by default', () => {
    const { element } = setup();
    expect(element.textContent?.trim()).toBe('Held');
    expect(element.getAttribute('role')).toBeNull();
    expect(element.classList).toContain('tm-badge--neutral');
  });

  it('carries exactly one tone class, following the input', () => {
    const { fixture, host, element } = setup();
    for (const tone of ['accent', 'info', 'success', 'warning', 'error', 'neutral'] as const) {
      host.tone.set(tone);
      fixture.detectChanges();
      const tones = [...element.classList].filter((name) => /^tm-badge--/.test(name));
      expect(tones).toEqual([`tm-badge--${tone}`]);
    }
  });

  it('draws a decorative leading dot and a leading icon only when asked', () => {
    const { fixture, host, element } = setup();
    expect(element.querySelector('.tm-badge__dot')).toBeNull();
    expect(element.querySelector('tm-icon')).toBeNull();

    host.dot.set(true);
    host.icon.set('circle-alert');
    fixture.detectChanges();
    const dot = element.querySelector('.tm-badge__dot');
    expect(dot?.getAttribute('aria-hidden')).toBe('true');
    const icon = element.querySelector('tm-icon');
    expect(icon?.getAttribute('aria-hidden')).toBe('true');
    expect(icon?.querySelector('svg')).not.toBeNull();
    // The marks lead: dot, icon, then the label.
    const order = ['tm-badge__dot', 'tm-badge__icon', 'tm-badge__label'];
    expect(
      [...element.children].map((child) => order.find((name) => child.classList.contains(name))),
    ).toEqual(order);
  });
});
