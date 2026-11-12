// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmErrorDialogHarness } from '@tellma/core-ui-testing';

import { TmErrorDialog, type TmErrorDialogResult } from './tm-error-dialog';

@Component({ template: '' })
class Host {}

/** The unit page loads no token stylesheet; the button widths need one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

async function setup() {
  const errors: unknown[] = [];
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: ErrorHandler, useValue: { handleError: (error: unknown) => errors.push(error) } },
    ],
  });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);
  return { fixture, loader, errors, dialog: TestBed.inject(TmErrorDialog) };
}

/** A promise the test settles by hand. */
function deferred<T>(): {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (error: unknown) => void;
} {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** Snapshots a promise's settlement without awaiting it. */
function track<T>(promise: Promise<T>): () => T | 'pending' {
  let outcome: T | 'pending' = 'pending';
  void promise.then((value) => (outcome = value));
  return () => outcome;
}

/** Lets settled promises run their callbacks. */
const flush = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('TmErrorDialog', () => {
  it('lists the message, the first ten items and a count of the rest, and the reference', async () => {
    const { fixture, loader, dialog } = await setup();
    const items = Array.from({ length: 13 }, (_, index) => `Line ${index + 1}`);
    void dialog.show({
      title: 'Could not save',
      message: 'Some lines are invalid.',
      items,
      reference: 'abc-123',
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    expect(await harness.getTitle()).toBe('Could not save');
    expect(await harness.getMessage()).toBe('Some lines are invalid.');
    expect(await harness.getItems()).toEqual(items.slice(0, 10));
    expect(await harness.getMoreText()).toBe('and 3 more');
    expect(await harness.hasReference()).toBe(true);
    expect(await harness.hasDetails()).toBe(false);
    const container = document.querySelector('cdk-dialog-container')!;
    expect(container.getAttribute('role')).toBe('alertdialog');
    expect(container.getAttribute('aria-describedby')).toBe(
      document.querySelector('.tm-error-dialog__message')!.id,
    );
    await harness.pressEscape();
  });

  it('lists ten items without a count', async () => {
    const { fixture, loader, dialog } = await setup();
    void dialog.show({
      title: 'T',
      message: 'M',
      items: Array.from({ length: 10 }, (_, i) => `${i}`),
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    expect((await harness.getItems()).length).toBe(10);
    expect(await harness.getMoreText()).toBeNull();
    await harness.pressEscape();
  });

  it('keeps the details and actions in a closed disclosure', async () => {
    const { fixture, loader, dialog } = await setup();
    const copied: string[] = [];
    const result = dialog.show({
      title: 'Something went wrong',
      message: 'The server failed.',
      details: [
        { label: 'Status', value: '500' },
        { label: 'UI version', value: '2.14.0' },
      ],
      actions: [
        { label: 'Copy diagnostics', run: () => copied.push('copy') },
        { label: 'Report', run: () => copied.push('report'), closes: true },
      ],
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    expect(await harness.hasDetails()).toBe(true);
    expect(await harness.isDetailsOpen()).toBe(false);
    expect(document.querySelector('.tm-error-dialog__region')).toBeNull();

    await harness.toggleDetails();
    expect(await harness.isDetailsOpen()).toBe(true);
    expect(await harness.getDetails()).toEqual([
      { label: 'Status', value: '500' },
      { label: 'UI version', value: '2.14.0' },
    ]);
    expect(await harness.getActionLabels()).toEqual(['Copy diagnostics', 'Report']);

    // An action runs and the dialog stays, unless it closes.
    await harness.runAction('Copy diagnostics');
    expect(copied).toEqual(['copy']);
    expect(document.querySelector('tm-error-dialog-content')).not.toBeNull();
    await harness.runAction('Report');
    expect(copied).toEqual(['copy', 'report']);
    expect(await result).toBe('close');
  });

  it('puts primary, secondary and Close in the footer, focusing the first', async () => {
    const { fixture, loader, dialog } = await setup();
    void dialog.show({
      title: 'T',
      message: 'M',
      primary: { label: 'Try again' },
      secondary: { label: 'Sign in' },
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    const buttons = await harness.getButtons();
    expect(buttons.map((button) => [button.key, button.label])).toEqual([
      ['primary', 'Try again'],
      ['secondary', 'Sign in'],
      ['close', 'Close'],
    ]);
    const close = document.querySelector('.tm-error-dialog__button[data-key="close"]')!;
    expect(close.classList.contains('tm-button--secondary')).toBe(true);
    expect(await harness.getFocusedButtonKey()).toBe('primary');
    await harness.pressEscape();
  });

  it('makes Close the primary button when no primary is given', async () => {
    const { fixture, loader, dialog } = await setup();
    const result = dialog.show({ title: 'T', message: 'M' });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    const close = document.querySelector('.tm-error-dialog__button[data-key="close"]')!;
    expect(close.classList.contains('tm-button--primary')).toBe(true);
    expect(await harness.getFocusedButtonKey()).toBe('close');
    await harness.clickButton('close');
    expect(await result).toBe('close');
  });

  it('resolves a button without run on its click; Escape and the close button resolve close', async () => {
    const { fixture, loader, dialog } = await setup();
    let result = dialog.show({ title: 'T', message: 'M', primary: { label: 'Try again' } });
    await fixture.whenStable();
    await (await loader.getHarness(TmErrorDialogHarness)).clickButton('primary');
    expect(await result).toBe('primary');

    result = dialog.show({ title: 'T', message: 'M', primary: { label: 'Try again' } });
    await fixture.whenStable();
    await (await loader.getHarness(TmErrorDialogHarness)).pressEscape();
    expect(await result).toBe('close');

    result = dialog.show({ title: 'T', message: 'M' });
    await fixture.whenStable();
    (document.querySelector('.tm-modal__close') as HTMLElement).click();
    expect(await result).toBe('close');
  });

  it('ignores the backdrop', async () => {
    const { fixture, loader, dialog } = await setup();
    const result = track(dialog.show({ title: 'T', message: 'M' }));
    await fixture.whenStable();
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
    await flush();
    expect(result()).toBe('pending');
    await (await loader.getHarness(TmErrorDialogHarness)).pressEscape();
  });

  // The button's width while pending rests on the global button stylesheet,
  // which this page does not load; the browser suite measures it.
  it('turns a run button pending in the click, named by pendingLabel', async () => {
    const { fixture, loader, dialog } = await setup();
    const run = deferred<boolean>();
    const result = track(
      dialog.show({
        title: 'T',
        message: 'M',
        primary: {
          label: 'Reload',
          pendingLabel: 'Reloading the application',
          run: () => run.promise,
        },
        secondary: { label: 'Later', run: () => Promise.resolve(true) },
      }),
    );
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    const button = document.querySelector<HTMLElement>(
      '.tm-error-dialog__button[data-key="primary"]',
    )!;

    // Pending in the click's own frame: the click handler renders before
    // the browser paints.
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-busy')).toBe('true');
    const [primary, secondary, close] = await harness.getButtons();
    expect(primary).toMatchObject({ pending: true, ariaLabel: 'Reloading the application' });
    expect(secondary).toMatchObject({ pending: false, disabled: true });
    expect(close).toMatchObject({ pending: false, disabled: false });

    // false keeps the dialog and returns the button to idle.
    run.resolve(false);
    await flush();
    await fixture.whenStable();
    expect(result()).toBe('pending');
    expect((await harness.getButtons())[0]).toMatchObject({ pending: false, ariaLabel: null });
    await harness.pressEscape();
  });

  it('reserves the wider label: the pending name can be the longer of the two', async () => {
    const { fixture, dialog } = await setup();
    void dialog.show({
      title: 'T',
      message: 'M',
      primary: { label: 'Go', pendingLabel: 'Reloading the application' },
    });
    await fixture.whenStable();
    const reserve = document.querySelector<HTMLElement>('.tm-error-dialog__label-reserve')!;
    const label = document.querySelector<HTMLElement>('.tm-error-dialog__label')!;
    expect(label.getBoundingClientRect().width).toBe(reserve.getBoundingClientRect().width);
    expect(reserve.getAttribute('aria-hidden')).toBe('true');
    (document.querySelector('.tm-modal__close') as HTMLElement).click();
  });

  it('closes on a run that resolves true', async () => {
    const { fixture, loader, dialog } = await setup();
    const result = dialog.show({
      title: 'T',
      message: 'M',
      primary: { label: 'Retry', run: () => Promise.resolve(true) },
    });
    await fixture.whenStable();
    await (await loader.getHarness(TmErrorDialogHarness)).clickButton('primary');
    expect(await result).toBe('primary');
  });

  it('keeps Escape live during a run and ignores the late result', async () => {
    const { fixture, loader, dialog } = await setup();
    const run = deferred<boolean>();
    const result = dialog.show({
      title: 'T',
      message: 'M',
      primary: { label: 'Retry', run: () => run.promise },
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    await harness.clickButton('primary');
    await harness.pressEscape();
    expect(await result).toBe('close');
    run.resolve(true);
    await flush();
    expect(document.querySelector('tm-error-dialog-content')).toBeNull();
  });

  it('returns a failed run to idle and reports the error', async () => {
    const { fixture, loader, dialog, errors } = await setup();
    const failure = new Error('network');
    void dialog.show({
      title: 'T',
      message: 'M',
      primary: { label: 'Retry', run: () => Promise.reject(failure) },
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    await harness.clickButton('primary');
    await flush();
    await fixture.whenStable();
    expect(errors).toEqual([failure]);
    expect((await harness.getButtons())[0].pending).toBe(false);
    await harness.pressEscape();
  });

  it('replaces an open dialog in place: the earlier promise resolves replaced', async () => {
    const { fixture, loader, dialog } = await setup();
    const first = dialog.show({
      title: 'First',
      message: 'One',
      primary: { label: 'A' },
      secondary: { label: 'B' },
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    // Focus the second footer position.
    (
      document.querySelector('.tm-error-dialog__button[data-key="secondary"]') as HTMLElement
    ).focus();

    const second = track(
      dialog.show({
        title: 'Second',
        message: 'Two',
        primary: { label: 'C' },
        secondary: { label: 'D' },
      }),
    );
    await fixture.whenStable();
    expect(await first).toBe('replaced' satisfies TmErrorDialogResult);
    expect(document.querySelectorAll('tm-error-dialog-content').length).toBe(1);
    expect(await harness.getTitle()).toBe('Second');
    expect(await harness.getMessage()).toBe('Two');
    expect((await harness.getButtons()).map((button) => button.label)).toEqual(['C', 'D', 'Close']);
    expect(await harness.getFocusedButtonKey()).toBe('secondary');
    expect(second()).toBe('pending');
    await harness.clickButton('primary');
    await flush();
    expect(second()).toBe('primary');
  });

  it('keeps focus on the last footer position when a replacement shortens the footer', async () => {
    const { fixture, loader, dialog } = await setup();
    void dialog.show({
      title: 'T',
      message: 'M',
      primary: { label: 'A' },
      secondary: { label: 'B' },
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    (document.querySelector('.tm-error-dialog__button[data-key="close"]') as HTMLElement).focus();
    void dialog.show({ title: 'T2', message: 'M2' });
    await fixture.whenStable();
    expect(await harness.getFocusedButtonKey()).toBe('close');
    await harness.pressEscape();
  });

  it('closes the Details disclosure on replacement', async () => {
    const { fixture, loader, dialog } = await setup();
    void dialog.show({ title: 'T', message: 'M', details: [{ label: 'Status', value: '500' }] });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    await harness.toggleDetails();
    expect(await harness.isDetailsOpen()).toBe(true);
    void dialog.show({ title: 'T', message: 'M', details: [{ label: 'Status', value: '503' }] });
    await fixture.whenStable();
    expect(await harness.isDetailsOpen()).toBe(false);
    await harness.pressEscape();
  });

  it('a replaced dialog ignores a late result from the earlier run', async () => {
    const { fixture, loader, dialog } = await setup();
    const run = deferred<boolean>();
    const first = dialog.show({
      title: 'T',
      message: 'M',
      primary: { label: 'Retry', run: () => run.promise },
    });
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    await harness.clickButton('primary');
    expect((await harness.getButtons())[0].pending).toBe(true);

    const second = track(dialog.show({ title: 'T2', message: 'M2', primary: { label: 'Retry' } }));
    await fixture.whenStable();
    expect(await first).toBe('replaced');
    // The replacement drops the pending state.
    expect((await harness.getButtons())[0].pending).toBe(false);

    run.resolve(true);
    await flush();
    await fixture.whenStable();
    expect(second()).toBe('pending');
    expect(document.querySelector('tm-error-dialog-content')).not.toBeNull();
    await harness.pressEscape();
    await flush();
    expect(second()).toBe('close');
  });

  it('opens a new dialog for a show right after a footer click closed the last one', async () => {
    const { fixture, loader, dialog } = await setup();
    const first = dialog.show({ title: 'T', message: 'M', primary: { label: 'Go' } });
    await fixture.whenStable();
    (document.querySelector('.tm-error-dialog__button[data-key="primary"]') as HTMLElement).click();
    const second = track(dialog.show({ title: 'Next', message: 'M2' }));
    expect(await first).toBe('primary');
    await fixture.whenStable();
    const harness = await loader.getHarness(TmErrorDialogHarness);
    expect(await harness.getTitle()).toBe('Next');
    await harness.pressEscape();
    await flush();
    expect(second()).toBe('close');
  });
});
