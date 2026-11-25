// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, DestroyRef, EnvironmentInjector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TmBanners, TmShell } from '@tellma/core';

import { tmAppRig, tmNavigate, tmSettleUntil, tmTestMembership } from '../../../src/routes/tm-app.testing.util';
import { type TmShellRows, TmShellRowsWriter } from './tm-shell-frame';

// The shell frame on the signed-out page, the lightest page that renders
// it: the landmarks in order with the skip link first, the skip link's
// focus move, the row heights it writes on the root element and restores,
// the banner row, and the immersive frame without its header. Then the
// rows across a navigation between the two shells, and the writer's
// newest-frame-wins rule with two frames alive at once.

/** The root element's value of a row variable, or `''` while the stylesheet's default holds. */
function row(name: string): string {
  return document.documentElement.style.getPropertyValue(name);
}

/** The root element's three row variables, in order. */
function rows(): string[] {
  return ['--tm-app-bar-height', '--tm-banner-height', '--tm-pinned-bar-height'].map(row);
}

describe('the shell frame', () => {
  afterEach(() => TestBed.resetTestingModule());

  /** Starts the app on the signed-out page and waits for its chunk. */
  async function open(): Promise<void> {
    await tmAppRig();
    await tmNavigate('/signed-out');
    await tmSettleUntil(() => document.querySelector('tm-shell-frame main') !== null, 'the frame renders');
  }

  it('renders the skip link, the header, the one main and the hidden aside, in that order', async () => {
    await open();
    const frame = document.querySelector('tm-shell-frame')!;
    expect([...frame.children].map((child) => child.localName)).toEqual(['a', 'header', 'main', 'aside']);
    expect(document.querySelectorAll('main')).toHaveLength(1);
    expect(frame.querySelector('main')?.id).toBe('tm-main');
    expect(frame.querySelector('aside')?.hidden).toBe(true);
    // The skip link is the first thing the keyboard reaches.
    const first = document.querySelector<HTMLElement>('a[href], button, [tabindex="0"]');
    expect(first?.textContent?.trim()).toBe('Skip to content');
  });

  it('moves focus to main from the skip link, leaving the URL alone', async () => {
    await open();
    const url = document.location.href;
    document.querySelector<HTMLAnchorElement>('.tm-shell-frame__skip')!.click();
    expect(document.activeElement?.id).toBe('tm-main');
    expect(document.location.href).toBe(url);
  });

  it('writes the heights of its rows on the root element, and restores the defaults at its end', async () => {
    await open();
    expect(rows()).toEqual(['40px', '0px', '0px']);
    TestBed.resetTestingModule();
    expect(rows()).toEqual(['', '', '']);
  });

  it("writes the tenant shell's rows after a navigation from the picker, and the picker's again on the way back", async () => {
    await tmAppRig({ tenants: [tmTestMembership(17, { name: 'Acme' })] });
    await tmNavigate('/tenants');
    await tmSettleUntil(() => document.querySelector('tm-tenant-picker tm-shell-frame') !== null, 'the picker renders');
    expect(rows()).toEqual(['40px', '0px', '0px']);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/17');
    await tmSettleUntil(() => document.querySelector('tm-tenant-shell tm-shell-frame tm-app-bar') !== null, 'the tenant shell renders its app bar');
    // The tenant shell's frame draws its app bar's row too, so the rows hold across the switch.
    await tmSettleUntil(() => rows().join() === '40px,0px,0px', "the tenant shell's rows are written");
    await router.navigateByUrl('/tenants');
    await tmSettleUntil(() => document.querySelector('tm-tenant-shell') === null, 'the tenant shell ends');
    await tmSettleUntil(() => rows().join() === '40px,0px,0px', "the picker's rows are written again");
  });

  describe('the rows writer', () => {
    /** A frame's rows, with the app bar's height given. */
    const frame = (appBar: number): TmShellRows => ({ '--tm-app-bar-height': appBar, '--tm-banner-height': 0, '--tm-pinned-bar-height': 0 });

    /** Holds two frames' rows, the second the newer, each until its own lifetime ends. */
    function hold(): { readonly older: EnvironmentInjector; readonly newer: EnvironmentInjector } {
      const writer = TestBed.inject(TmShellRowsWriter);
      const parent = TestBed.inject(EnvironmentInjector);
      const older = createEnvironmentInjector([], parent);
      const newer = createEnvironmentInjector([], parent);
      writer.hold(signal(frame(40)), older.get(DestroyRef));
      writer.hold(signal(frame(0)), newer.get(DestroyRef));
      TestBed.tick();
      return { older, newer };
    }

    it("shows the newer frame's rows, and keeps them when the older frame ends first", () => {
      const { older, newer } = hold();
      expect(rows()).toEqual(['0px', '0px', '0px']);
      older.destroy();
      TestBed.tick();
      expect(rows()).toEqual(['0px', '0px', '0px']);
      newer.destroy();
      TestBed.tick();
      expect(rows()).toEqual(['', '', '']);
    });

    it("falls back to the older frame's rows when the newer frame ends first", () => {
      const { older, newer } = hold();
      newer.destroy();
      TestBed.tick();
      expect(rows()).toEqual(['40px', '0px', '0px']);
      older.destroy();
      TestBed.tick();
      expect(rows()).toEqual(['', '', '']);
    });
  });

  it('opens the banner row while a banner shows', async () => {
    await open();
    TestBed.inject(TmBanners).show({ id: 'acme.notice', level: 5, tone: 'info', message: signal('A notice'), dismissible: true });
    // The row opens at the next idle moment.
    await tmSettleUntil(() => row('--tm-banner-height') === '32px', 'the banner row opens');
  });

  it('renders no header while immersive', async () => {
    await open();
    const holder = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
    TestBed.inject(TmShell).setImmersive(true, holder.get(DestroyRef));
    await tmSettleUntil(() => document.querySelector('tm-shell-frame header') === null, 'the header leaves');
    holder.destroy();
    await tmSettleUntil(() => document.querySelector('tm-shell-frame header') !== null, 'the header returns');
  });
});
