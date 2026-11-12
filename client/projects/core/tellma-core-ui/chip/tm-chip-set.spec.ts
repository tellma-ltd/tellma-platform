// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmChipSetHarness } from '@tellma/core-ui-testing';

import { TmChip } from './tm-chip';
import { TmChipSet, type TmChipSetOverflow } from './tm-chip-set';

@Component({
  imports: [TmChip, TmChipSet],
  template: `
    <div [style.inline-size.px]="width()" [attr.dir]="dir()">
      <tm-chip-set
        ariaLabel="Filters"
        [overflow]="overflow()"
        (exit)="exits.push($event)"
      >
        @for (label of labels(); track label) {
          <tm-chip
            [label]="label"
            [removable]="removable()"
            (activate)="activated.push(label)"
            (remove)="remove(label)"
          />
        }
      </tm-chip-set>
      <input data-testid="input" />
    </div>
  `,
})
class Host {
  readonly labels = signal(['Service', 'Sale', 'Operation', 'Purchase', 'Payroll']);
  readonly overflow = signal<TmChipSetOverflow>('wrap');
  readonly removable = signal(true);
  readonly width = signal(600);
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly exits: Array<'start' | 'end'> = [];
  readonly activated: string[] = [];

  remove(label: string): void {
    this.labels.set(this.labels().filter((candidate) => candidate !== label));
  }
}

async function setup(init: (host: Host) => void = () => undefined) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  init(fixture.componentInstance);
  fixture.autoDetectChanges();
  await settle(fixture);
  const set = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmChipSetHarness);
  return { fixture, host: fixture.componentInstance, set };
}

/** Two frames: layout, the resize observer and the render it schedules all run. */
async function settle(fixture: ComponentFixture<Host>): Promise<void> {
  for (let frame = 0; frame < 3; frame++) {
    await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
    await fixture.whenStable();
  }
}

/** The set's element. */
function setElement(fixture: ComponentFixture<Host>): HTMLElement {
  return (fixture.nativeElement as HTMLElement).querySelector('tm-chip-set')!;
}

/** The controls of the single tab stop, in order. */
function controls(fixture: ComponentFixture<Host>): HTMLElement[] {
  return Array.from(
    setElement(fixture).querySelectorAll<HTMLElement>(
      'tm-chip:not(.tm-chip--collapsed) :is(.tm-chip__label:is(a, button), .tm-chip__remove), .tm-chip-set__more:not(.tm-chip-set__more--idle) button',
    ),
  );
}

/** Presses a key on the focused element. */
function press(key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  document.activeElement!.dispatchEvent(event);
  return event;
}

