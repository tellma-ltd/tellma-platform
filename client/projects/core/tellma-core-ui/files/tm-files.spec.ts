// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmDropzoneHarness, TmFilePickerHarness } from '@tellma/core-ui-testing';

import { tmMaxMegabytes } from './internal/byte-size';
import { tmSelectFiles } from './internal/file-selection';
import { TmDropzone } from './tm-dropzone';
import { TmFilePicker } from './tm-file-picker';
import type { TmFileSelection } from './tm-file-selection';

function file(name: string, type: string, size = 10): File {
  return new File([new Uint8Array(size)], name, { type });
}

const LOOSE = { accept: '', multiple: true, maxFileSize: 1000, maxFiles: null };

describe('tmSelectFiles (the shared engine)', () => {
  it('matches extensions, exact MIME types, and MIME wildcards case-insensitively', () => {
    const options = { ...LOOSE, accept: '.PDF, image/*, application/json' };
    const selection = tmSelectFiles(
      [
        file('report.pdf', 'application/pdf'),
        file('photo.png', 'image/png'),
        file('data.JSON', 'application/json'),
        file('notes.txt', 'text/plain'),
      ],
      null,
      options,
    );
    expect(selection.accepted.map((accepted) => accepted.name)).toEqual([
      'report.pdf',
      'photo.png',
      'data.JSON',
    ]);
    expect(selection.rejected).toEqual([
      { file: expect.objectContaining({ name: 'notes.txt' }), reason: 'type' },
    ]);
  });

  it('an empty accept admits everything', () => {
    const selection = tmSelectFiles([file('anything.bin', '')], null, LOOSE);
    expect(selection.accepted).toHaveLength(1);
    expect(selection.rejected).toHaveLength(0);
  });

  it('rejects oversized files with reason size', () => {
    const selection = tmSelectFiles(
      [file('small.txt', 'text/plain', 10), file('big.txt', 'text/plain', 2000)],
      null,
      LOOSE,
    );
    expect(selection.accepted.map((accepted) => accepted.name)).toEqual(['small.txt']);
    expect(selection.rejected[0].reason).toBe('size');
  });

  it('multiple: false takes the first file and rejects the rest with count', () => {
    const selection = tmSelectFiles(
      [file('a.txt', 'text/plain'), file('b.txt', 'text/plain'), file('c.txt', 'text/plain')],
      null,
      { ...LOOSE, multiple: false },
    );
    expect(selection.accepted.map((accepted) => accepted.name)).toEqual(['a.txt']);
    expect(selection.rejected.map((rejection) => rejection.reason)).toEqual(['count', 'count']);
  });

  it('maxFiles caps the selection; earlier rejections never consume slots', () => {
    const selection = tmSelectFiles(
      [
        file('big.txt', 'text/plain', 2000), // rejected: size — no slot used
        file('a.txt', 'text/plain'),
        file('b.txt', 'text/plain'),
        file('c.txt', 'text/plain'),
      ],
      null,
      { ...LOOSE, maxFiles: 2 },
    );
    expect(selection.accepted.map((accepted) => accepted.name)).toEqual(['a.txt', 'b.txt']);
    expect(selection.rejected.map((rejection) => rejection.reason)).toEqual(['size', 'count']);
  });

  it('folder flags reject directories before any other check', () => {
    const selection = tmSelectFiles(
      [file('folder', ''), file('a.txt', 'text/plain')],
      [true, false],
      LOOSE,
    );
    expect(selection.accepted.map((accepted) => accepted.name)).toEqual(['a.txt']);
    expect(selection.rejected[0].reason).toBe('folder');
  });
});

describe('tmMaxMegabytes (the announced ceiling)', () => {
  it('never rounds a real ceiling to 0 MB, nor 512 KiB up to a whole MB', () => {
    expect(tmMaxMegabytes(512 * 1024)).toBe(0.5);
    expect(tmMaxMegabytes(100 * 1024)).toBe(0.098);
    expect(tmMaxMegabytes(1.4 * 1024 * 1024)).toBe(1.4);
  });

  it('keeps whole megabytes whole and drops the noise above 10 MB', () => {
    expect(tmMaxMegabytes(1024 * 1024)).toBe(1);
    expect(tmMaxMegabytes(20 * 1024 * 1024)).toBe(20);
    expect(tmMaxMegabytes(100 * 1024 * 1024)).toBe(100);
  });

  it('degrades to 0 for a ceiling that has no megabyte value', () => {
    expect(tmMaxMegabytes(0)).toBe(0);
    expect(tmMaxMegabytes(Number.NaN)).toBe(0);
  });
});

