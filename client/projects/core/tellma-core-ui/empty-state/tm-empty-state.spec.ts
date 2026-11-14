// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Inbox } from 'lucide';

import { provideTellmaUi } from '@tellma/core-ui';
import { provideTellmaIcons } from '@tellma/core-ui/icon';
import { TmEmptyStateHarness } from '@tellma/core-ui-testing';

import { TmEmptyState } from './tm-empty-state';

@Component({
  imports: [TmEmptyState],
  template: `
    <tm-empty-state
      heading="No invoices yet"
      [icon]="icon()"
      [tone]="tone()"
      [size]="size()"
      [live]="live()"
    >
      Invoices you create appear here.
      <button tmEmptyStateActions type="button">New invoice</button>
    </tm-empty-state>
  `,
})
class Host {
  readonly icon = signal<string | undefined>('inbox');
  readonly tone = signal<'neutral' | 'error'>('neutral');
  readonly size = signal<'page' | 'panel'>('page');
  readonly live = signal<'off' | 'polite'>('off');
}

async function setup(live: 'off' | 'polite' = 'off') {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), provideTellmaIcons({ inbox: Inbox })],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.live.set(live);
  fixture.detectChanges();
  const state = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmEmptyStateHarness);
  return { fixture, host: fixture.componentInstance, state };
}

describe('tm-empty-state', () => {
  it('renders the icon, the heading, the detail and the actions, in that order', async () => {
    const { fixture, state } = await setup();
    expect(await state.hasIcon()).toBe(true);
    expect(await state.getHeading()).toBe('No invoices yet');
    expect(await state.getDetail()).toBe('Invoices you create appear here.');
    const body = (fixture.nativeElement as HTMLElement).querySelector('.tm-empty-state__body')!;
    const order = [
      'tm-empty-state__icon',
      'tm-empty-state__heading',
      'tm-empty-state__detail',
      'tm-empty-state__actions',
    ];
    expect(
      [...body.children].map((child) => order.find((name) => child.classList.contains(name))),
    ).toEqual(order);
    expect(body.querySelector('.tm-empty-state__actions button')?.textContent).toBe('New invoice');
    expect(body.querySelector('tm-icon')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('is silent by default, and a polite region filled after insertion when live', async () => {
    const quiet = await setup();
    expect(await quiet.state.isAnnounced()).toBe(false);
    TestBed.resetTestingModule();

    const { fixture, state } = await setup('polite');
    expect(await state.isAnnounced()).toBe(true);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(await state.getHeading()).toBe('No invoices yet');
  });

  it('reports the tone and the size', async () => {
    const { fixture, host, state } = await setup();
    expect(await state.getTone()).toBe('neutral');
    expect(await state.getSize()).toBe('page');
    host.tone.set('error');
    host.size.set('panel');
    fixture.detectChanges();
    expect(await state.getTone()).toBe('error');
    expect(await state.getSize()).toBe('panel');
  });

  it('draws no icon when none is named', async () => {
    const { fixture, host, state } = await setup();
    host.icon.set(undefined);
    fixture.detectChanges();
    expect(await state.hasIcon()).toBe(false);
  });
});
