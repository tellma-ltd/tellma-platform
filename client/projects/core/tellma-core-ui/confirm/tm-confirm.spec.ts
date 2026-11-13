// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmConfirmHarness } from '@tellma/core-ui-testing';

import { TmConfirm, type TmConfirmOptions } from './tm-confirm';

@Component({ template: '' })
class Host {}

async function setup() {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);
  return { fixture, loader, confirm: TestBed.inject(TmConfirm) };
}

/** The leave guard's question. */
const LEAVE: TmConfirmOptions<'discard' | 'stay'> = {
  title: 'Leave without saving?',
  message: 'This center has unsaved changes.',
  choices: [
    { key: 'discard', label: 'Discard changes', variant: 'ghost' },
    { key: 'stay', label: 'Stay', variant: 'primary' },
  ],
};

describe('TmConfirm.ask', () => {
  it('opens a small alert dialog described by its message', async () => {
    const { fixture, loader, confirm } = await setup();
    void confirm.ask(LEAVE);
    await fixture.whenStable();
    const dialog = await loader.getHarness(TmConfirmHarness);
    expect(await dialog.getTitle()).toBe('Leave without saving?');
    expect(await dialog.getMessage()).toBe('This center has unsaved changes.');
    expect(await dialog.getChoiceLabels()).toEqual(['Discard changes', 'Stay']);
    expect(await dialog.getIcon()).toBeNull();
    const container = document.querySelector('cdk-dialog-container')!;
    expect(container.getAttribute('role')).toBe('alertdialog');
    const message = document.querySelector('.tm-confirm__message')!;
    expect(container.getAttribute('aria-describedby')).toBe(message.id);
    expect(document.querySelector('.tm-modal-panel--sm')).not.toBeNull();
    await dialog.pressEscape();
  });

  it('resolves to the clicked choice', async () => {
    const { fixture, loader, confirm } = await setup();
    const answer = confirm.ask(LEAVE);
    await fixture.whenStable();
    await (await loader.getHarness(TmConfirmHarness)).choose('Discard changes');
    expect(await answer).toBe('discard');
    expect(document.querySelector('tm-confirm-content')).toBeNull();
  });

  it('resolves null on Escape', async () => {
    const { fixture, loader, confirm } = await setup();
    const answer = confirm.ask(LEAVE);
    await fixture.whenStable();
    await (await loader.getHarness(TmConfirmHarness)).pressEscape();
    expect(await answer).toBeNull();
  });

  it('resolves null on the close button', async () => {
    const { fixture, loader, confirm } = await setup();
    const answer = confirm.ask(LEAVE);
    await fixture.whenStable();
    await (await loader.getHarness(TmConfirmHarness)).close();
    expect(await answer).toBeNull();
  });

  it('resolves null on the backdrop', async () => {
    const { fixture, confirm } = await setup();
    const answer = confirm.ask(LEAVE);
    await fixture.whenStable();
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
    expect(await answer).toBeNull();
  });

  it('focuses the primary choice by default, else the first that is not danger', async () => {
    const { fixture, loader, confirm } = await setup();
    void confirm.ask(LEAVE);
    await fixture.whenStable();
    let dialog = await loader.getHarness(TmConfirmHarness);
    expect(await dialog.getFocusedChoiceLabel()).toBe('Stay');
    await dialog.pressEscape();

    void confirm.ask({
      title: 'Remove the line?',
      message: 'The line goes.',
      choices: [
        { key: 'remove', label: 'Remove', variant: 'danger' },
        { key: 'keep', label: 'Keep', variant: 'secondary' },
      ],
    });
    await fixture.whenStable();
    dialog = await loader.getHarness(TmConfirmHarness);
    expect(await dialog.getFocusedChoiceLabel()).toBe('Keep');
    // A danger choice brings the warning glyph by default.
    expect(await dialog.getIcon()).toBe('warning');
    await dialog.pressEscape();
  });

  it('honours initialFocus and an explicit icon', async () => {
    const { fixture, loader, confirm } = await setup();
    void confirm.ask({ ...LEAVE, initialFocus: 'discard', icon: 'info' });
    await fixture.whenStable();
    const dialog = await loader.getHarness(TmConfirmHarness);
    expect(await dialog.getFocusedChoiceLabel()).toBe('Discard changes');
    expect(await dialog.getIcon()).toBe('info');
    await dialog.pressEscape();
  });
});

describe('TmConfirm.confirm', () => {
  it('asks OK or Cancel, focuses OK, and resolves true on OK', async () => {
    const { fixture, loader, confirm } = await setup();
    const answer = confirm.confirm({ title: 'Post the document?', message: 'It will be final.' });
    await fixture.whenStable();
    const dialog = await loader.getHarness(TmConfirmHarness);
    expect(await dialog.getChoiceLabels()).toEqual(['Cancel', 'OK']);
    expect(await dialog.getFocusedChoiceLabel()).toBe('OK');
    expect(await dialog.getIcon()).toBeNull();
    await dialog.choose('OK');
    expect(await answer).toBe(true);
  });

  it('a destructive confirm is danger with the warning glyph and focus on Cancel', async () => {
    const { fixture, loader, confirm } = await setup();
    const answer = confirm.confirm({
      title: 'Delete this center?',
      message: 'This cannot be undone.',
      confirmLabel: 'Delete',
      cancelLabel: 'Keep',
      destructive: true,
    });
    await fixture.whenStable();
    const dialog = await loader.getHarness(TmConfirmHarness);
    expect(await dialog.getChoiceLabels()).toEqual(['Keep', 'Delete']);
    expect(await dialog.getFocusedChoiceLabel()).toBe('Keep');
    expect(await dialog.getIcon()).toBe('warning');
    expect(
      document.querySelector('.tm-confirm__choice.tm-button--danger')?.textContent?.trim(),
    ).toBe('Delete');
    await dialog.choose('Keep');
    expect(await answer).toBe(false);
  });

  it('resolves false when dismissed', async () => {
    const { fixture, loader, confirm } = await setup();
    const answer = confirm.confirm({ title: 'Post?', message: 'Final.' });
    await fixture.whenStable();
    await (await loader.getHarness(TmConfirmHarness)).pressEscape();
    expect(await answer).toBe(false);
  });
});
