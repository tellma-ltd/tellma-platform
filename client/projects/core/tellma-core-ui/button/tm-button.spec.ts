// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { HarnessPredicate } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmButtonHarness, TmTooltipHarness } from '@tellma/core-ui-testing';
import { TmTooltip } from '@tellma/core-ui/tooltip';

import { TmButton } from './tm-button';

/** Buttons disabled with a reason, a link, and a reason beside a tmTooltip. */
@Component({
  imports: [TmButton, TmTooltip],
  template: `
    <form (submit)="onSubmit($event)">
      <button
        tmButton
        type="submit"
        data-testid="reason"
        style="--tooltip-delay: 0ms"
        [disabledReason]="reason()"
        (click)="clicks = clicks + 1"
      >
        Edit
      </button>
    </form>
    <a tmButton data-testid="link" href="#target" variant="primary" size="sm">Open</a>
    <button
      tmButton
      data-testid="with-tooltip"
      aria-label="Delete"
      tmTooltip="Delete"
      style="--tooltip-delay: 0ms"
      [disabledReason]="reason()"
    >
      <svg viewBox="0 0 16 16" aria-hidden="true"></svg>
    </button>
  `,
})
class ReasonHost {
  readonly reason = signal<string | null>('Needs the Customers permission.');
  clicks = 0;
  submits = 0;

  onSubmit(event: Event): void {
    event.preventDefault();
    this.submits += 1;
  }
}

async function setupReason(): Promise<{
  fixture: ComponentFixture<ReasonHost>;
  host: ReasonHost;
  get: (id: string) => HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(ReasonHost);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    host: fixture.componentInstance,
    get: (id) => root.querySelector(`[data-testid="${id}"]`) as HTMLElement,
  };
}

/** The texts of the host's descriptions. */
function descriptions(element: Element): string[] {
  return (element.getAttribute('aria-describedby') ?? '')
    .split(' ')
    .filter((id) => id !== '')
    .map((id) => document.getElementById(id)?.textContent?.trim() ?? '');
}

/** The visible tooltip surface's text, or null. */
function tooltipText(): string | null {
  return document.querySelector('.tm-tooltip__panel')?.textContent?.trim() ?? null;
}

async function tick(fixture: ComponentFixture<unknown>, ms = 0): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
  await fixture.whenStable();
}

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

