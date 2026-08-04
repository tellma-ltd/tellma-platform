// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { TmButtonHarness } from '@tellma/core-ui-testing';

import { TmButton } from './tm-button';

@Component({
  imports: [TmButton],
  template: `
    <form (submit)="onSubmit($event)">
      <button
        tmButton
        data-testid="btn"
        [variant]="variant()"
        [size]="size()"
        [pending]="pending()"
        (click)="clicks = clicks + 1"
      >
        <svg viewBox="0 0 16 16" aria-hidden="true" (click)="iconClicks = iconClicks + 1"></svg>
        Save
      </button>
      <button tmButton type="submit" data-testid="submit-btn">Submit</button>
    </form>
  `,
})
class Host {
  readonly variant = signal<'primary' | 'secondary' | 'ghost' | 'danger'>('secondary');
  readonly size = signal<'sm' | 'md' | 'lg'>('md');
  readonly pending = signal(false);
  clicks = 0;
  iconClicks = 0;
  submits = 0;

  onSubmit(event: Event): void {
    event.preventDefault();
    this.submits += 1;
  }
}

function setup(): { fixture: ComponentFixture<Host>; host: Host; button: HTMLButtonElement } {
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const button = fixture.nativeElement.querySelector('[data-testid="btn"]') as HTMLButtonElement;
  return { fixture, host: fixture.componentInstance, button };
}

describe('tmButton', () => {
  it('defaults an unauthored type to "button" and preserves an authored one', () => {
    const { fixture, button } = setup();
    expect(button.type).toBe('button');
    const submit = fixture.nativeElement.querySelector(
      '[data-testid="submit-btn"]',
    ) as HTMLButtonElement;
    expect(submit.type).toBe('submit');
  });

  it('an untyped button inside a form never submits it', () => {
    const { host, button } = setup();
    button.click();
    expect(host.clicks).toBe(1);
    expect(host.submits).toBe(0);
  });

  it('reflects variant and size as host classes', () => {
    const { fixture, host, button } = setup();
    expect(button.classList).toContain('tm-button');
    expect(button.classList).toContain('tm-button--secondary');
    host.variant.set('danger');
    host.size.set('lg');
    fixture.detectChanges();
    expect(button.classList).toContain('tm-button--danger');
    expect(button.classList).toContain('tm-button--lg');
    expect(button.classList).not.toContain('tm-button--secondary');
  });

  describe('pending', () => {
    it('sets aria-busy and never disables (focus is retained)', () => {
      const { fixture, host, button } = setup();
      button.focus();
      host.pending.set(true);
      fixture.detectChanges();
      expect(button.getAttribute('aria-busy')).toBe('true');
      expect(button.disabled).toBe(false);
      expect(document.activeElement).toBe(button);
    });

    it('swallows activation before any consumer handler — host and projected children alike', () => {
      const { fixture, host, button } = setup();
      host.pending.set(true);
      fixture.detectChanges();

      // Direct activation (also what Enter/Space synthesize on a native button).
      button.click();
      expect(host.clicks).toBe(0);

      // A real click landing on projected content bubbles through the
      // capture-phase guard first.
      const icon = button.querySelector('svg')!;
      icon.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
      expect(host.clicks).toBe(0);
      expect(host.iconClicks).toBe(0);

      host.pending.set(false);
      fixture.detectChanges();
      button.click();
      expect(host.clicks).toBe(1);
    });

    it('overlays a spinner while pending and tears it down after', () => {
      const { fixture, host, button } = setup();
      expect(button.querySelector('.tm-button__spinner')).toBeNull();
      host.pending.set(true);
      fixture.detectChanges();
      const spinner = button.querySelector('.tm-button__spinner');
      expect(spinner).not.toBeNull();
      expect(spinner!.querySelector('svg')).not.toBeNull(); // rendered, not an empty host
      host.pending.set(false);
      fixture.detectChanges();
      expect(button.querySelector('.tm-button__spinner')).toBeNull();
    });
  });

  describe('icon-only dev warning', () => {
    it('warns when a button has neither text nor an accessible name', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      @Component({
        imports: [TmButton],
        template: `<button tmButton><svg viewBox="0 0 16 16" aria-hidden="true"></svg></button>`,
      })
      class IconOnly {}
      const fixture = TestBed.createComponent(IconOnly);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(warn).toHaveBeenCalledWith(
        expect.stringContaining('icon-only button without an accessible name'),
        expect.anything(),
      );
      warn.mockRestore();
    });

    it('stays silent when an aria-label is present', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      @Component({
        imports: [TmButton],
        template: `
          <button tmButton aria-label="Close"><svg viewBox="0 0 16 16" aria-hidden="true"></svg></button>
        `,
      })
      class Labeled {}
      const fixture = TestBed.createComponent(Labeled);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(warn).not.toHaveBeenCalled();
      warn.mockRestore();
    });
  });

  it('drives the button through TmButtonHarness', async () => {
    const { fixture, host } = setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    // Two buttons match the directive's selector; the first is the probe.
    const [button] = await loader.getAllHarnesses(TmButtonHarness);
    expect(await button.getText()).toBe('Save');
    expect(await button.getVariant()).toBe('secondary');
    expect(await button.getSize()).toBe('md');
    expect(await button.isPending()).toBe(false);
    expect(await button.isDisabled()).toBe(false);

    await button.focus();
    expect(await button.isFocused()).toBe(true);
    await button.click();
    expect(host.clicks).toBe(1);

    host.variant.set('danger');
    host.size.set('lg');
    host.pending.set(true);
    expect(await button.getVariant()).toBe('danger');
    expect(await button.getSize()).toBe('lg');
    expect(await button.isPending()).toBe(true);
    await button.click();
    expect(host.clicks).toBe(1); // swallowed while pending
  });
});
