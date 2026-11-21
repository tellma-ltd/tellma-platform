// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Plus } from 'lucide';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { provideTellmaIcons } from '@tellma/core-ui/icon';
import { TmChipHarness } from '@tellma/core-ui-testing';

import { TmChip } from './tm-chip';

@Component({
  imports: [TmChip],
  template: `
    <tm-chip data-testid="plain" label="Service" />
    <tm-chip data-testid="md" label="Medium" size="md" />
    <tm-chip data-testid="lg" label="Large" size="lg" />
    <tm-chip data-testid="link" label="SAL-0042" href="/documents/42" tone="accent" />
    <tm-chip data-testid="button" label="Posted" size="sm" (activate)="activations = activations + 1" />
    <tm-chip
      data-testid="removable"
      label="Sale"
      tone="warning"
      icon="plus"
      [removable]="true"
      (remove)="removals = removals + 1"
    />
    <tm-chip data-testid="projected" label="Posted after 1 May" (activate)="0"
      >Posted after <b>1 May</b></tm-chip
    >
    @if (listening()) {
      <tm-chip data-testid="toggle" label="Toggle" (activate)="0" />
    } @else {
      <tm-chip data-testid="toggle" label="Toggle" />
    }
  `,
})
class Host {
  activations = 0;
  removals = 0;
  readonly listening = signal(false);
}

async function setup() {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), provideTellmaIcons({ plus: Plus })],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const chip = (id: string) =>
    loader.getHarness(
      TmChipHarness.with({}).add(
        id,
        async (harness) => (await (await harness.host()).getAttribute('data-testid')) === id,
      ),
    );
  return { fixture, host: fixture.componentInstance, chip };
}

/** A chip's host element by test id. */
function element(fixture: ComponentFixture<Host>, id: string): HTMLElement {
  return (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${id}"]`)!;
}

/** The unit page loads no token stylesheet; the chips' geometry needs one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

describe('tm-chip', () => {
  it('renders plain text in a bdi when nothing listens and there is no link', async () => {
    const { fixture, chip } = await setup();
    const plain = await chip('plain');
    expect(await plain.getKind()).toBe('text');
    expect(await plain.getLabel()).toBe('Service');
    expect(element(fixture, 'plain').querySelector('bdi.tm-chip__text')?.textContent?.trim()).toBe(
      'Service',
    );
    // Outside a set the chip is not a list item.
    expect(element(fixture, 'plain').getAttribute('role')).toBeNull();
    expect(await plain.getTone()).toBe('neutral');
  });

  it('renders a link named by its label when href is set', async () => {
    const { chip } = await setup();
    const link = await chip('link');
    expect(await link.getKind()).toBe('link');
    expect(await link.getHref()).toBe('/documents/42');
    expect(await link.getLabel()).toBe('SAL-0042');
    expect(await link.getTone()).toBe('accent');
  });

  it('renders a button that emits activate once a listener is bound', async () => {
    const { host, chip } = await setup();
    const button = await chip('button');
    expect(await button.getKind()).toBe('button');
    await button.activate();
    expect(host.activations).toBe(1);
    expect(await button.getSize()).toBe('sm');
  });

  it('turns the label into a button when a listener arrives and back when it goes', async () => {
    const { fixture, host, chip } = await setup();
    expect(await (await chip('toggle')).getKind()).toBe('text');
    host.listening.set(true);
    fixture.detectChanges();
    expect(await (await chip('toggle')).getKind()).toBe('button');
    host.listening.set(false);
    fixture.detectChanges();
    expect(await (await chip('toggle')).getKind()).toBe('text');
  });

  it('names the remove button "Remove {label}" and emits remove on a click', async () => {
    const { host, chip } = await setup();
    const removable = await chip('removable');
    expect(await removable.isRemovable()).toBe(true);
    expect(await removable.getRemoveLabel()).toBe('Remove Sale');
    await removable.remove();
    expect(host.removals).toBe(1);
  });

  it('emits remove on Delete or Backspace, but not with a modifier', async () => {
    const { fixture, host } = await setup();
    const remove = element(fixture, 'removable').querySelector<HTMLElement>('.tm-chip__remove')!;
    const press = (key: string, init: KeyboardEventInit = {}) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
      remove.dispatchEvent(event);
      return event;
    };
    expect(press('Delete').defaultPrevented).toBe(true);
    expect(press('Backspace').defaultPrevented).toBe(true);
    expect(host.removals).toBe(2);
    press('Delete', { ctrlKey: true });
    press('Backspace', { shiftKey: true });
    press('Enter');
    expect(host.removals).toBe(2);
  });

  it('ignores Delete on a chip that is not removable', async () => {
    const { fixture, host } = await setup();
    const button = element(fixture, 'button').querySelector<HTMLElement>('button')!;
    const event = new KeyboardEvent('keydown', { key: 'Delete', bubbles: true, cancelable: true });
    button.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
    expect(host.removals).toBe(0);
  });

  it('shows projected content while the label still names the chip', async () => {
    const { chip } = await setup();
    const projected = await chip('projected');
    expect(await projected.getText()).toBe('Posted after 1 May');
    expect(await projected.getLabel()).toBe('Posted after 1 May');
    expect(await projected.getKind()).toBe('button');
  });

  it('focuses the label button, else the remove button, else the link', async () => {
    const { fixture } = await setup();
    const chips = fixture.debugElement
      .queryAll((node) => node.componentInstance instanceof TmChip)
      .map((node) => node.componentInstance as TmChip);
    const byLabel = (label: string) => chips.find((c) => c.label() === label)!;

    byLabel('Posted').focus();
    expect(document.activeElement?.classList.contains('tm-chip__label')).toBe(true);
    byLabel('Sale').focus();
    expect(document.activeElement?.classList.contains('tm-chip__remove')).toBe(true);
    byLabel('SAL-0042').focus();
    expect(document.activeElement?.tagName).toBe('A');
    // A plain chip has nothing to focus.
    (document.activeElement as HTMLElement).blur();
    byLabel('Service').focus();
    expect(document.activeElement).toBe(document.body);
  });

  it('draws at the 20, 24 and 28 px chip steps', async () => {
    const { fixture } = await setup();
    expect(element(fixture, 'button').getBoundingClientRect().height).toBe(20);
    expect(element(fixture, 'md').getBoundingClientRect().height).toBe(24);
    expect(element(fixture, 'lg').getBoundingClientRect().height).toBe(28);
  });
});