describe('tmButton disabledReason', () => {
  it('renders aria-disabled, keeps focus and stays out of the native disabled state', async () => {
    const { get } = await setupReason();
    const button = get('reason') as HTMLButtonElement;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.disabled).toBe(false);
    button.focus();
    expect(document.activeElement).toBe(button);
  });

  it('swallows click, Enter and Space activation, submit included', async () => {
    const { host, get } = await setupReason();
    const button = get('reason') as HTMLButtonElement;
    button.click();
    // Enter and Space reach a native button as a synthesized click.
    button.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    expect(host.clicks).toBe(0);
    expect(host.submits).toBe(0);
  });

  it('describes the button with the reason, and shows it as a tooltip on hover', async () => {
    const { fixture, get } = await setupReason();
    const button = get('reason');
    expect(descriptions(button)).toEqual(['Needs the Customers permission.']);
    button.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
    await tick(fixture);
    expect(tooltipText()).toBe('Needs the Customers permission.');
  });

  it('shows the reason on a touch tap, and hides it on the next tap elsewhere', async () => {
    const { fixture, get } = await setupReason();
    const button = get('reason');
    const init = { pointerType: 'touch', isPrimary: true, pointerId: 3, bubbles: true };
    button.dispatchEvent(new PointerEvent('pointerdown', init));
    button.dispatchEvent(new PointerEvent('pointerup', init));
    await tick(fixture);
    expect(tooltipText()).toBe('Needs the Customers permission.');

    document.body.dispatchEvent(new PointerEvent('pointerdown', { ...init, pointerId: 4 }));
    await tick(fixture);
    expect(tooltipText()).toBeNull();
  });

  it('clearing the reason restores activation and removes the description', async () => {
    const { fixture, host, get } = await setupReason();
    const button = get('reason') as HTMLButtonElement;
    host.reason.set(null);
    await fixture.whenStable();
    expect(button.hasAttribute('aria-disabled')).toBe(false);
    expect(descriptions(button)).toEqual([]);
    expect(button.classList.contains('tm-tooltip-host')).toBe(false);
    button.click();
    expect(host.clicks).toBe(1);
    expect(host.submits).toBe(1);
  });

  it('a blank reason disables nothing', async () => {
    const { fixture, host, get } = await setupReason();
    host.reason.set('   ');
    await fixture.whenStable();
    expect(get('reason').hasAttribute('aria-disabled')).toBe(false);
  });

  it('a tmTooltip on the same button shows the reason in place of its text', async () => {
    const { fixture, host, get } = await setupReason();
    const button = get('with-tooltip');
    expect(descriptions(button)).toEqual(['Needs the Customers permission.']);
    button.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
    await tick(fixture);
    expect(tooltipText()).toBe('Needs the Customers permission.');

    host.reason.set(null);
    await fixture.whenStable();
    expect(tooltipText()).toBe('Delete');
  });

  it('reads the reason through TmButtonHarness and the tooltip harness', async () => {
    const { fixture } = await setupReason();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const reason = await loader.getHarness(
      new HarnessPredicate(TmButtonHarness, { selector: '[data-testid="reason"]' }),
    );
    expect(await reason.getDisabledReason()).toBe('Needs the Customers permission.');
    expect(await reason.isDisabled()).toBe(false);
    const tooltip = await loader.getHarness(
      new HarnessPredicate(TmTooltipHarness, { selector: '[data-testid="reason"]' }),
    );
    expect(await tooltip.getDescription()).toBe('Needs the Customers permission.');

    const link = await loader.getHarness(
      new HarnessPredicate(TmButtonHarness, { selector: '[data-testid="link"]' }),
    );
    expect(await link.getDisabledReason()).toBeNull();
    expect(await link.isLink()).toBe(true);
  });
});

describe('a[tmButton]', () => {
  it('applies the variant and size and nothing else', async () => {
    const { fixture, get } = await setupReason();
    const link = get('link') as HTMLAnchorElement;
    expect(link.classList).toContain('tm-button');
    expect(link.classList).toContain('tm-button--primary');
    expect(link.classList).toContain('tm-button--sm');
    expect(link.hasAttribute('type')).toBe(false);
    const harness = await TestbedHarnessEnvironment.loader(fixture).getHarness(
      new HarnessPredicate(TmButtonHarness, { selector: '[data-testid="link"]' }),
    );
    expect(await harness.getVariant()).toBe('primary');
    expect(await harness.getSize()).toBe('sm');
    expect(await harness.isDisabled()).toBe(false);
  });

  it('ignores pending and a reason, and stays a link', async () => {
    @Component({
      imports: [TmButton],
      template: `<a tmButton href="#x" [pending]="true" [disabledReason]="'Nope'">Go</a>`,
    })
    class PendingLinkHost {}
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      const fixture = TestBed.createComponent(PendingLinkHost);
      await fixture.whenStable();
      const link = (fixture.nativeElement as HTMLElement).querySelector('a') as HTMLAnchorElement;
      expect(link.hasAttribute('aria-busy')).toBe(false);
      expect(link.hasAttribute('aria-disabled')).toBe(false);
      expect(link.classList.contains('tm-button--pending')).toBe(false);
      expect(link.querySelector('.tm-button__spinner')).toBeNull();
      // A link cannot be disabled: the reason is a development error.
      expect(error.mock.calls.some(([message]) => String(message).includes('link'))).toBe(true);
    } finally {
      error.mockRestore();
    }
  });
});

describe('tmButton dev-mode reason checks', () => {
  it('warns when disabled and a reason are both set', async () => {
    @Component({
      imports: [TmButton],
      template: `<button tmButton disabled [disabledReason]="'No permission'">Edit</button>`,
    })
    class BothHost {}
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const fixture = TestBed.createComponent(BothHost);
      await fixture.whenStable();
      expect(
        warn.mock.calls.some(([message]) => String(message).includes('disabledReason')),
      ).toBe(true);
    } finally {
      warn.mockRestore();
    }
  });
});
