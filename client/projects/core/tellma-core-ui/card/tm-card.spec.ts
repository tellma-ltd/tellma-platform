// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmCardHarness } from '@tellma/core-ui-testing';

import { TmCard, type TmCardHeadingLevel } from './tm-card';

@Component({
  imports: [TmCard],
  template: `
    @if (bindInvalid()) {
      <tm-card
        [heading]="heading()"
        [headingLevel]="level()"
        [count]="count()"
        [collapsible]="collapsible()"
        [(expanded)]="expanded"
        [preserveContent]="preserve()"
        [invalid]="invalid()"
      >
        <span tmCardSubtitle>Ledger lines</span>
        <button type="button" tmCardActions>Add</button>
        <input class="field" aria-label="Amount" />
        <p tmCardFooter>Footer</p>
      </tm-card>
    } @else {
      <tm-card
        [heading]="heading()"
        [headingLevel]="level()"
        [count]="count()"
        [collapsible]="collapsible()"
        [(expanded)]="expanded"
        [preserveContent]="preserve()"
      >
        <input class="field" aria-label="Amount" />
      </tm-card>
    }
  `,
})
class Host {
  readonly heading = signal<string | undefined>('Lines');
  readonly level = signal<TmCardHeadingLevel>(2);
  readonly count = signal<number | null>(null);
  readonly collapsible = signal(false);
  readonly expanded = signal(true);
  readonly preserve = signal(false);
  readonly bindInvalid = signal(true);
  readonly invalid = signal(false);
}

/** The unit page loads no token stylesheet; the dot's box needs one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

async function setup(configure: (host: Host) => void = () => undefined) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await fixture.whenStable();
  const card = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmCardHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('tm-card')!;
  return { fixture, host: fixture.componentInstance, card, element };
}

describe('tm-card', () => {
  it('renders the heading at its level, the count, the subtitle and the slots', async () => {
    const { fixture, host, card, element } = await setup((h) => h.count.set(12));
    expect(await card.getHeading()).toBe('Lines');
    expect(await card.getHeadingLevel()).toBe(2);
    expect(await card.getCount()).toBe('12');
    expect(await card.getSubtitle()).toBe('Ledger lines');
    expect(element.querySelector('.tm-card__actions button')?.textContent).toBe('Add');
    expect(element.querySelector('.tm-card__body .field')).not.toBeNull();
    expect(element.querySelector('.tm-card__footer')?.textContent).toBe('Footer');
    // The count sits outside the heading, so it is not part of its name.
    expect(element.querySelector('.tm-card__heading tm-counter')).toBeNull();

    host.level.set(4);
    await fixture.whenStable();
    expect(await card.getHeadingLevel()).toBe(4);
  });

  it('has no heading element without a heading', async () => {
    const { card } = await setup((h) => h.heading.set(undefined));
    expect(await card.getHeading()).toBeNull();
    expect(await card.getHeadingLevel()).toBeNull();
    expect(await card.isCollapsible()).toBe(false);
  });

  it('a collapsible heading is a disclosure button controlling the body', async () => {
    const { fixture, host, card, element } = await setup((h) => h.collapsible.set(true));
    const toggle = element.querySelector<HTMLButtonElement>('h2 > button.tm-card__toggle')!;
    const region = element.querySelector('.tm-card__region')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.getAttribute('aria-controls')).toBe(region.id);
    expect(await card.isExpanded()).toBe(true);

    await card.toggle();
    expect(host.expanded()).toBe(false);
    expect(await card.isExpanded()).toBe(false);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    // Without preserveContent the body leaves the DOM.
    expect(await card.isBodyRendered()).toBe(false);
    expect(toggle.getAttribute('aria-controls')).toBeNull();

    host.expanded.set(true);
    await fixture.whenStable();
    expect(await card.isBodyRendered()).toBe(true);
  });

  it('preserveContent keeps a collapsed body in the DOM, hidden and inert', async () => {
    const { card, element } = await setup((h) => {
      h.collapsible.set(true);
      h.preserve.set(true);
      h.expanded.set(false);
    });
    expect(await card.isBodyRendered()).toBe(true);
    expect(await card.isBodyInert()).toBe(true);
    const field = element.querySelector<HTMLInputElement>('.field')!;
    expect(field.checkVisibility()).toBe(false);
    field.focus();
    expect(document.activeElement).not.toBe(field);

    await card.toggle();
    expect(await card.isBodyInert()).toBe(false);
    field.focus();
    expect(document.activeElement).toBe(field);
  });

  it('an invalid card names its heading "has errors" and paints the dot, collapsed or expanded', async () => {
    const { fixture, host, card, element } = await setup((h) => h.collapsible.set(true));
    expect(await card.reservesErrorDot()).toBe(true);
    expect(await card.isInvalid()).toBe(false);
    expect(await card.getHeadingName()).toBe('Lines');
    const dot = element.querySelector<HTMLElement>('.tm-card__dot')!;
    expect(getComputedStyle(dot).visibility).toBe('hidden');

    host.invalid.set(true);
    await fixture.whenStable();
    expect(await card.isInvalid()).toBe(true);
    expect(await card.getHeadingName()).toBe('Lines, has errors');
    expect(getComputedStyle(dot).visibility).toBe('visible');
    expect(dot.getAttribute('aria-hidden')).toBe('true');
    // The visible title is hidden from assistive technology, so the button
    // is named once, by the hidden error name.
    expect(element.querySelector('.tm-card__title')?.getAttribute('aria-hidden')).toBe('true');

    await card.toggle();
    expect(await card.isExpanded()).toBe(false);
    expect(await card.getHeadingName()).toBe('Lines, has errors');
    expect(getComputedStyle(dot).visibility).toBe('visible');
  });

  it('the dot coming and going moves nothing in the header', async () => {
    const { fixture, host, element } = await setup();
    const heading = element.querySelector<HTMLElement>('.tm-card__heading')!;
    const before = heading.getBoundingClientRect().width;
    host.invalid.set(true);
    await fixture.whenStable();
    expect(heading.getBoundingClientRect().width).toBe(before);
  });

  it('an unbound invalid reserves no dot box', async () => {
    const { card } = await setup((h) => h.bindInvalid.set(false));
    expect(await card.reservesErrorDot()).toBe(false);
    expect(await card.isInvalid()).toBe(false);
  });
});