@Component({
  imports: [TmFilePicker, TmDropzone],
  template: `
    <button tmFilePicker accept=".txt" [maxFileSize]="1000" (filesSelected)="record($event)">
      Attach
    </button>
    <tm-dropzone
      accept=".txt,image/*"
      [multiple]="multiple()"
      [maxFileSize]="1024 * 1024"
      [maxFiles]="2"
      (filesSelected)="record($event)"
    />
  `,
})
class Host {
  readonly multiple = signal(true);
  readonly selections: TmFileSelection[] = [];
  record(selection: TmFileSelection): void {
    this.selections.push(selection);
  }
}

@Component({
  imports: [TmDropzone],
  template: `<tm-dropzone [maxFileSize]="512 * 1024" />`,
})
class SmallCapHost {}

async function setup(): Promise<{ fixture: ComponentFixture<Host>; host: Host; root: HTMLElement }> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  return { fixture, host: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
}

function makeTransfer(...files: File[]): DataTransfer {
  const transfer = new DataTransfer();
  for (const item of files) {
    transfer.items.add(item);
  }
  return transfer;
}

function liveAnnouncement(): string {
  return (
    Array.from(document.querySelectorAll('.cdk-live-announcer-element'))
      .map((element) => element.textContent?.trim() ?? '')
      .find((text) => text !== '') ?? ''
  );
}

describe('tmFilePicker', () => {
  it('creates the hidden body-hosted input on demand and removes it on destroy', async () => {
    const { fixture, root } = await setup();
    expect(document.body.querySelector('input[type="file"]')).toBeNull(); // lazy

    (root.querySelector('button[tmFilePicker]') as HTMLButtonElement).click();
    const input = document.body.querySelector<HTMLInputElement>('input[type="file"]');
    expect(input).not.toBeNull();
    expect(input?.getAttribute('aria-hidden')).toBe('true');
    expect(input?.tabIndex).toBe(-1);
    expect(input?.accept).toBe('.txt');
    expect(input?.parentElement).toBe(document.body);

    fixture.destroy();
    expect(document.body.querySelector('input[type="file"]')).toBeNull();
  });

  it('a dialog selection runs the guardrails, emits, and announces', async () => {
    const { fixture, host, root } = await setup();
    (root.querySelector('button[tmFilePicker]') as HTMLButtonElement).click();
    const input = document.body.querySelector<HTMLInputElement>('input[type="file"]')!;
    const transfer = makeTransfer(file('ok.txt', 'text/plain'), file('bad.png', 'image/png'));
    input.files = transfer.files;
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    expect(host.selections).toHaveLength(1);
    expect(host.selections[0].accepted.map((accepted) => accepted.name)).toEqual(['ok.txt']);
    expect(host.selections[0].rejected[0].reason).toBe('type');
    await new Promise((resolve) => setTimeout(resolve, 150)); // announcer defers
    expect(liveAnnouncement()).toContain('1 file added');
    expect(liveAnnouncement()).toContain('bad.png is not an accepted file type');
  });
});

