// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { disabled, form, FormField, readonly, validate } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmSwitchHarness } from '@tellma/core-ui-testing';

import { TmSwitch } from './tm-switch';

/** A switch bound to a Signal Forms field. */
@Component({
  imports: [FormField, TmSwitch],
  // Unit tests load no token sheet: the track's geometry is set inline.
  template: `
    <tm-switch
      [formField]="prefs.notify"
      [labelPosition]="position()"
      style="--switch-track-width: 28px; --switch-track-height: 16px; --switch-knob-size: 12px; --switch-gap: 8px"
      >Notify me</tm-switch
    >
  `,
})
class Host {
  readonly position = signal<'start' | 'end'>('end');
  readonly locked = signal(false);
  readonly off = signal(false);
  readonly mustBeOn = signal(false);
  readonly model = signal({ notify: false });
  readonly prefs = form(this.model, (path) => {
    readonly(path.notify, () => this.locked());
    disabled(path.notify, () => this.off());
    validate(path.notify, (ctx) =>
      this.mustBeOn() && !ctx.value() ? { kind: 'required', message: 'Turn it on' } : undefined,
    );
  });
}

async function setup() {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return { fixture, switcher: await loader.getHarness(TmSwitchHarness) };
}

describe('tm-switch', () => {
  it('binds `checked` to a Signal Forms field both ways', async () => {
    const { fixture, switcher } = await setup();
    expect(await switcher.isChecked()).toBe(false);
    await switcher.toggle();
    expect(fixture.componentInstance.model().notify).toBe(true);
    fixture.componentInstance.model.set({ notify: false });
    await fixture.whenStable();
    expect(await switcher.isChecked()).toBe(false);
  });

  it('is a native checkbox with the switch role and its label as the name', async () => {
    const { fixture, switcher } = await setup();
    const native = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(native.type).toBe('checkbox');
    expect(native.getAttribute('role')).toBe('switch');
    expect(native.labels?.[0]?.textContent?.trim()).toBe('Notify me');
    expect(await switcher.getLabelText()).toBe('Notify me');
  });

  it('never toggles while readonly', async () => {
    const { fixture, switcher } = await setup();
    fixture.componentInstance.locked.set(true);
    await fixture.whenStable();
    await switcher.toggle();
    expect(fixture.componentInstance.model().notify).toBe(false);
    expect(await switcher.isChecked()).toBe(false);
    const native = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(native.getAttribute('aria-readonly')).toBe('true');
  });

  it('reflects a bound disabled state', async () => {
    const { fixture, switcher } = await setup();
    fixture.componentInstance.off.set(true);
    await fixture.whenStable();
    expect(await switcher.isDisabled()).toBe(true);
  });

  it('marks itself invalid once touched', async () => {
    const { fixture, switcher } = await setup();
    fixture.componentInstance.mustBeOn.set(true);
    await fixture.whenStable();
    expect(await switcher.isInvalid()).toBe(false);
    await switcher.focus();
    await switcher.blur();
    await fixture.whenStable();
    expect(await switcher.isInvalid()).toBe(true);
  });

  it('puts the label on the start side on request', async () => {
    const { fixture } = await setup();
    const host = fixture.nativeElement.querySelector('tm-switch') as HTMLElement;
    const track = host.querySelector('.tm-switch__track') as HTMLElement;
    const label = host.querySelector('.tm-switch__label') as HTMLElement;
    expect(label.getBoundingClientRect().left).toBeGreaterThan(track.getBoundingClientRect().left);
    fixture.componentInstance.position.set('start');
    await fixture.whenStable();
    expect(label.getBoundingClientRect().left).toBeLessThan(track.getBoundingClientRect().left);
  });
});
