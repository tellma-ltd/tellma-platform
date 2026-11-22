// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, DestroyRef, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TM_SHELL_LAYOUT, TmShell } from './tm-shell';

/** A lifetime the test ends, as a page's would. */
function lifetime(): EnvironmentInjector {
  return createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
}

/** Whether the root element carries the immersive attribute. */
function rootImmersive(): boolean {
  return document.documentElement.hasAttribute('data-tm-immersive');
}

describe('TmShell', () => {
  afterEach(() => document.documentElement.removeAttribute('data-tm-immersive'));

  it('starts with the rows shown', () => {
    const shell = TestBed.inject(TmShell);
    expect(shell.immersive()).toBe(false);
    expect(rootImmersive()).toBe(false);
  });

  it('takes the rows away while a caller holds it, until the caller gives them back', () => {
    const shell = TestBed.inject(TmShell);
    const page = lifetime().get(DestroyRef);

    shell.setImmersive(true, page);
    expect(shell.immersive()).toBe(true);
    expect(rootImmersive()).toBe(true);

    shell.setImmersive(false, page);
    expect(shell.immersive()).toBe(false);
    expect(rootImmersive()).toBe(false);
  });

  it("gives the rows back when the caller's lifetime ends", () => {
    const shell = TestBed.inject(TmShell);
    const page = lifetime();
    shell.setImmersive(true, page.get(DestroyRef));

    page.destroy();
    expect(shell.immersive()).toBe(false);
    expect(rootImmersive()).toBe(false);
  });

  it('stays immersive while any caller holds it, and counts a repeated call once', () => {
    const shell = TestBed.inject(TmShell);
    const first = lifetime();
    const second = lifetime();
    shell.setImmersive(true, first.get(DestroyRef));
    shell.setImmersive(true, first.get(DestroyRef));
    shell.setImmersive(true, second.get(DestroyRef));

    shell.setImmersive(false, first.get(DestroyRef));
    expect(shell.immersive()).toBe(true);
    second.destroy();
    expect(shell.immersive()).toBe(false);

    // A release with nothing held, and the end of a lifetime already released, change nothing.
    shell.setImmersive(false, first.get(DestroyRef));
    first.destroy();
    expect(shell.immersive()).toBe(false);
    expect(rootImmersive()).toBe(false);
  });

  it('restores the frame when the app ends', () => {
    TestBed.inject(TmShell).setImmersive(true, lifetime().get(DestroyRef));
    TestBed.resetTestingModule();
    expect(rootImmersive()).toBe(false);
  });

  it("moves the frame's variables to zero through the attribute, and mirrors the rows' heights", () => {
    // The frame stylesheet is global, so the spec loads the rules it tests.
    const style = document.createElement('style');
    style.textContent =
      ':root{--tm-app-bar-height:40px;--tm-banner-height:0px;--tm-pinned-bar-height:32px;' +
      '--tm-shell-top:calc(var(--tm-app-bar-height) + var(--tm-banner-height) + var(--tm-pinned-bar-height))}' +
      ':root[data-tm-immersive]{--tm-app-bar-height:0px!important;--tm-banner-height:0px!important;--tm-pinned-bar-height:0px!important}';
    document.head.append(style);
    const probe = document.createElement('div');
    probe.style.blockSize = 'var(--tm-shell-top)';
    document.body.append(probe);
    try {
      expect(probe.getBoundingClientRect().height).toBe(TM_SHELL_LAYOUT.appBar + TM_SHELL_LAYOUT.pinnedBar);

      // What the shell writes inline gives way to immersive mode.
      document.documentElement.style.setProperty('--tm-banner-height', `${TM_SHELL_LAYOUT.banner}px`);
      expect(probe.getBoundingClientRect().height).toBe(
        TM_SHELL_LAYOUT.appBar + TM_SHELL_LAYOUT.banner + TM_SHELL_LAYOUT.pinnedBar,
      );
      TestBed.inject(TmShell).setImmersive(true, lifetime().get(DestroyRef));
      expect(probe.getBoundingClientRect().height).toBe(0);
    } finally {
      document.documentElement.style.removeProperty('--tm-banner-height');
      style.remove();
      probe.remove();
    }
  });
});
