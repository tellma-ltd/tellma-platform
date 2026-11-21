// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, TemplateRef, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Dialog, type DialogConfig } from '@angular/cdk/dialog';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmModalHarness } from '@tellma/core-ui-testing';

import { TM_MODAL_DATA, TmModal } from './tm-modal';
import { TmModalFooter } from './tm-modal-footer';
import { TmModalRef, type TmModalResult } from './tm-modal-ref';

/** Modal content: injects the ref and the data, closes with a result. */
@Component({
  selector: 'tm-test-modal-content',
  imports: [TmModalFooter],
  template: `
    <p class="content-text">Hello {{ data === null ? 'nobody' : data }}</p>
    <div tmModalFooter>
      <button type="button" class="save" (click)="ref.close('saved')">Save</button>
    </div>
  `,
})
class ContentProbe {
  protected readonly data = inject(TM_MODAL_DATA);
  protected readonly ref = inject(TmModalRef) as TmModalRef<string>;
}

/** Alert-dialog content: the message element the dialog is described by. */
@Component({
  selector: 'tm-test-alert-content',
  template: `<p id="probe-message">Unsaved changes will be lost.</p>`,
})
class AlertProbe {}

@Component({
  template: `
    <button type="button" class="opener">Open</button>
    <ng-template #content let-ref let-data="data">
      <p class="template-text">Template for {{ data }}</p>
      <button type="button" class="template-close" (click)="ref.close()">Done</button>
    </ng-template>
  `,
})
class Host {
  readonly content = viewChild.required<TemplateRef<unknown>>('content');
}

async function setup(): Promise<{ fixture: ComponentFixture<Host>; host: Host; modal: TmModal }> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  return { fixture, host: fixture.componentInstance, modal: TestBed.inject(TmModal) };
}

function shell(): HTMLElement | null {
  return document.querySelector('tm-modal-shell');
}

function container(): HTMLElement {
  return document.querySelector('cdk-dialog-container') as HTMLElement;
}

function pressEscape(target: Element, init: KeyboardEventInit = {}): void {
  target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, ...init }));
}

/** Snapshots a `closed` promise's settlement without awaiting it. */
function track<R>(ref: TmModalRef<R>): { result: () => TmModalResult<R> | 'open' } {
  let outcome: TmModalResult<R> | 'open' = 'open';
  void ref.closed.then((result) => (outcome = result));
  return { result: () => outcome };
}

