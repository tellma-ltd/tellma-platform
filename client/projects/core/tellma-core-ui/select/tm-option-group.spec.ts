// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { TmOption } from './tm-option';
import { TmOptionGroup } from './tm-option-group';
import { TmSelect } from './tm-select';

describe('tm-option-group', () => {
  it('refuses a group inside a group in development, naming the one-level rule', () => {
    @Component({
      imports: [TmSelect, TmOption, TmOptionGroup],
      template: `
        <tm-select aria-label="Nested">
          <tm-option-group label="Outer">
            <tm-option-group label="Inner">
              <tm-option [value]="1" label="One">One</tm-option>
            </tm-option-group>
          </tm-option-group>
        </tm-select>
      `,
    })
    class NestedHost {}

    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    expect(() => TestBed.createComponent(NestedHost)).toThrowError(
      /a group cannot sit inside another group/,
    );
  });

  it('gives every group its own heading, which names it', async () => {
    @Component({
      imports: [TmSelect, TmOption, TmOptionGroup],
      template: `
        <tm-select aria-label="Two groups">
          <tm-option-group label="A"
            ><tm-option [value]="1" label="One">One</tm-option></tm-option-group
          >
          <tm-option-group label="B"
            ><tm-option [value]="2" label="Two">Two</tm-option></tm-option-group
          >
        </tm-select>
      `,
    })
    class TwoGroupsHost {}

    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(TwoGroupsHost);
    await fixture.whenStable();
    const trigger = fixture.nativeElement.querySelector('.tm-select__trigger') as HTMLElement;
    trigger.focus();
    trigger.click();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();

    const groups = [...document.querySelectorAll('[role="group"]')];
    const ids = groups.map((group) => group.getAttribute('aria-labelledby'));
    expect(new Set(ids).size).toBe(2);
    expect(ids.map((id) => document.getElementById(id!)?.textContent?.trim())).toEqual(['A', 'B']);
  });
});
