// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, DestroyRef, EnvironmentInjector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmBanners, TmShell } from '@tellma/core';

import { tmAppRig, tmNavigate, tmSettleUntil } from '../../../src/routes/tm-app.testing.util';

// The shell frame on the signed-out page, the lightest page that renders
// it: the landmarks in order with the skip link first, the skip link's
// focus move, the row heights it writes on the root element and restores,
// the banner row, and the immersive frame without its header.

/** The root element's value of a row variable, or `''` while the stylesheet's default holds. */
function row(name: string): string {
  return document.documentElement.style.getPropertyValue(name);
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
    expect(['--tm-app-bar-height', '--tm-banner-height', '--tm-pinned-bar-height'].map(row)).toEqual(['40px', '0px', '0px']);
    TestBed.resetTestingModule();
    expect(['--tm-app-bar-height', '--tm-banner-height', '--tm-pinned-bar-height'].map(row)).toEqual(['', '', '']);
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