describe('tm-dropzone', () => {
  it('re-exposes the host directive inputs AND the filesSelected output (pinned)', async () => {
    const { fixture, host, root } = await setup();
    const zone = root.querySelector('tm-dropzone') as HTMLElement;
    // Drop through the zone: the emission must arrive via the re-exposed
    // output binding on <tm-dropzone> — the hostDirectives contract.
    const transfer = makeTransfer(file('a.txt', 'text/plain'), file('b.txt', 'text/plain'));
    zone.dispatchEvent(new DragEvent('drop', { dataTransfer: transfer, cancelable: true }));
    await fixture.whenStable();
    expect(host.selections).toHaveLength(1);
    expect(host.selections[0].accepted.map((accepted) => accepted.name)).toEqual([
      'a.txt',
      'b.txt',
    ]);
  });

  it('is a single focusable button; Enter opens the shared hidden input', async () => {
    const { root } = await setup();
    const zone = root.querySelector('tm-dropzone') as HTMLElement;
    expect(zone.getAttribute('role')).toBe('button');
    expect(zone.tabIndex).toBe(0);
    expect(zone.querySelector('button, a, input')).toBeNull(); // no nested interactives

    zone.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
    const input = document.body.querySelector<HTMLInputElement>('input[type="file"]');
    expect(input).not.toBeNull();
    expect(input?.multiple).toBe(true); // the re-exposed input reached the picker
  });

  it('applies the guardrails to drops: maxFiles and accept', async () => {
    const { fixture, host, root } = await setup();
    const zone = root.querySelector('tm-dropzone') as HTMLElement;
    const transfer = makeTransfer(
      file('a.txt', 'text/plain'),
      file('b.txt', 'text/plain'),
      file('c.txt', 'text/plain'),
      file('nope.bin', 'application/octet-stream'),
    );
    zone.dispatchEvent(new DragEvent('drop', { dataTransfer: transfer, cancelable: true }));
    await fixture.whenStable();
    const selection = host.selections[0];
    expect(selection.accepted.map((accepted) => accepted.name)).toEqual(['a.txt', 'b.txt']);
    expect(selection.rejected.map((rejection) => rejection.reason).sort()).toEqual([
      'count',
      'type',
    ]);
  });

  it('a focused paste with clipboard files runs the same pipeline', async () => {
    const { fixture, host, root } = await setup();
    const zone = root.querySelector('tm-dropzone') as HTMLElement;
    zone.focus();
    const transfer = makeTransfer(file('shot.png', 'image/png'));
    zone.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: transfer, cancelable: true }),
    );
    await fixture.whenStable();
    expect(host.selections).toHaveLength(1);
    expect(host.selections[0].accepted[0].name).toBe('shot.png');
  });

  it('drag-over highlights and clears through nested enter/leave pairs', async () => {
    const { fixture, root } = await setup();
    const zone = root.querySelector('tm-dropzone') as HTMLElement;
    zone.dispatchEvent(new DragEvent('dragenter', { cancelable: true }));
    zone.dispatchEvent(new DragEvent('dragenter', { cancelable: true })); // a child boundary
    await fixture.whenStable();
    expect(zone.classList.contains('tm-dropzone--dragover')).toBe(true);
    zone.dispatchEvent(new DragEvent('dragleave'));
    await fixture.whenStable();
    expect(zone.classList.contains('tm-dropzone--dragover')).toBe(true); // still inside
    zone.dispatchEvent(new DragEvent('dragleave'));
    await fixture.whenStable();
    expect(zone.classList.contains('tm-dropzone--dragover')).toBe(false);
  });

  it('the document guard cancels missed FILE drops while a dropzone is connected', async () => {
    const { fixture } = await setup();
    const withFiles = (): DataTransfer => makeTransfer(file('missed.txt', 'text/plain'));

    const missed = new DragEvent('dragover', {
      bubbles: true,
      cancelable: true,
      dataTransfer: withFiles(),
    });
    document.body.dispatchEvent(missed);
    expect(missed.defaultPrevented).toBe(true); // the guard is armed

    const drop = new DragEvent('drop', {
      bubbles: true,
      cancelable: true,
      dataTransfer: withFiles(),
    });
    document.body.dispatchEvent(drop);
    expect(drop.defaultPrevented).toBe(true);

    // A TEXT drag (dragging between inputs) is ordinary editing — the
    // guard must leave native drag-and-drop alone.
    const textTransfer = new DataTransfer();
    textTransfer.setData('text/plain', 'dragged words');
    const textDrag = new DragEvent('dragover', {
      bubbles: true,
      cancelable: true,
      dataTransfer: textTransfer,
    });
    document.body.dispatchEvent(textDrag);
    expect(textDrag.defaultPrevented).toBe(false);

    fixture.destroy(); // the last dropzone leaves → the guard uninstalls
    const after = new DragEvent('dragover', {
      bubbles: true,
      cancelable: true,
      dataTransfer: withFiles(),
    });
    document.body.dispatchEvent(after);
    expect(after.defaultPrevented).toBe(false);
  });

  it('drives both surfaces through their harnesses', async () => {
    const { fixture } = await setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const picker = await loader.getHarness(TmFilePickerHarness);
    expect(await picker.getLabel()).toBe('Attach');
    const dropzone = await loader.getHarness(TmDropzoneHarness);
    expect(await dropzone.getHintText()).toBe('Drag and drop here, paste, or browse');
    const meta = await dropzone.getMetaLines();
    expect(meta[0]).toBe('Accepted: .txt,image/*');
    expect(meta[1]).toBe('Up to 1 MB each');
  });

  it('states a sub-megabyte ceiling as itself, not as "0 MB"', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(SmallCapHost);
    await fixture.whenStable();
    const dropzone = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmDropzoneHarness);
    expect(await dropzone.getMetaLines()).toEqual(['Up to 0.5 MB each']);
  });
});
