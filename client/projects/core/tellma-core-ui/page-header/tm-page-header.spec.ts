// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

import { TmPageHeader } from './tm-page-header';

@Component({
  imports: [TmPageHeader],
  template: `
    <div [style.width.px]="width()">
      <tm-page-header>
        @if (back()) {
          <a tmPageBack href="/centers">Centers</a>
        }
        <h1 tmPageTitle>Jeddah sales</h1>
        @if (showFirstEnd()) {
          <button type="button" tmPageHeaderEnd class="first-end">Share</button>
        }
        <button type="button" tmPageHeaderEnd class="last-end">More</button>
        @if (toolbar()) {
          <button type="button" tmPageToolbar>Edit</button>
        }
        @if (band()) {
          <span tmPageHeaderBand>Period: January</span>
        }
      </tm-page-header>
    </div>
  `,
})
class Host {
  readonly width = signal(900);
  readonly back = signal(true);
  readonly showFirstEnd = signal(true);
  readonly toolbar = signal(true);
  readonly band = signal(true);
}

/** The unit page loads no token stylesheet; the paddings need one. */
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
  const root = fixture.nativeElement as HTMLElement;
  const shown = (selector: string): boolean =>
    getComputedStyle(root.querySelector(selector)!).display !== 'none';
  return { fixture, host: fixture.componentInstance, root, shown };
}

describe('tm-page-header', () => {
  it('projects each slot into its row, the separator hidden from assistive technology', async () => {
    const { root } = await setup();
    expect(root.querySelector('.tm-page-header__back [tmPageBack]')).not.toBeNull();
    expect(root.querySelector('.tm-page-header__separator')?.getAttribute('aria-hidden')).toBe(
      'true',
    );
    expect(root.querySelector('.tm-page-header__title-row > h1')).not.toBeNull();
    expect(
      root.querySelector('.tm-page-header__title-row .tm-page-header__end .last-end'),
    ).not.toBeNull();
    expect(root.querySelector('.tm-page-header__toolbar-start [tmPageToolbar]')).not.toBeNull();
    expect(root.querySelector('.tm-page-header__band [tmPageHeaderBand]')).not.toBeNull();
    // No roles are imposed.
    expect(root.querySelector('tm-page-header [role]')).toBeNull();
  });

  it('leaves out the crumb, the toolbar row and the band while they are empty', async () => {
    const { shown } = await setup((h) => {
      h.back.set(false);
      h.toolbar.set(false);
      h.band.set(false);
    });
    expect(shown('.tm-page-header__back')).toBe(false);
    expect(shown('.tm-page-header__toolbar')).toBe(false);
    expect(shown('.tm-page-header__band')).toBe(false);
    expect(shown('.tm-page-header__title-row')).toBe(true);
  });

  it('pads 12x14 px, and 10 px while narrower than 576 px', async () => {
    const { fixture, host, root } = await setup();
    const inner = root.querySelector('.tm-page-header__inner')!;
    expect(getComputedStyle(inner).paddingBlockStart).toBe('12px');
    expect(getComputedStyle(inner).paddingInlineStart).toBe('14px');
    host.width.set(500);
    await fixture.whenStable();
    expect(getComputedStyle(inner).paddingBlockStart).toBe('10px');
    expect(getComputedStyle(inner).paddingInlineStart).toBe('10px');
  });

  it('packs the end slot against the inline end: hiding its first item moves nothing', async () => {
    const { fixture, host, root } = await setup();
    const last = (): number => root.querySelector('.last-end')!.getBoundingClientRect().left;
    const before = last();
    host.showFirstEnd.set(false);
    await fixture.whenStable();
    expect(last()).toBe(before);
  });
});
