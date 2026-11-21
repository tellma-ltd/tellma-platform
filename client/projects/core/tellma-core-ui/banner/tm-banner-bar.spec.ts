// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmBannerBarHarness } from '@tellma/core-ui-testing';

import { TmBannerBar, type TmBannerTone } from './tm-banner-bar';

@Component({
  imports: [TmBannerBar],
  template: `
    <tm-banner-bar
      [tone]="tone()"
      [message]="message()"
      [actionLabel]="actionLabel()"
      [dismissible]="dismissible()"
      [position]="position()"
      (action)="events.push('action')"
      (dismiss)="events.push('dismiss')"
      (previous)="events.push('previous')"
      (next)="events.push('next')"
    />
  `,
})
class Host {
  readonly tone = signal<TmBannerTone>('info');
  readonly message = signal('A new version is available');
  readonly actionLabel = signal<string | null>(null);
  readonly dismissible = signal(false);
  readonly position = signal<{ index: number; count: number } | null>(null);
  readonly events: string[] = [];
}

/** The unit page loads no token stylesheet; the bar's geometry needs one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

async function setup() {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  fixture.autoDetectChanges();
  await fixture.whenStable();
  const bar = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmBannerBarHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('tm-banner-bar')!;
  return { fixture, host: fixture.componentInstance, bar, element };
}

describe('tm-banner-bar', () => {
  it('is one 32px row with no live role, its tone read before the message', async () => {
    const { bar, element } = await setup();
    expect(element.getBoundingClientRect().height).toBe(32);
    expect(element.getAttribute('role')).toBeNull();
    expect(element.querySelector('[role]')).toBeNull();
    expect(await bar.getTone()).toBe('info');
    expect(await bar.getTonePrefix()).toBe('Info:');
    expect(await bar.getMessage()).toBe('A new version is available');
    expect(await bar.getActionLabel()).toBeNull();
    expect(await bar.getPosition()).toBeNull();
    expect(await bar.isDismissible()).toBe(false);
  });

  it('stays 32px with every control on and a message too long for the line', async () => {
    const { fixture, host, element } = await setup();
    host.message.set('The server will restart for maintenance in a few minutes. '.repeat(10));
    host.actionLabel.set('Learn more');
    host.dismissible.set(true);
    host.position.set({ index: 1, count: 3 });
    host.tone.set('error');
    await fixture.whenStable();
    expect(element.getBoundingClientRect().height).toBe(32);
    const message = element.querySelector<HTMLElement>('.tm-banner-bar__message')!;
    expect(message.scrollWidth).toBeGreaterThan(message.clientWidth);
    for (const button of Array.from(element.querySelectorAll('button'))) {
      const box = button.getBoundingClientRect();
      expect(box.height).toBeGreaterThanOrEqual(24);
      expect(box.width).toBeGreaterThanOrEqual(24);
    }
  });

  it('emits action, dismiss, previous and next from named buttons', async () => {
    const { fixture, host, bar } = await setup();
    host.actionLabel.set('Reload');
    host.dismissible.set(true);
    host.position.set({ index: 2, count: 3 });
    await fixture.whenStable();
    expect(await bar.getActionLabel()).toBe('Reload');
    expect(await bar.getPosition()).toBe('2 of 3');
    expect(await bar.getButtonLabels()).toEqual(['Previous message', 'Next message', 'Dismiss']);
    await bar.clickAction();
    await bar.previous();
    await bar.next();
    await bar.dismiss();
    expect(host.events).toEqual(['action', 'previous', 'next', 'dismiss']);
  });

  it('hides the pager for a single message', async () => {
    const { fixture, host, bar } = await setup();
    host.position.set({ index: 1, count: 1 });
    await fixture.whenStable();
    expect(await bar.getPosition()).toBeNull();
  });

  it('draws the tone glyph and reads the tone prefix for each tone', async () => {
    const { fixture, host, bar, element } = await setup();
    host.tone.set('warning');
    await fixture.whenStable();
    expect(await bar.getTonePrefix()).toBe('Warning:');
    expect(element.querySelector('.tm-banner-bar__icon svg')).not.toBeNull();
  });
});