describe('TmModal', () => {
  it('renders component content in the shell with title, data, and injectable ref', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open<string>(ContentProbe, { title: 'Greeting', data: 'Ahmad' });
    await fixture.whenStable();

    expect(shell()).not.toBeNull();
    expect(document.querySelector('.tm-modal__title')?.textContent).toBe('Greeting');
    expect(document.querySelector('.content-text')?.textContent).toContain('Hello Ahmad');

    // The content closes itself through the injected ref.
    (document.querySelector('.save') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(shell()).toBeNull();
    expect(await ref.closed).toEqual({ via: 'api', value: 'saved' });
  });

  it('injects TM_MODAL_DATA as null when opened without data', async () => {
    const { fixture, modal } = await setup();
    modal.open(ContentProbe, { title: 'No data' });
    await fixture.whenStable();
    expect(document.querySelector('.content-text')?.textContent).toContain('Hello nobody');
  });

  it('close(value) resolves {via: api} with the value; close() with undefined', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open<number>(ContentProbe, { title: 'Result' });
    await fixture.whenStable();
    ref.close(42);
    expect(await ref.closed).toEqual({ via: 'api', value: 42 });

    const bare = modal.open(ContentProbe, { title: 'Bare' });
    await fixture.whenStable();
    bare.close();
    expect(await bare.closed).toEqual({ via: 'api', value: undefined });
  });

  it('renders template content with the ref as $implicit and the data in context', async () => {
    const { fixture, host, modal } = await setup();
    const ref = modal.open(host.content(), { title: 'Template', data: 'lines' });
    await fixture.whenStable();
    expect(document.querySelector('.template-text')?.textContent).toContain('Template for lines');

    (document.querySelector('.template-close') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(await ref.closed).toEqual({ via: 'api', value: undefined });
  });

  it('retitles an open modal in place, keeping its name; never names an untitled one', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'First' });
    await fixture.whenStable();
    const title = document.querySelector('.tm-modal__title') as HTMLElement;
    ref.ɵsetTitle('Second');
    ref.ɵsetTitle('');
    await fixture.whenStable();
    // The same element, so aria-labelledby still points at it.
    expect(document.querySelector('.tm-modal__title')).toBe(title);
    expect(title.textContent).toBe('Second');
    expect(container().getAttribute('aria-labelledby')).toBe(title.id);
    ref.close();

    // An untitled modal warns in dev mode; that warning is covered elsewhere.
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const untitled = modal.open(ContentProbe, {});
      await fixture.whenStable();
      untitled.ɵsetTitle('Late');
      await fixture.whenStable();
      expect(document.querySelector('.tm-modal__title')).toBeNull();
      untitled.close();
    } finally {
      warn.mockRestore();
    }
  });

  it('the X button dismisses with {via: close-button}; showClose: false removes it', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'Closable' });
    await fixture.whenStable();
    (document.querySelector('.tm-modal__close') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(await ref.closed).toEqual({ via: 'close-button' });

    modal.open(ContentProbe, { title: 'No X', showClose: false });
    await fixture.whenStable();
    expect(document.querySelector('.tm-modal__close')).toBeNull();
  });

  it('backdrop click dismisses with {via: backdrop}; backdropDismiss: false ignores it', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'Backdrop' });
    await fixture.whenStable();
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
    await fixture.whenStable();
    expect(await ref.closed).toEqual({ via: 'backdrop' });

    const pinned = modal.open(ContentProbe, { title: 'Pinned', backdropDismiss: false });
    const tracked = track(pinned);
    await fixture.whenStable();
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
    await fixture.whenStable();
    expect(tracked.result()).toBe('open');
    expect(shell()).not.toBeNull();
  });

  it('Escape dismisses with {via: escape}; escapeDismiss: false ignores it', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'Esc' });
    await fixture.whenStable();
    pressEscape(container(), { cancelable: true });
    await fixture.whenStable();
    expect(await ref.closed).toEqual({ via: 'escape' });

    const pinned = modal.open(ContentProbe, { title: 'Esc off', escapeDismiss: false });
    const tracked = track(pinned);
    await fixture.whenStable();
    pressEscape(container());
    await fixture.whenStable();
    expect(tracked.result()).toBe('open');
    expect(shell()).not.toBeNull();
  });

  it('a consumed Escape never dismisses', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'Consumed Esc' });
    const tracked = track(ref);
    await fixture.whenStable();
    const consumed = new KeyboardEvent('keydown', {
      key: 'Escape',
      bubbles: true,
      cancelable: true,
    });
    consumed.preventDefault();
    container().dispatchEvent(consumed);
    await fixture.whenStable();
    expect(tracked.result()).toBe('open');
    expect(shell()).not.toBeNull();
  });

  it('canDismiss=false blocks every user dismissal; programmatic close still works', async () => {
    const { fixture, modal } = await setup();
    let guardCalls = 0;
    const ref = modal.open(ContentProbe, {
      title: 'Guarded',
      canDismiss: () => {
        guardCalls += 1;
        return false;
      },
    });
    const tracked = track(ref);
    await fixture.whenStable();

    (document.querySelector('.tm-modal__close') as HTMLButtonElement).click();
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
    pressEscape(container());
    await fixture.whenStable();
    expect(guardCalls).toBe(3);
    expect(tracked.result()).toBe('open');
    expect(shell()).not.toBeNull();

    ref.close();
    await fixture.whenStable();
    expect(await ref.closed).toEqual({ via: 'api', value: undefined });
  });

  it('an async guard blocks repeat dismissals while pending and closes with the original via', async () => {
    const { fixture, modal } = await setup();
    let resolveGuard!: (allowed: boolean) => void;
    let guardCalls = 0;
    const ref = modal.open(ContentProbe, {
      title: 'Async guard',
      canDismiss: () => {
        guardCalls += 1;
        return new Promise<boolean>((resolve) => (resolveGuard = resolve));
      },
    });
    await fixture.whenStable();

    pressEscape(container());
    // While the guard is pending, further attempts are ignored entirely.
    (document.querySelector('.tm-modal__close') as HTMLButtonElement).click();
    pressEscape(container());
    expect(guardCalls).toBe(1);

    resolveGuard(true);
    expect(await ref.closed).toEqual({ via: 'escape' });
  });

  it('a rejecting guard keeps the modal open and re-arms dismissal', async () => {
    const { fixture, modal } = await setup();
    let verdict: Promise<boolean> = Promise.reject(new Error('guard failed'));
    verdict.catch(() => undefined); // silence the pre-handled rejection
    const ref = modal.open(ContentProbe, { title: 'Rejecting', canDismiss: () => verdict });
    const tracked = track(ref);
    await fixture.whenStable();

    pressEscape(container());
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(tracked.result()).toBe('open');
    expect(shell()).not.toBeNull();

    verdict = Promise.resolve(true);
    pressEscape(container());
    expect(await ref.closed).toEqual({ via: 'escape' });
  });

  it('sets role=dialog and labels the dialog by the rendered title', async () => {
    const { fixture, modal } = await setup();
    modal.open(ContentProbe, { title: 'Labelled' });
    await fixture.whenStable();
    const dialog = container();
    expect(dialog.getAttribute('role')).toBe('dialog');
    const labelledBy = dialog.getAttribute('aria-labelledby');
    expect(labelledBy).not.toBeNull();
    const label = document.getElementById(labelledBy as string);
    expect(label?.textContent).toBe('Labelled');
    expect(label?.tagName).toBe('H2');
  });

  it('applies the size bucket and custom panelClass to the overlay pane', async () => {
    const { fixture, modal } = await setup();
    modal.open(ContentProbe, { title: 'Panel', size: 'sm', panelClass: 'custom-panel' });
    await fixture.whenStable();
    const pane = document.querySelector('.tm-modal-panel') as HTMLElement;
    expect(pane.classList.contains('tm-modal-panel--sm')).toBe(true);
    expect(pane.classList.contains('custom-panel')).toBe(true);
  });

  it('mirrors an open modal when the document direction flips', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'Mirrored' });
    await fixture.whenStable();
    // The overlay host carries the direction the pane's content inherits.
    const host = (document.querySelector('.tm-modal-panel') as HTMLElement).parentElement!;
    expect(host.getAttribute('dir')).toBe('ltr');
    try {
      document.documentElement.dir = 'rtl';
      // The live root Directionality observes the attribute in a microtask.
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(host.getAttribute('dir')).toBe('rtl');

      document.documentElement.dir = 'ltr';
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(host.getAttribute('dir')).toBe('ltr');
    } finally {
      document.documentElement.removeAttribute('dir');
      ref.close();
    }
  });

  it('opens in the document direction current at open time', async () => {
    const { fixture, modal } = await setup();
    document.documentElement.dir = 'rtl';
    await new Promise((resolve) => setTimeout(resolve, 0));
    try {
      const ref = modal.open(ContentProbe, { title: 'Opened in RTL' });
      await fixture.whenStable();
      const host = (document.querySelector('.tm-modal-panel') as HTMLElement).parentElement!;
      expect(host.getAttribute('dir')).toBe('rtl');
      ref.close();
    } finally {
      document.documentElement.removeAttribute('dir');
    }
  });

  it('passes no direction to the CDK, and the role and description when given', async () => {
    const { fixture, modal } = await setup();
    const open = vi.spyOn(TestBed.inject(Dialog), 'open');
    modal.open(AlertProbe, { title: 'Discard?', role: 'alertdialog', describedBy: 'probe-message' });
    await fixture.whenStable();
    const config = open.mock.calls[0][1] as DialogConfig;
    expect('direction' in config).toBe(false);
    expect(config.role).toBe('alertdialog');
    expect(config.ariaDescribedBy).toBe('probe-message');

    // …and they reach the dialog element: the message describes the dialog.
    const dialog = container();
    expect(dialog.getAttribute('role')).toBe('alertdialog');
    expect(dialog.getAttribute('aria-describedby')).toBe('probe-message');
    expect(document.getElementById('probe-message')?.textContent).toBe(
      'Unsaved changes will be lost.',
    );
  });

  it('defaults to role=dialog with no description', async () => {
    const { fixture, modal } = await setup();
    const open = vi.spyOn(TestBed.inject(Dialog), 'open');
    modal.open(ContentProbe, { title: 'Plain' });
    await fixture.whenStable();
    const config = open.mock.calls[0][1] as DialogConfig;
    expect(config.role).toBe('dialog');
    expect(config.ariaDescribedBy).toBeNull();
    expect(container().hasAttribute('aria-describedby')).toBe(false);
  });

  it('warns in dev mode for an alert dialog without a description', async () => {
    const { fixture, modal } = await setup();
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      modal.open(AlertProbe, { title: 'Discard?', role: 'alertdialog' });
      await fixture.whenStable();
      expect(warn.mock.calls.some(([message]) => String(message).includes('describedBy'))).toBe(
        true,
      );

      warn.mockClear();
      modal.open(AlertProbe, { title: 'Discard?', role: 'alertdialog', describedBy: 'x' });
      expect(warn).not.toHaveBeenCalled();
    } finally {
      warn.mockRestore();
    }
  });

  it('a drawer carries the placement and size classes, centered modals the size only', async () => {
    const { fixture, modal } = await setup();
    modal.open(ContentProbe, { title: 'Centered', size: 'lg' });
    await fixture.whenStable();
    const centered = document.querySelector('.tm-modal-panel') as HTMLElement;
    expect(centered.classList.contains('tm-modal-panel--lg')).toBe(true);
    expect(centered.classList.contains('tm-modal-panel--end')).toBe(false);
    expect(document.querySelector('.tm-modal-backdrop--end')).toBeNull();
    TestBed.inject(Dialog).closeAll();
    await fixture.whenStable();

    modal.open(ContentProbe, { title: 'Drawer', placement: 'end', size: 'sm' });
    await fixture.whenStable();
    const drawer = document.querySelector('.tm-modal-panel') as HTMLElement;
    expect(drawer.classList.contains('tm-modal-panel--end')).toBe(true);
    expect(drawer.classList.contains('tm-modal-panel--sm')).toBe(true);
    expect(document.querySelector('.tm-modal-backdrop--end')).not.toBeNull();
    expect(shell()?.classList.contains('tm-modal--end')).toBe(true);
  });

  it('docks a drawer to the inline-end edge at full height, with no entrance motion', async () => {
    const { fixture, modal } = await setup();
    modal.open(ContentProbe, { title: 'Drawer', placement: 'end' });
    await fixture.whenStable();
    const pane = document.querySelector('.tm-modal-panel') as HTMLElement;
    const rect = pane.getBoundingClientRect();
    expect(Math.abs(rect.right - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
    expect(Math.abs(rect.top)).toBeLessThanOrEqual(1);
    expect(Math.abs(rect.height - window.innerHeight)).toBeLessThanOrEqual(1);
    expect(getComputedStyle(container()).animationName).toBe('none');
  });

  it('a drawer dismisses on its backdrop through the canDismiss guard', async () => {
    const { fixture, modal } = await setup();
    let allow = false;
    const ref = modal.open(ContentProbe, {
      title: 'Drawer',
      placement: 'end',
      canDismiss: () => allow,
    });
    const tracked = track(ref);
    await fixture.whenStable();
    const backdrop = document.querySelector('.tm-modal-backdrop--end') as HTMLElement;
    backdrop.click();
    await fixture.whenStable();
    expect(tracked.result()).toBe('open');
    expect(shell()).not.toBeNull();

    allow = true;
    backdrop.click();
    expect(await ref.closed).toEqual({ via: 'backdrop' });
  });

  it('moves an open drawer to the other edge when the document direction flips', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'Drawer', placement: 'end' });
    await fixture.whenStable();
    const pane = document.querySelector('.tm-modal-panel') as HTMLElement;
    try {
      document.documentElement.dir = 'rtl';
      // The live root Directionality observes the attribute in a microtask.
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(Math.abs(pane.getBoundingClientRect().left)).toBeLessThanOrEqual(1);

      document.documentElement.dir = 'ltr';
      await new Promise((resolve) => setTimeout(resolve, 0));
      const right = pane.getBoundingClientRect().right;
      expect(Math.abs(right - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
    } finally {
      document.documentElement.removeAttribute('dir');
      ref.close();
    }
  });

  it('marks footer content with the shell footer class', async () => {
    const { fixture, modal } = await setup();
    modal.open(ContentProbe, { title: 'Footer' });
    await fixture.whenStable();
    const footer = document.querySelector('[tmModalFooter]') as HTMLElement;
    expect(footer.classList.contains('tm-modal__footer')).toBe(true);
  });

  it('stacked modals: Escape dismisses the topmost layer only', async () => {
    const { fixture, modal } = await setup();
    const first = modal.open(ContentProbe, { title: 'First' });
    await fixture.whenStable();
    const second = modal.open(ContentProbe, { title: 'Second' });
    const firstTracked = track(first);
    await fixture.whenStable();
    expect(document.querySelectorAll('tm-modal-shell').length).toBe(2);

    pressEscape(document.body);
    await fixture.whenStable();
    expect(await second.closed).toEqual({ via: 'escape' });
    expect(firstTracked.result()).toBe('open');
    expect(document.querySelectorAll('tm-modal-shell').length).toBe(1);

    pressEscape(document.body);
    await fixture.whenStable();
    expect(await first.closed).toEqual({ via: 'escape' });
    expect(document.querySelectorAll('tm-modal-shell').length).toBe(0);
  });

  it('moves focus into the dialog on open and restores it to the opener on close', async () => {
    const { fixture, modal } = await setup();
    const opener = fixture.nativeElement.querySelector('.opener') as HTMLButtonElement;
    opener.focus();
    const ref = modal.open(ContentProbe, { title: 'Focus' });
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0)); // focus trap settles
    expect(container().contains(document.activeElement)).toBe(true);

    ref.close();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(document.activeElement).toBe(opener);
  });

  it('drives the shell through TmModalHarness', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'Harnessed', data: 'you' });
    await fixture.whenStable();
    const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);
    const harness = await loader.getHarness(TmModalHarness);
    expect(await harness.getTitle()).toBe('Harnessed');
    expect(await harness.getBodyText()).toContain('Hello you');
    expect(await harness.hasCloseButton()).toBe(true);
    expect(await harness.getPlacement()).toBe('center');
    await harness.close();
    expect(await ref.closed).toEqual({ via: 'close-button' });
  });
});

describe('TmModalHarness placement', () => {
  it('reports the drawer placement', async () => {
    const { fixture, modal } = await setup();
    const ref = modal.open(ContentProbe, { title: 'Drawer', placement: 'end' });
    await fixture.whenStable();
    const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);
    const harness = await loader.getHarness(TmModalHarness);
    expect(await harness.getPlacement()).toBe('end');
    ref.close();
  });
});
