// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmAlertHarness } from '@tellma/core-ui-testing';

import { TmAlert, type TmAlertKind, type TmAlertLive } from './tm-alert';

@Component({
  imports: [TmAlert],
  template: `
    <tm-alert [kind]="kind()" [live]="live()" [heading]="heading()" data-testid="alert">
      Something happened.
    </tm-alert>
  `,
})
class Host {
  readonly kind = signal<TmAlertKind>('info');
  readonly live = signal<TmAlertLive>('off');
  readonly heading = signal<string | undefined>(undefined);
}

/** Inserts the alert fresh — the dynamic (failed-save) scenario. */
@Component({
  imports: [TmAlert],
  template: `
    @if (shown()) {
      <tm-alert kind="error" live="assertive">Save failed.</tm-alert>
    }
  `,
})
class InsertionHost {
  readonly shown = signal(false);
}

function setup(): { fixture: ComponentFixture<Host>; host: Host; element: HTMLElement } {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const element = fixture.nativeElement.querySelector('tm-alert') as HTMLElement;
  return { fixture, host: fixture.componentInstance, element };
}

const flushMicrotasks = () => Promise.resolve();

describe('tm-alert', () => {
  it('renders the kind as a host class with a glyph and the localized hidden prefix', () => {
    const { fixture, host, element } = setup();
    expect(element.classList).toContain('tm-alert--info');
    expect(element.querySelector('.tm-alert__icon')).not.toBeNull();
    expect(element.querySelector('.tm-alert__sr-kind')?.textContent).toBe('Info:');

    host.kind.set('error');
    fixture.detectChanges();
    expect(element.classList).toContain('tm-alert--error');
    expect(element.classList).not.toContain('tm-alert--info');
    expect(element.querySelector('.tm-alert__sr-kind')?.textContent).toBe('Error:');
  });

  it('renders the optional heading before the projected content', () => {
    const { fixture, host, element } = setup();
    host.heading.set('Save failed');
    fixture.detectChanges();
    const heading = element.querySelector('.tm-alert__heading');
    expect(heading?.textContent).toBe('Save failed');
    expect(element.textContent).toContain('Something happened.');
  });

  it('maps live to the region role: off → none, polite → status, assertive → alert', () => {
    const { fixture, host, element } = setup();
    const region = element.querySelector('.tm-alert__content')!;
    expect(region.getAttribute('role')).toBeNull();
    host.live.set('polite');
    fixture.detectChanges();
    expect(region.getAttribute('role')).toBe('status');
    host.live.set('assertive');
    fixture.detectChanges();
    expect(region.getAttribute('role')).toBe('alert');
  });

  it('an off alert renders its content synchronously (no gate)', () => {
    const { element } = setup();
    // No microtask flush: the static alert already carries its content.
    expect(element.textContent).toContain('Something happened.');
  });

  it('a freshly inserted live alert enters EMPTY and populates a microtask later', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(InsertionHost);
    fixture.detectChanges();

    fixture.componentInstance.shown.set(true);
    fixture.detectChanges();
    // Synchronously after insertion: the role="alert" region exists with NO
    // content — the announcement trigger must be a mutation INSIDE it.
    const region = fixture.nativeElement.querySelector('.tm-alert__content') as HTMLElement;
    expect(region.getAttribute('role')).toBe('alert');
    expect(region.textContent?.trim()).toBe('');

    await flushMicrotasks();
    fixture.detectChanges();
    expect(region.textContent).toContain('Error:');
    expect(region.textContent).toContain('Save failed.');
  });

  it('drives the alert through TmAlertHarness', async () => {
    const { fixture, host } = setup();
    const alert = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmAlertHarness);
    expect(await alert.getKind()).toBe('info');
    expect(await alert.getRole()).toBeNull();
    expect(await alert.getHeading()).toBeNull();
    expect(await alert.getText()).toContain('Something happened.');

    host.kind.set('warning');
    host.live.set('polite');
    host.heading.set('Heads up');
    expect(await alert.getKind()).toBe('warning');
    expect(await alert.getRole()).toBe('status');
    expect(await alert.getHeading()).toBe('Heads up');
  });
});
