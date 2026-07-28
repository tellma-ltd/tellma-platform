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
  TM_UI_MESSAGE_CONTEXT,
  TM_UI_STRINGS_EN,
  TM_UI_TRANSLATE,
} from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';

import { provideTellmaLocaleAr } from './provide-tellma-locale-ar';
import { TM_LOCALE_AR_STRINGS } from './strings-ar';

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

/** Every leaf key path of a nested string table, sorted. */
function keyPaths(node: unknown, prefix = ''): string[] {
  if (typeof node === 'string') {
    return [prefix];
  }
  if (node === null || typeof node !== 'object') {
    return [`${prefix}<invalid>`];
  }
  return Object.entries(node)
    .flatMap(([key, value]) => keyPaths(value, prefix === '' ? key : `${prefix}.${key}`))
    .sort();
}

/** The message at a leaf key path ('' when the path holds no string). */
function messageAt(table: unknown, path: string): string {
  let node: unknown = table;
  for (const part of path.split('.')) {
    if (node === null || typeof node !== 'object') {
      return '';
    }
    node = (node as Record<string, unknown>)[part];
  }
  return typeof node === 'string' ? node : '';
}

/** Index of the `}` closing the `{` at `open`; throws when unbalanced. */
function closingBrace(message: string, open: number): number {
  let depth = 0;
  for (let i = open; i < message.length; i += 1) {
    if (message[i] === '{') {
      depth += 1;
    } else if (message[i] === '}') {
      depth -= 1;
      if (depth === 0) {
        return i;
      }
    }
  }
  throw new Error(`unbalanced braces in "${message}"`);
}

/** Splits a placeholder body on its top-level commas, into at most 3 parts. */
function splitBody(body: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < body.length && parts.length < 2; i += 1) {
    if (body[i] === '{') {
      depth += 1;
    } else if (body[i] === '}') {
      depth -= 1;
    } else if (body[i] === ',' && depth === 0) {
      parts.push(body.slice(start, i));
      start = i + 1;
    }
  }
  parts.push(body.slice(start));
  return parts.map((part) => part.trim());
}

/** The ICU argument types whose remainder is a list of sub-MESSAGES. */
const BRANCHING_TYPES = new Set(['plural', 'selectordinal', 'select']);

/**
 * Every ICU argument a message references (name -> type, '' when plain),
 * arms included. Structural rather than a regex, because a regex cannot tell
 * the placeholder `{count}` from the sub-message `{Nothing}` in
 * `{cells, plural, =0 {Nothing} other {# cells}}` — only the nesting can.
 */
function icuArguments(message: string, into = new Map<string, string>()): Map<string, string> {
  for (let i = 0; i < message.length; i += 1) {
    if (message[i] !== '{') {
      continue;
    }
    const close = closingBrace(message, i);
    const [name, type = '', arms = ''] = splitBody(message.slice(i + 1, close));
    into.set(name, type);
    if (BRANCHING_TYPES.has(type)) {
      for (let j = 0; j < arms.length; j += 1) {
        if (arms[j] === '{') {
          const armClose = closingBrace(arms, j);
          icuArguments(arms.slice(j + 1, armClose), into);
          j = armClose;
        }
      }
    }
    i = close;
  }
  return into;
}

/** A type-appropriate value per argument, enough to render any arm. */
function sampleParams(args: Map<string, string>): Record<string, unknown> {
  const params: Record<string, unknown> = {};
  for (const [name, type] of args) {
    if (type === 'plural' || type === 'selectordinal' || type === 'number') {
      params[name] = 5; // messageformat throws on a non-number here
    } else if (type === 'date' || type === 'time') {
      params[name] = new Date(0);
    } else if (type === 'select') {
      params[name] = 'other'; // the arm every select must define
    } else {
      params[name] = 'x';
    }
  }
  return params;
}

/**
 * Arguments the SESSION supplies rather than the caller (TM_UI_MESSAGE_CONTEXT
 * — Arabic imperatives conjugate for the addressee), so Arabic may branch on
 * them where English, being uninflected, has no placeholder at all.
 */
const AMBIENT_ARGS = new Set(['gender']);

describe('EN↔AR string parity (DoD 18)', () => {
  it('the Arabic pack covers exactly the built-in English key set', () => {
    // A key missing from AR silently falls back to English mid-sentence;
    // a key missing from EN is dead weight the fallback path can't serve.
    expect(keyPaths(TM_LOCALE_AR_STRINGS)).toEqual(keyPaths(TM_UI_STRINGS_EN));
  });

  it('the Arabic message of each key takes exactly the English arguments', () => {
    // Matching key paths are not matching MESSAGES: a caller passes the
    // params English asks for, so an argument AR drops silently deletes a
    // number from the sentence, and one AR invents renders as empty text.
    const dropped: string[] = [];
    const invented: string[] = [];
    for (const path of keyPaths(TM_UI_STRINGS_EN)) {
      const english = icuArguments(messageAt(TM_UI_STRINGS_EN, path));
      const arabic = icuArguments(messageAt(TM_LOCALE_AR_STRINGS, path));
      for (const name of english.keys()) {
        if (!arabic.has(name)) {
          dropped.push(`${path}: {${name}}`);
        }
      }
      for (const name of arabic.keys()) {
        if (!english.has(name) && !AMBIENT_ARGS.has(name)) {
          invented.push(`${path}: {${name}}`);
        }
      }
    }
    expect(dropped).toEqual([]);
    expect(invented).toEqual([]);
  });
});

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

  it('EVERY Arabic message compiles and renders', async () => {
    const fixture = await setup(true);
    const host = fixture.componentInstance;
    host.transloco.setActiveLang('ar');
    await settle(fixture);

    // The four tests above pin four messages; malformed ICU anywhere else
    // would first surface as a runtime throw in whichever component happens
    // to render that string. Reading each signal runs the real transpiler.
    const broken: string[] = [];
    for (const path of keyPaths(TM_LOCALE_AR_STRINGS)) {
      const message = messageAt(TM_LOCALE_AR_STRINGS, path);
      try {
        const rendered = host.translate(path, sampleParams(icuArguments(message)))();
        if (rendered === '') {
          broken.push(`${path}: rendered empty`);
        }
      } catch (error) {
        broken.push(`${path}: ${(error as Error).message}`);
      }
    }
    expect(broken).toEqual([]);
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
});
