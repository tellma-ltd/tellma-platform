// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { email, form, FormField, minLength, required } from '@angular/forms/signals';

import {
  provideTellmaUi,
  TM_FONT_SUBSETS,
  TM_UI_MESSAGE_CONTEXT,
  TM_UI_TRANSLATE,
} from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';

import { TM_FONTS_ARABIC } from './font-manifest.generated';
import { provideTellmaLocaleAr } from './provide-tellma-locale-ar';

@Component({
  imports: [TmInput, TmFormField, FormField],
  template: `
    <tm-form-field label="Email">
      <input tmInput [formField]="f.email" />
    </tm-form-field>
  `,
})
class Host {
  readonly translate = inject(TM_UI_TRANSLATE);
  readonly transloco = inject(TranslocoService);
  readonly model = signal({ email: '' });
  readonly f = form(this.model, (p) => {
    required(p.email);
    email(p.email);
    minLength(p.email, 5);
  });
}

async function setup(withPack: boolean) {
  TestBed.configureTestingModule({
    providers: withPack
      ? [provideTellmaUi(), provideTellmaLocaleAr()]
      : // 'ar' nominated as a tenant locale, pack NOT installed.
        [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
  });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
  return fixture;
}

async function settle(fixture: { whenStable(): Promise<unknown> }) {
  await new Promise((resolve) => setTimeout(resolve, 20));
  await fixture.whenStable();
}

async function touchEmail(fixture: ReturnType<typeof TestBed.createComponent<Host>>) {
  const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
  input.focus();
  input.blur();
  await fixture.whenStable();
}

function errorText(fixture: ReturnType<typeof TestBed.createComponent<Host>>): string {
  return (
    (fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement).textContent ??
    ''
  ).trim();
}

describe('@tellma/locale-ar (DoD 13)', () => {
  it('WITH the pack: Arabic locale renders Arabic library strings', async () => {
    const fixture = await setup(true);
    const host = fixture.componentInstance;

    host.transloco.setActiveLang('ar');
    await settle(fixture);
    await touchEmail(fixture);
    await settle(fixture);

    expect(errorText(fixture)).toBe('هذا الحقل مطلوب');
  });

  it('WITH the pack: ICU Arabic plural categories interpolate', async () => {
    const fixture = await setup(true);
    const host = fixture.componentInstance;
    host.transloco.setActiveLang('ar');
    await settle(fixture);

    const two = host.translate('errors.minLength', { minLength: 2 });
    const few = host.translate('errors.minLength', { minLength: 5 });
    const many = host.translate('errors.minLength', { minLength: 11 });
    const other = host.translate('errors.minLength', { minLength: 100 });
    await settle(fixture);
    expect(two()).toBe('أدخل حرفين على الأقل'); // the Arabic dual
    expect(few()).toBe('أدخل 5 أحرف على الأقل'); // 3-10 broken plural
    expect(many()).toBe('أدخل 11 حرفا على الأقل'); // 11-99 singular accusative
    expect(other()).toBe('أدخل 100 حرف على الأقل'); // 100+ singular
  });

  it('the ambient gender context conjugates the imperative', async () => {
    const gender = signal<Record<string, unknown>>({ gender: 'female' });
    TestBed.configureTestingModule({
      providers: [
        provideTellmaUi(),
        provideTellmaLocaleAr(),
        { provide: TM_UI_MESSAGE_CONTEXT, useValue: gender.asReadonly() },
      ],
    });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const host = fixture.componentInstance;
    host.transloco.setActiveLang('ar');
    await settle(fixture);

    const text = host.translate('errors.minLength', { minLength: 2 });
    const placeholder = host.translate('select.placeholder');
    expect(text()).toBe('أدخلي حرفين على الأقل'); // feminine imperative
    expect(placeholder()).toBe('حددي خيارا');

    gender.set({ gender: 'other' });
    expect(text()).toBe('أدخل حرفين على الأقل'); // live re-render on switch
  });

  it('WITHOUT the pack: the same keys fall back to ENGLISH — never a raw key', async () => {
    const fixture = await setup(false);
    const host = fixture.componentInstance;

    host.transloco.setActiveLang('ar');
    await settle(fixture);
    await touchEmail(fixture);
    await settle(fixture);

    expect(errorText(fixture)).toBe('This field is required');
  });

  it('switching the locale at runtime re-renders ALREADY-VISIBLE error text', async () => {
    const fixture = await setup(true);
    const host = fixture.componentInstance;

    await touchEmail(fixture);
    await settle(fixture);
    expect(errorText(fixture)).toBe('This field is required'); // English visible

    host.transloco.setActiveLang('ar');
    await settle(fixture);
    expect(errorText(fixture)).toBe('هذا الحقل مطلوب'); // SAME error, re-rendered

    host.transloco.setActiveLang('en');
    await settle(fixture);
    expect(errorText(fixture)).toBe('This field is required');
  });

  it('contributes the Arabic font subsets through the TM_FONT_SUBSETS multi token', async () => {
    await setup(true);
    const merged = TestBed.inject(TM_FONT_SUBSETS).flat();
    expect(merged).toContain(TM_FONTS_ARABIC[0]);
    // The union also still carries the core's Latin entries.
    expect(merged.some((s) => s.script === 'latin')).toBe(true);
    // Self-hosted: no CDN URL.
    expect(TM_FONTS_ARABIC.every((s) => !/^https?:/.test(s.url))).toBe(true);
  });
});
