// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { TmSkeleton, type TmSkeletonShape } from './tm-skeleton';

@Component({
  imports: [TmSkeleton],
  template: `
    <tm-skeleton
      [shape]="shape()"
      [width]="width()"
      [height]="height()"
      [lines]="lines()"
      [columns]="columns()"
    />
  `,
})
class Host {
  readonly shape = signal<TmSkeletonShape>('line');
  readonly width = signal('100%');
  readonly height = signal<string | undefined>(undefined);
  readonly lines = signal<number | undefined>(undefined);
  readonly columns = signal(4);
}

function setup() {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const element = (fixture.nativeElement as HTMLElement).querySelector(
    'tm-skeleton',
  ) as HTMLElement;
  return { fixture, host: fixture.componentInstance, element };
}

describe('tm-skeleton', () => {
  it('is hidden from assistive technology and draws one line by default', () => {
    const { element } = setup();
    expect(element.getAttribute('aria-hidden')).toBe('true');
    expect(element.getAttribute('data-shape')).toBe('line');
    expect(element.querySelectorAll('.tm-skeleton__bar').length).toBe(1);
    expect(element.style.inlineSize).toBe('100%');
  });

  it('draws as many line bars as asked', () => {
    const { fixture, host, element } = setup();
    host.lines.set(3);
    fixture.detectChanges();
    expect(element.querySelectorAll('.tm-skeleton__bar').length).toBe(3);
  });

  it('binds the width and height on a block and a circle', () => {
    const { fixture, host, element } = setup();
    host.shape.set('block');
    host.width.set('240px');
    host.height.set('120px');
    fixture.detectChanges();
    expect(element.getAttribute('data-shape')).toBe('block');
    expect(element.style.inlineSize).toBe('240px');
    expect(element.style.blockSize).toBe('120px');
    expect(element.children.length).toBe(0);

    host.shape.set('circle');
    host.width.set('24px');
    host.height.set(undefined);
    fixture.detectChanges();
    expect(element.getAttribute('data-shape')).toBe('circle');
    expect(element.style.blockSize).toBe('');
  });

  it('draws a table as a header row and eight body rows of four cells by default', () => {
    const { fixture, host, element } = setup();
    host.shape.set('table');
    fixture.detectChanges();
    const rows = element.querySelectorAll('.tm-skeleton__row');
    expect(rows.length).toBe(9);
    expect(rows[0].classList).toContain('tm-skeleton__row--header');
    for (const row of Array.from(rows)) {
      expect(row.querySelectorAll('.tm-skeleton__bar').length).toBe(4);
    }
    expect(element.style.getPropertyValue('--tm-skeleton-columns')).toBe('4');
    // The rows take the grid's row height for the workspace's size step.
    expect(element.getAttribute('data-size')).toBe('sm');
  });

  it('draws a table with the rows and columns asked for', () => {
    const { fixture, host, element } = setup();
    host.shape.set('table');
    host.lines.set(3);
    host.columns.set(6);
    fixture.detectChanges();
    const rows = element.querySelectorAll('.tm-skeleton__row');
    expect(rows.length).toBe(4);
    expect(rows[1].querySelectorAll('.tm-skeleton__bar').length).toBe(6);
  });
});
