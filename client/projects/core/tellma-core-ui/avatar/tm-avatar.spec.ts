// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { By } from '@angular/platform-browser';

import { provideTellmaUi, TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TM_BLOB_FETCHER, TmImage } from '@tellma/core-ui/image';
import { TmAvatarHarness } from '@tellma/core-ui-testing';
import { tmTokensDefault } from '@tellma/core-ui-tokens';

import { TmAvatar, type TmAvatarSize } from './tm-avatar';

@Component({
  imports: [TmAvatar],
  template: `
    <tm-avatar
      [name]="name()"
      [src]="src()"
      [colorKey]="colorKey()"
      [size]="size()"
      [decorative]="decorative()"
    />
  `,
})
class Host {
  readonly name = signal('Farah Nasser');
  readonly src = signal<string | null>(null);
  readonly colorKey = signal<string | null>(null);
  readonly size = signal<TmAvatarSize>('sm');
  readonly decorative = signal(false);
}

/** Every photo fetch fails, so a photo falls back to the initials. */
async function setup(locale = 'en-US') {
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: TM_ACTIVE_LOCALE, useValue: signal(locale) },
      {
        provide: TM_BLOB_FETCHER,
        useValue: () => Promise.reject(new Error('network down')),
      },
    ],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const avatar = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmAvatarHarness);
  return { fixture, host: fixture.componentInstance, avatar };
}

/** Polls until the predicate holds (the image pipeline settles asynchronously). */
async function until(fixture: ComponentFixture<unknown>, predicate: () => boolean): Promise<void> {
  const start = Date.now();
  while (!predicate()) {
    if (Date.now() - start > 15000) {
      throw new Error('condition not met in time');
    }
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

describe('tm-avatar', () => {
  it('shows the initials as an image named by the name, in the accent tone', async () => {
    const { avatar } = await setup();
    expect(await avatar.getInitials()).toBe('FN');
    expect(await avatar.getAccessibleName()).toBe('Farah Nasser');
    expect(await avatar.isDecorative()).toBe(false);
    expect(await avatar.getTone()).toBeNull();
    expect(await avatar.getSize()).toBe('sm');
    expect(await avatar.hasPhoto()).toBe(false);
  });

  it('takes the initials in the active locale', async () => {
    const { fixture, host, avatar } = await setup('ar-SA');
    host.name.set('فرح ناصر');
    fixture.detectChanges();
    expect(await avatar.getInitials()).toBe('ف');
  });

  it('picks the tone from the key, and the accent tone without one', async () => {
    const { fixture, host, avatar } = await setup();
    host.colorKey.set('core.User:17');
    fixture.detectChanges();
    expect(await avatar.getTone()).toBe(4);
    host.colorKey.set('core.User:18');
    fixture.detectChanges();
    expect(await avatar.getTone()).toBe(3);
    host.colorKey.set('');
    fixture.detectChanges();
    expect(await avatar.getTone()).toBeNull();
  });

  it('is hidden from assistive technology when decorative', async () => {
    const { fixture, host, avatar } = await setup();
    host.decorative.set(true);
    fixture.detectChanges();
    expect(await avatar.isDecorative()).toBe(true);
    expect(await avatar.getAccessibleName()).toBeNull();
  });

  it('shows the person glyph, unnamed, for a name with no letters', async () => {
    const { fixture, host, avatar } = await setup();
    host.name.set('  ');
    fixture.detectChanges();
    expect(await avatar.getInitials()).toBe('');
    expect(await avatar.isShowingGlyph()).toBe(true);
    expect(await avatar.isDecorative()).toBe(true);
  });

  it('falls back to the initials, silently, when the photo fails', async () => {
    const { fixture, host, avatar } = await setup();
    host.src.set('/img/avatar-spec-failing');
    fixture.detectChanges();
    expect(await avatar.hasPhoto()).toBe(true);
    const element = fixture.nativeElement as HTMLElement;
    await until(fixture, () => element.querySelector('.tm-image__fallback') !== null);
    expect(await avatar.getInitials()).toBe('FN');
    // The photo is unnamed: the avatar's own label is the only name read.
    const fallback = element.querySelector('.tm-image__fallback')!;
    expect(fallback.getAttribute('aria-hidden')).toBe('true');
    expect(element.querySelector('.tm-image__notice')?.textContent?.trim() ?? '').toBe('');
    expect(await avatar.getAccessibleName()).toBe('Farah Nasser');
  });

  it('requests the photo at the diameter the size tokens draw', async () => {
    const { fixture, host } = await setup();
    host.src.set('/img/avatar-spec-sizes');
    const sizes = tmTokensDefault.component['avatar'] as Record<string, string>;
    for (const [size, key] of [
      ['xs', 'sizeXs'],
      ['sm', 'sizeSm'],
      ['md', 'sizeMd'],
      ['lg', 'sizeLg'],
      ['xl', 'sizeXl'],
    ] as const) {
      host.size.set(size);
      fixture.detectChanges();
      const image = fixture.debugElement.query(By.directive(TmImage)).componentInstance as TmImage;
      expect(`${image.width()}px`).toBe(sizes[key]);
      expect(`${image.height()}px`).toBe(sizes[key]);
    }
  });
});
