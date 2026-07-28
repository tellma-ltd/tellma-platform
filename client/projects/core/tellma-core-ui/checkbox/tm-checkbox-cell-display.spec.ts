// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { TmCellEditor, TmCellEditorHost } from '@tellma/core-ui/contracts';
import { provideTellmaUi, TM_CELL_EDITOR_HOST } from '@tellma/core-ui';

import { TM_CHECKBOX_CELL_DISPLAY, TmCheckbox } from './tm-checkbox';

describe('TM_CHECKBOX_CELL_DISPLAY (§6.3)', () => {
  it('formats as the spreadsheet-interop TRUE/FALSE literals, never localized', () => {
    expect(TM_CHECKBOX_CELL_DISPLAY.formatValue(true, 'en')).toBe('TRUE');
    expect(TM_CHECKBOX_CELL_DISPLAY.formatValue(false, 'en')).toBe('FALSE');
    expect(TM_CHECKBOX_CELL_DISPLAY.formatValue(true, 'ar')).toBe('TRUE');
  });

  it('a cleared (null) cell displays as unchecked FALSE', () => {
    expect(TM_CHECKBOX_CELL_DISPLAY.formatValue(null, 'en')).toBe('FALSE');
    expect(TM_CHECKBOX_CELL_DISPLAY.displayClass!(null)).toBe('tm-grid-bool');
  });

  it('exposes the token-driven glyph classes for the two visual states', () => {
    expect(TM_CHECKBOX_CELL_DISPLAY.displayClass!(true)).toBe('tm-grid-bool tm-grid-bool--on');
    expect(TM_CHECKBOX_CELL_DISPLAY.displayClass!(false)).toBe('tm-grid-bool');
  });

  it('tm-checkbox does NOT register as a cell editor — boolean cells toggle directly', async () => {
    // Grid boolean cells never mount an editor; the checkbox deliberately
    // has no `value` channel, so it stays out of the editor registry even
    // when a host token is present.
    const registered: TmCellEditor<unknown>[] = [];
    const cellHost: TmCellEditorHost = {
      register(editor) {
        registered.push(editor);
      },
    };
    @Component({
      imports: [TmCheckbox],
      template: `<tm-checkbox>Posted</tm-checkbox>`,
    })
    class Host {}
    TestBed.configureTestingModule({
      providers: [provideTellmaUi(), { provide: TM_CELL_EDITOR_HOST, useValue: cellHost }],
    });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    expect(registered).toEqual([]);
  });
});
