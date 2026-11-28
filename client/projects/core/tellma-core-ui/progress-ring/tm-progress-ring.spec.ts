// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { TmProgressRing } from './tm-progress-ring';

@Component({
  imports: [TmProgressRing],
  template: `
    <tm-progress-ring
      [value]="value()"
      [diameter]="36"
      [decorative]="decorative()"
      aria-label="Export progress"
    />
  `,
})
class Host {
  readonly value = signal<number | null>(null);
  readonly decorative = signal(true);
}

function setup() {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const element = (fixture.nativeElement as HTMLElement).querySelector(
    'tm-progress-ring',
  ) as HTMLElement;
  return { fixture, host: fixture.componentInstance, element };
}

describe('tm-progress-ring', () => {
  it('is decorative by default, sized by its diameter', () => {
    const { element } = setup();
    expect(element.getAttribute('aria-hidden')).toBe('true');
    expect(element.hasAttribute('role')).toBe(false);
    expect(element.style.inlineSize).toBe('36px');
    expect(element.style.blockSize).toBe('36px');
  });

  it('spins a quarter arc for null and draws the share for a number', () => {
    const { fixture, host, element } = setup();
    const arc = element.querySelector('.tm-progress-ring__arc')!;
    expect(arc.classList).toContain('tm-progress-ring__arc--spin');
    expect(arc.getAttribute('stroke-dasharray')).toBe('25 100');
    host.value.set(60);
    fixture.detectChanges();
    expect(arc.classList).not.toContain('tm-progress-ring__arc--spin');
    expect(arc.getAttribute('stroke-dasharray')).toBe('60 100');
    host.value.set(0);
    fixture.detectChanges();
    expect(arc.classList).toContain('tm-progress-ring__arc--empty');
  });

  it('is a progress bar when not decorative', () => {
    const { fixture, host, element } = setup();
    host.decorative.set(false);
    host.value.set(60);
    fixture.detectChanges();
    expect(element.hasAttribute('aria-hidden')).toBe(false);
    expect(element.getAttribute('role')).toBe('progressbar');
    expect(element.getAttribute('aria-valuenow')).toBe('60');
    expect(element.getAttribute('aria-valuemin')).toBe('0');
    expect(element.getAttribute('aria-valuemax')).toBe('100');
  });
});
