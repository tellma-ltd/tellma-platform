// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  TmDescriptionList,
  type TmDescriptionListColumns,
  type TmDescriptionListLayout,
} from './tm-description-list';

@Component({
  imports: [TmDescriptionList],
  template: `
    <dl tmDescriptionList [columns]="columns()" [layout]="layout()">
      <div>
        <dt>Code</dt>
        <dd>JED-01</dd>
      </div>
      <div>
        <dt>Manager</dt>
        <dd>Sara</dd>
      </div>
    </dl>
    <dl tmDescriptionList class="plain">
      <div>
        <dt>Code</dt>
        <dd>X</dd>
      </div>
    </dl>
  `,
})
class Host {
  readonly columns = signal<TmDescriptionListColumns>(2);
  readonly layout = signal<TmDescriptionListLayout>('inline');
}

describe('dl[tmDescriptionList]', () => {
  it('marks the list and states its columns and layout for the stylesheet', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    const [list, plain] = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('dl'));
    expect(list.classList).toContain('tm-description-list');
    expect(list.getAttribute('data-columns')).toBe('2');
    expect(list.getAttribute('data-layout')).toBe('inline');
    // The defaults: one column, stacked.
    expect(plain.getAttribute('data-columns')).toBe('1');
    expect(plain.getAttribute('data-layout')).toBe('stacked');

    fixture.componentInstance.columns.set('auto');
    fixture.componentInstance.layout.set('stacked');
    await fixture.whenStable();
    expect(list.getAttribute('data-columns')).toBe('auto');
    expect(list.getAttribute('data-layout')).toBe('stacked');
    // No role is imposed: the native list semantics stand.
    expect(list.getAttribute('role')).toBeNull();
  });
});
