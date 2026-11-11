// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmKeysText } from '@tellma/core-ui/kbd';
import { TmCopyButtonHarness, TmReferenceHarness } from '@tellma/core-ui-testing';

import { TmCopyButton } from './tm-copy-button';
import { TmReference } from './tm-reference';

@Component({
  imports: [TmCopyButton, TmReference],
  template: `
    <tm-copy-button [text]="text()" [showLabel]="showLabel()" (copied)="copies = copies + 1" />
    <tm-reference value="4bf92f3577b34da6a3ce929d0e0e4736" />
  `,
})
class Host {
  readonly text = signal<string | (() => string | Promise<string>)>('INV-0042');
  readonly showLabel = signal(false);
  copies = 0;
}

async function setup() {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  // The first copy button is the standalone one; the second is the reference's.
  const [button] = await loader.getAllHarnesses(TmCopyButtonHarness);
  const reference = await loader.getHarness(TmReferenceHarness);
  return { fixture, host: fixture.componentInstance, button, reference };
}

/** Lets pending clipboard promises settle and re-renders. */
async function settle(fixture: ComponentFixture<Host>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  fixture.detectChanges();
  await fixture.whenStable();
}

describe('tm-copy-button', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('is an icon-only button named "Copy", with nothing announced', async () => {
    const { button } = await setup();
    expect(await button.getLabel()).toBe('Copy');
    expect(await button.isCopied()).toBe(false);
    expect(await button.getAnnouncement()).toBe('');
  });

  it('writes the text in the click, then says "Copied" for two seconds', async () => {
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    const { fixture, host, button } = await setup();
    await button.click();
    expect(writeText).toHaveBeenCalledWith('INV-0042');
    await settle(fixture);
    expect(await button.isCopied()).toBe(true);
    expect(await button.getAnnouncement()).toBe('Copied');
    expect(host.copies).toBe(1);
    // The accessible name stays put; the label and the region say "Copied".
    expect(await button.getLabel()).toBe('Copy');

    await new Promise((resolve) => setTimeout(resolve, 2050));
    fixture.detectChanges();
    expect(await button.isCopied()).toBe(false);
    expect(await button.getAnnouncement()).toBe('');
  });

  it('keeps both labels in the box when the label shows', async () => {
    vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    const { fixture, host, button } = await setup();
    host.showLabel.set(true);
    fixture.detectChanges();
    expect(await button.getLabel()).toBe('Copy');
    const element = fixture.nativeElement.querySelector('tm-copy-button') as HTMLElement;
    expect(element.querySelectorAll('.tm-copy-button__label').length).toBe(2);
    await button.click();
    await settle(fixture);
    expect(await button.getLabel()).toBe('Copied');
  });

  it('hands a builder promise to the clipboard as a pending item', async () => {
    const write = vi.spyOn(navigator.clipboard, 'write').mockResolvedValue();
    const { fixture, host, button } = await setup();
    host.text.set(() => Promise.resolve('built later'));
    fixture.detectChanges();
    await button.click();
    expect(write).toHaveBeenCalledTimes(1);
    const [item] = write.mock.calls[0][0];
    const blob = await item.getType('text/plain');
    expect(await blob.text()).toBe('built later');
    await settle(fixture);
    expect(await button.isCopied()).toBe(true);
  });

  it('writes a builder string at once', async () => {
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    const { fixture, host, button } = await setup();
    host.text.set(() => 'built now');
    fixture.detectChanges();
    await button.click();
    expect(writeText).toHaveBeenCalledWith('built now');
  });

  it('falls back to a selected, read-only input when the clipboard refuses', async () => {
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new DOMException('denied'));
    const { fixture, host, button } = await setup();
    await button.click();
    await settle(fixture);
    expect(await button.isShowingManualCopy()).toBe(true);
    expect(await button.getManualText()).toBe('INV-0042');
    expect(await button.getManualPrompt()).toBe(`Press ${tmKeysText('Mod+C')} to copy`);
    const input = fixture.nativeElement.querySelector('.tm-copy-button__manual') as HTMLInputElement;
    expect(document.activeElement).toBe(input);
    expect(input.readOnly).toBe(true);
    expect(input.selectionStart).toBe(0);
    expect(input.selectionEnd).toBe('INV-0042'.length);
    expect(host.copies).toBe(0);

    // A copy from there is a success, and focus goes back to the button.
    input.dispatchEvent(new ClipboardEvent('copy', { bubbles: true }));
    await settle(fixture);
    expect(await button.isCopied()).toBe(true);
    expect(host.copies).toBe(1);
    expect(await button.isShowingManualCopy()).toBe(false);
    expect(document.activeElement).toBe(
      fixture.nativeElement.querySelector('.tm-copy-button__button'),
    );
  });

  it('takes the fallback down on Escape, returning focus to the button', async () => {
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new DOMException('denied'));
    const { fixture, button } = await setup();
    await button.click();
    await settle(fixture);
    const input = fixture.nativeElement.querySelector('.tm-copy-button__manual') as HTMLInputElement;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(await button.isShowingManualCopy()).toBe(false);
    expect(await button.isCopied()).toBe(false);
    expect(document.activeElement).toBe(
      fixture.nativeElement.querySelector('.tm-copy-button__button'),
    );
  });
});

describe('tm-reference', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the label and the full value in a left-to-right chip', async () => {
    const { fixture, reference } = await setup();
    expect(await reference.getLabel()).toBe('Reference');
    expect(await reference.getValue()).toBe('4bf92f3577b34da6a3ce929d0e0e4736');
    const chip = fixture.nativeElement.querySelector('.tm-reference__value') as HTMLElement;
    expect(chip.getAttribute('dir')).toBe('ltr');
    expect(await (await reference.getCopyButton()).getLabel()).toBe('Copy reference');
  });

  it('copies all 32 characters', async () => {
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    const { reference } = await setup();
    await reference.copy();
    expect(writeText).toHaveBeenCalledWith('4bf92f3577b34da6a3ce929d0e0e4736');
  });
});