/** The unit page loads no token stylesheet; the chips' geometry needs one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

describe('tm-chip-set', () => {
  it('is a named list of list items with exactly one tab stop', async () => {
    const { fixture, set } = await setup();
    expect(setElement(fixture).getAttribute('role')).toBe('list');
    expect(await set.getAriaLabel()).toBe('Filters');
    for (const chip of Array.from(setElement(fixture).querySelectorAll('tm-chip'))) {
      expect(chip.getAttribute('role')).toBe('listitem');
    }
    expect(await set.getTabStopCount()).toBe(1);
    expect(controls(fixture)[0].tabIndex).toBe(0);
    expect(controls(fixture).slice(1).every((control) => control.tabIndex === -1)).toBe(true);
  });

  it('moves with Left and Right, a label button before its remove button, and keeps the tab stop', async () => {
    const { fixture } = await setup();
    const all = controls(fixture);
    all[0].focus();
    press('ArrowRight');
    expect(document.activeElement).toBe(all[1]);
    expect(all[1].classList.contains('tm-chip__remove')).toBe(true);
    press('ArrowRight');
    expect(document.activeElement).toBe(all[2]);
    press('ArrowLeft');
    expect(document.activeElement).toBe(all[1]);
    expect(all[1].tabIndex).toBe(0);
    expect(all.filter((control) => control.tabIndex === 0)).toHaveLength(1);
  });

  it('swaps Left and Right in RTL, read live from the direction', async () => {
    const { fixture, host } = await setup();
    host.dir.set('rtl');
    await settle(fixture);
    const all = controls(fixture);
    all[0].focus();
    press('ArrowLeft');
    expect(document.activeElement).toBe(all[1]);
    press('ArrowRight');
    expect(document.activeElement).toBe(all[0]);
  });

  it('jumps with Home and End and ignores arrows with a modifier', async () => {
    const { fixture } = await setup();
    const all = controls(fixture);
    all[0].focus();
    press('End');
    expect(document.activeElement).toBe(all[all.length - 1]);
    press('Home');
    expect(document.activeElement).toBe(all[0]);
    const event = press('ArrowRight', { shiftKey: true });
    expect(event.defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(all[0]);
  });

  it('emits exit past either end', async () => {
    const { fixture, host } = await setup();
    const all = controls(fixture);
    all[0].focus();
    press('ArrowLeft');
    expect(host.exits).toEqual(['start']);
    all[all.length - 1].focus();
    press('ArrowRight');
    expect(host.exits).toEqual(['start', 'end']);
  });

  it('moves focus to the next chip when the focused one is removed, else the previous, else out', async () => {
    const { fixture, host } = await setup((h) => h.labels.set(['Service', 'Sale', 'Operation']));
    // Remove Sale from its remove button: Operation's control takes focus.
    controls(fixture)[3].focus();
    press('Delete');
    await settle(fixture);
    expect(host.labels()).toEqual(['Service', 'Operation']);
    expect(document.activeElement?.closest('tm-chip')?.textContent).toContain('Operation');

    // Remove the last chip: the previous one takes focus.
    press('Backspace');
    await settle(fixture);
    expect(host.labels()).toEqual(['Service']);
    expect(document.activeElement?.closest('tm-chip')?.textContent).toContain('Service');

    // Remove the only chip: the set emits exit('end').
    press('Delete');
    await settle(fixture);
    expect(host.labels()).toEqual([]);
    expect(host.exits).toEqual(['end']);
  });

  it('leaves focus alone when a chip that does not hold it goes', async () => {
    const { fixture, host } = await setup();
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('input')!;
    input.focus();
    host.remove('Sale');
    await settle(fixture);
    expect(document.activeElement).toBe(input);
    expect(host.exits).toEqual([]);
  });

  it('focusLast focuses the last chip, so a second Backspace removes it', async () => {
    const { fixture, host } = await setup();
    const chipSet = fixture.debugElement.query((node) => node.componentInstance instanceof TmChipSet)
      .componentInstance as TmChipSet;
    host.removable.set(false);
    await settle(fixture);
    chipSet.focusLast();
    expect(document.activeElement?.closest('tm-chip')?.textContent).toContain('Payroll');
    host.removable.set(true);
    await settle(fixture);
    chipSet.focusLast();
    press('Backspace');
    await settle(fixture);
    expect(host.labels()).not.toContain('Payroll');
  });

  it('collapses to one line behind a "+N" chip named after the hidden chips', async () => {
    const { fixture, set } = await setup((h) => {
      h.overflow.set('collapse');
      h.width.set(240);
    });
    const visible = await set.getVisibleChips();
    const all = await set.getChips();
    expect(visible.length).toBeGreaterThan(0);
    expect(visible.length).toBeLessThan(all.length);
    const hidden = all.length - visible.length;
    expect(await set.getMoreText()).toBe(`+${hidden}`);
    const hiddenLabels = [];
    for (const chip of all.slice(visible.length)) {
      hiddenLabels.push(await chip.getLabel());
    }
    expect(await set.getMoreLabel()).toBe(`${hidden} more: ${hiddenLabels.join(', ')}`);
    // One line: every shown chip and the "+N" chip share a top edge, inside the set.
    const box = setElement(fixture).getBoundingClientRect();
    const shown = Array.from(
      setElement(fixture).querySelectorAll<HTMLElement>('tm-chip:not(.tm-chip--collapsed), .tm-chip-set__more'),
    );
    const tops = new Set(shown.map((element) => Math.round(element.getBoundingClientRect().top)));
    expect(tops.size).toBe(1);
    for (const element of shown) {
      expect(element.getBoundingClientRect().right).toBeLessThanOrEqual(box.right + 0.5);
    }
    // The hidden chips' controls are out of the tab order and the arrow keys' reach.
    expect(controls(fixture).at(-1)?.closest('.tm-chip-set__more')).not.toBeNull();
  });

  it('shows every chip with no "+N" chip when they all fit', async () => {
    const { set } = await setup((h) => {
      h.overflow.set('collapse');
      h.width.set(1200);
    });
    expect((await set.getVisibleChips()).length).toBe(5);
    expect(await set.getMoreLabel()).toBeNull();
  });

  it('recomputes the collapse fit on resize, and never on a keystroke', async () => {
    const { fixture, host, set } = await setup((h) => {
      h.overflow.set('collapse');
      h.width.set(240);
    });
    const narrow = (await set.getVisibleChips()).length;

    // Typing in a field beside the set measures nothing.
    const measure = vi.spyOn(Element.prototype, 'getBoundingClientRect');
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    input.focus();
    for (const key of ['a', 'b', 'c']) {
      input.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
      input.value += key;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await settle(fixture);
    }
    expect(measure).not.toHaveBeenCalled();
    measure.mockRestore();

    // A wider set shows more chips.
    host.width.set(1200);
    await settle(fixture);
    expect((await set.getVisibleChips()).length).toBeGreaterThan(narrow);
  });

  it('keeps one scrolling line and scrolls the focused chip into view', async () => {
    const { fixture } = await setup((h) => {
      h.overflow.set('scroll');
      h.width.set(200);
    });
    const line = setElement(fixture);
    expect(line.scrollWidth).toBeGreaterThan(line.clientWidth);
    const tops = new Set(
      Array.from(line.querySelectorAll('tm-chip')).map((chip) =>
        Math.round(chip.getBoundingClientRect().top),
      ),
    );
    expect(tops.size).toBe(1);

    controls(fixture)[0].focus();
    press('End');
    const chip = document.activeElement!.closest('tm-chip')!.getBoundingClientRect();
    const box = line.getBoundingClientRect();
    expect(line.scrollLeft).toBeGreaterThan(0);
    expect(chip.right).toBeLessThanOrEqual(box.right + 0.5);
    expect(chip.left).toBeGreaterThanOrEqual(box.left - 0.5);

    press('Home');
    expect(line.scrollLeft).toBe(0);
  });

  it('activates a chip from its label button', async () => {
    const { fixture, host } = await setup();
    controls(fixture)[0].click();
    expect(host.activated).toEqual(['Service']);
  });
});
