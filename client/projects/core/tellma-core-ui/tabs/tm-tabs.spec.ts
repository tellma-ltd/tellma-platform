// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal, type OnDestroy } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmTabGroupHarness } from '@tellma/core-ui-testing';

import { TmTab, TmTabContent, TmTabLabel } from './tm-tab';
import { TmTabGroup } from './tm-tab-group';

/** A destruction probe: counts live instances of the projected content. */
let liveProbes = 0;

@Component({
  selector: 'tm-test-probe',
  template: `probe`,
})
class Probe implements OnDestroy {
  constructor() {
    liveProbes += 1;
  }
  ngOnDestroy(): void {
    liveProbes -= 1;
  }
}

@Component({
  imports: [TmTabGroup, TmTab, TmTabContent, TmTabLabel, Probe],
  template: `
    <tm-tab-group
      [(selectedId)]="selected"
      [selectionMode]="mode()"
      data-testid="group"
    >
      <tm-tab id="one" label="One">
        <ng-template tmTabContent><p class="content-one">First content</p></ng-template>
      </tm-tab>
      <tm-tab id="two" label="Two" [preserveContent]="preserve()">
        <ng-template tmTabContent>
          <tm-test-probe />
          <input class="content-two-input" />
        </ng-template>
      </tm-tab>
      <tm-tab id="three" [disabled]="thirdDisabled()">
        <ng-template tmTabLabel><strong class="rich">Three!</strong></ng-template>
        <ng-template tmTabContent><p class="content-three">Third content</p></ng-template>
      </tm-tab>
    </tm-tab-group>
  `,
})
class Host {
  readonly selected = signal<string | undefined>(undefined);
  readonly mode = signal<'follow' | 'explicit'>('follow');
  readonly preserve = signal(false);
  readonly thirdDisabled = signal(false);
}

async function setup(): Promise<{ fixture: ComponentFixture<Host>; host: Host; root: HTMLElement }> {
  liveProbes = 0;
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  return { fixture, host: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
}

function visiblePanels(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>('[role="tabpanel"]')).filter(
    (panel) => !panel.inert,
  );
}

describe('tm-tab-group + tm-tab', () => {
  it('renders the aria pattern from the definitions (no NG0201) with rich labels', async () => {
    const { root } = await setup();
    const tabs = root.querySelectorAll('[role="tab"]');
    expect(tabs).toHaveLength(3);
    expect(root.querySelector('[role="tablist"]')).not.toBeNull();
    expect(tabs[0].textContent).toContain('One');
    expect(tabs[2].querySelector('strong.rich')?.textContent).toBe('Three!');
  });

  it('defaults the selection to the first enabled tab WITHOUT writing the model', async () => {
    const { host, root } = await setup();
    const tabs = root.querySelectorAll('[role="tab"]');
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    expect(host.selected()).toBeUndefined(); // display-level defaulting only
    expect(root.querySelector('.content-one')).not.toBeNull();
  });

  it('only the active panel content is in the DOM; deactivation destroys it', async () => {
    const { fixture, host, root } = await setup();
    expect(root.querySelector('.content-one')).not.toBeNull();
    expect(root.querySelector('.content-two-input')).toBeNull();
    expect(liveProbes).toBe(0);

    (root.querySelectorAll('[role="tab"]')[1] as HTMLElement).click();
    await fixture.whenStable();
    expect(host.selected()).toBe('two');
    expect(liveProbes).toBe(1);
    expect(root.querySelector('.content-one')).toBeNull(); // destroyed
    expect(visiblePanels(root)).toHaveLength(1);

    (root.querySelectorAll('[role="tab"]')[0] as HTMLElement).click();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0)); // deferred teardown tick
    await fixture.whenStable();
    expect(liveProbes).toBe(0); // tab two's content destroyed on deactivate
    expect(root.querySelector('.content-two-input')).toBeNull();
  });

  it('preserveContent keeps the activated panel alive, hidden and inert', async () => {
    const { fixture, host, root } = await setup();
    host.preserve.set(true);
    await fixture.whenStable();

    (root.querySelectorAll('[role="tab"]')[1] as HTMLElement).click();
    await fixture.whenStable();
    const input = root.querySelector<HTMLInputElement>('.content-two-input')!;
    input.value = 'typed state';

    (root.querySelectorAll('[role="tab"]')[0] as HTMLElement).click();
    await fixture.whenStable();
    // The DOM survived, inert-hidden — transient state included.
    const kept = root.querySelector<HTMLInputElement>('.content-two-input');
    expect(kept).not.toBeNull();
    expect(kept!.value).toBe('typed state');
    expect(liveProbes).toBe(1);
    const panel = kept!.closest('[role="tabpanel"]') as HTMLElement;
    expect(panel.inert).toBe(true);
    expect(visiblePanels(root)).toHaveLength(1);
  });

  it('a disabled tab never activates; an id pointing at it falls back visibly', async () => {
    const { fixture, host, root } = await setup();
    host.thirdDisabled.set(true);
    host.selected.set('three');
    await fixture.whenStable();
    const tabs = root.querySelectorAll('[role="tab"]');
    expect(tabs[2].getAttribute('aria-selected')).toBe('false');
    expect(tabs[0].getAttribute('aria-selected')).toBe('true'); // fallback
    expect(host.selected()).toBe('three'); // consumer state untouched
  });

  it('two-way selectedId follows external writes', async () => {
    const { fixture, host, root } = await setup();
    host.selected.set('two');
    await fixture.whenStable();
    expect(root.querySelectorAll('[role="tab"]')[1].getAttribute('aria-selected')).toBe('true');
    expect(root.querySelector('.content-two-input')).not.toBeNull();
  });

  it('drives the strip through TmTabGroupHarness', async () => {
    const { fixture, host } = await setup();
    host.thirdDisabled.set(true);
    await fixture.whenStable();
    const group = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmTabGroupHarness);

    const tabs = await group.getTabs();
    expect(await Promise.all(tabs.map((tab) => tab.getLabel()))).toEqual(['One', 'Two', 'Three!']);
    expect(await tabs[0].isSelected()).toBe(true);
    expect(await tabs[2].isDisabled()).toBe(true);
    expect(await tabs[0].isDisabled()).toBe(false);
    expect(await (await group.getSelectedTab())?.getLabel()).toBe('One');

    await tabs[1].select();
    expect(host.selected()).toBe('two');
    expect(await tabs[1].isSelected()).toBe(true);
    expect(await (await group.getSelectedTab())?.getLabel()).toBe('Two');

    await group.selectTab('One');
    expect(await group.getActivePanelText()).toBe('First content');
  });
});
