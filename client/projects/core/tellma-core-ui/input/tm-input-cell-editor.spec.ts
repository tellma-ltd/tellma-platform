// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { TmCellEditor, TmCellEditorHost } from '@tellma/core-ui/contracts';
import { provideTellmaUi, TM_CELL_EDITOR_HOST } from '@tellma/core-ui';

import { TmInput } from './tm-input';

/** Records what a grid cell would receive through TM_CELL_EDITOR_HOST. */
class RecordingCellHost implements TmCellEditorHost {
  editor: TmCellEditor<unknown> | null = null;
  register(editor: TmCellEditor<unknown>): void {
    this.editor = editor;
  }
}

@Component({
  imports: [TmInput],
  template: `<input tmInput />`,
})
class Host {}

describe('tmInput as TmCellEditor (§6.3)', () => {
  async function setup() {
    const cellHost = new RecordingCellHost();
    TestBed.configureTestingModule({
      providers: [provideTellmaUi(), { provide: TM_CELL_EDITOR_HOST, useValue: cellHost }],
    });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    const editor = cellHost.editor as TmCellEditor<string>;
    return { fixture, cellHost, editor, element };
  }

  function type(element: HTMLInputElement, text: string): void {
    element.focus();
    element.value = text;
    element.dispatchEvent(new Event('input', { bubbles: true }));
  }

  it('registers itself with the provided TM_CELL_EDITOR_HOST on construction', async () => {
    const { cellHost } = await setup();
    expect(cellHost.editor).not.toBeNull();
    expect(cellHost.editor).toBeInstanceOf(TmInput);
  });

  it('text mirrors the value and is never null for a plain text input', async () => {
    const { fixture, editor, element } = await setup();
    expect(editor.text()).toBe('');
    type(element, 'hello');
    await fixture.whenStable();
    expect(editor.text()).toBe('hello');
  });

  it('seed replaces the content, places the caret at the end, and does not move the revert baseline', async () => {
    const { fixture, editor, element } = await setup();
    // The grid loads the pre-open value through the value channel (external).
    editor.value.set('original');
    await fixture.whenStable();

    editor.seed!('ab');
    await fixture.whenStable();
    expect(element.value).toBe('ab');
    expect(element.selectionStart).toBe(2);
    expect(element.selectionEnd).toBe(2);

    editor.cancel();
    await fixture.whenStable();
    expect(editor.value()).toBe('original');
    expect(element.value).toBe('original');
  });

  it('typing never moves the baseline; cancel restores the value present at open', async () => {
    const { fixture, editor, element } = await setup();
    editor.value.set('open-value');
    await fixture.whenStable();

    type(element, 'user typing');
    await fixture.whenStable();
    expect(editor.value()).toBe('user typing');

    editor.cancel();
    await fixture.whenStable();
    expect(editor.value()).toBe('open-value');
  });

  it('commit moves the baseline so a later cancel returns to the committed value', async () => {
    const { fixture, editor, element } = await setup();
    editor.value.set('open-value');
    await fixture.whenStable();

    type(element, 'committed');
    await fixture.whenStable();
    editor.commit();

    type(element, 'discarded');
    await fixture.whenStable();
    editor.cancel();
    await fixture.whenStable();
    expect(editor.value()).toBe('committed');
  });

  it('registration is optional: a bare input outside any grid works without the token', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(element).toBeTruthy();
  });
});
