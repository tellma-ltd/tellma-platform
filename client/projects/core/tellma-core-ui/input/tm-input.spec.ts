// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, resource, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import {
  disabled,
  email,
  form,
  FormField,
  minLength,
  required,
  validateAsync,
} from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmFormFieldHarness, TmInputHarness } from '@tellma/core-ui-testing';

import { TmInput } from './tm-input';

describe('tmInput + tm-form-field (Signal Forms, §3.1/§3.2/§5)', () => {
  async function setup<T>(component: new () => T) {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(component);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0)); // i18n settle
    await fixture.whenStable();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    return {
      fixture,
      input: await loader.getHarness(TmInputHarness),
      field: await loader.getHarness(TmFormFieldHarness),
    };
  }

  describe('value flow', () => {
    @Component({
      imports: [TmInput, TmFormField, FormField],
      template: `
        <tm-form-field label="Email" hint="Your work email">
          <input tmInput [formField]="f.email" />
        </tm-form-field>
      `,
    })
    class Host {
      readonly model = signal({ email: '' });
      readonly f = form(this.model, (p) => {
        required(p.email);
        email(p.email);
      });
    }

    it('user typing flows into the model; external writes flow into the input', async () => {
      const { fixture, input } = await setup(Host);
      const host = fixture.componentInstance;

      await input.setValue('a@b.co');
      await fixture.whenStable();
      expect(host.model().email).toBe('a@b.co');

      host.f.email().value.set('x@y.org');
      await fixture.whenStable();
      expect(await input.getValue()).toBe('x@y.org');
    });

    it('shows the hint until touched-and-invalid, then swaps to the localized error', async () => {
      const { fixture, input, field } = await setup(Host);

      // Pristine: hint visible, no error, not marked invalid.
      expect(await field.getHintText()).toBe('Your work email');
      expect(await field.getErrorText()).toBeNull();
      expect(await input.isInvalid()).toBe(false);

      // Blur without typing -> touched + required error, localized.
      await input.focus();
      await input.blur();
      await fixture.whenStable();
      expect(await field.getErrorText()).toBe('This field is required');
      expect(await field.getHintText()).toBeNull();
      expect(await input.isInvalid()).toBe(true);

      // aria-describedby now points at the error element, which holds the text.
      const describedBy = await input.getDescribedBy();
      expect(describedBy).toBeTruthy();
      const errorEl = document.getElementById(describedBy!);
      expect(errorEl?.textContent?.trim()).toBe('This field is required');
      expect(errorEl?.getAttribute('aria-live')).toBe('polite');
    });

    it('marks required from the schema and renders the required marker', async () => {
      const { input, field } = await setup(Host);
      expect(await input.isRequired()).toBe(true);
      expect(await field.hasRequiredMarker()).toBe(true);
    });

    it('label is associated via <label for> and click focuses the input', async () => {
      const { fixture, input, field } = await setup(Host);
      const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
      const inputEl = fixture.nativeElement.querySelector('input') as HTMLInputElement;
      expect(label.htmlFor).toBe(inputEl.id);

      await field.labelClick();
      await fixture.whenStable();
      expect(await input.isFocused()).toBe(true);
    });
  });

  describe('message precedence + ICU (§5, DoD 15)', () => {
    @Component({
      imports: [TmInput, TmFormField, FormField],
      template: `
        <tm-form-field label="Username">
          <input tmInput [formField]="f.username" />
        </tm-form-field>
      `,
    })
    class Host {
      readonly model = signal({ username: '' });
      readonly f = form(this.model, (p) => {
        required(p.username, { message: 'Inline: pick a username' });
        minLength(p.username, 5);
      });
    }

    it('schema-inline message wins; kind default interpolates ICU params', async () => {
      const { fixture, input, field } = await setup(Host);

      await input.focus();
      await input.blur();
      await fixture.whenStable();
      // Inline message beats the localized 'required' default.
      expect(await field.getErrorText()).toBe('Inline: pick a username');

      await input.setValue('abc');
      await fixture.whenStable();
      // minLength has no inline message -> localized ICU default with params.
      expect(await field.getErrorText()).toBe('Enter at least 5 characters');
    });
  });

  describe('disabled/readonly/required precedence (§5, DoD 3)', () => {
    // NOTE: the spec's "single regression test pinning the framework's
    // write order for a conflicting template binding" is impossible AND
    // unnecessary in v22: the Angular compiler REJECTS such a binding
    // outright (NG8022 "Binding to '[disabled]' is not allowed on nodes
    // using the '[formField]' directive") — verified during this stage.
    // The conflict the spec forbids by lint cannot even compile; our
    // template lint remains as the earlier, friendlier message.
    @Component({
      imports: [TmInput, TmFormField, FormField],
      template: `
        <tm-form-field label="Code">
          <input tmInput [formField]="f.code" />
        </tm-form-field>
      `,
    })
    class BoundHost {
      readonly model = signal({ code: 'x' });
      readonly f = form(this.model, (p) => {
        disabled(p.code, { when: () => true });
      });
    }

    it('the bound field is authoritative for disabled state', async () => {
      const { input } = await setup(BoundHost);
      expect(await input.isDisabled()).toBe(true);
    });

    @Component({
      imports: [TmInput],
      template: `<input tmInput disabled readonly required placeholder="Bare" />`,
    })
    class UnboundHost {}

    it('component inputs apply in unbound usage', async () => {
      TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
      const fixture = TestBed.createComponent(UnboundHost);
      await fixture.whenStable();
      const loader = TestbedHarnessEnvironment.loader(fixture);
      const input = await loader.getHarness(TmInputHarness);
      expect(await input.isDisabled()).toBe(true);
      expect(await input.isReadonly()).toBe(true);
      expect(await input.isRequired()).toBe(true);
      expect(await input.getPlaceholder()).toBe('Bare');
    });
  });

  describe('author-supplied aria-describedby (§2.1)', () => {
    @Component({
      imports: [TmInput],
      template: `
        <span id="ext-desc">External description</span>
        <input tmInput aria-describedby="ext-desc" />
      `,
    })
    class BareHost {}

    it('survives on a bare input outside any field', async () => {
      TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
      const fixture = TestBed.createComponent(BareHost);
      await fixture.whenStable();
      const inputEl = fixture.nativeElement.querySelector('input') as HTMLInputElement;
      expect(inputEl.getAttribute('aria-describedby')).toBe('ext-desc');
    });

    @Component({
      imports: [TmInput, TmFormField, FormField],
      template: `
        <span id="ext-desc">External description</span>
        <tm-form-field label="Email" hint="Work email">
          <input tmInput aria-describedby="ext-desc" [formField]="f.email" />
        </tm-form-field>
      `,
    })
    class FieldHost {
      readonly model = signal({ email: '' });
      readonly f = form(this.model, (p) => {
        required(p.email);
      });
    }

    it("merges ahead of the field's hint/error ids, never clobbered", async () => {
      const { fixture, input } = await setup(FieldHost);
      const inputEl = fixture.nativeElement.querySelector('input') as HTMLInputElement;

      const ids = inputEl.getAttribute('aria-describedby')!.split(' ');
      expect(ids[0]).toBe('ext-desc');
      expect(document.getElementById(ids[1])?.textContent?.trim()).toBe('Work email');

      await input.focus();
      await input.blur();
      await fixture.whenStable();
      const after = inputEl.getAttribute('aria-describedby')!.split(' ');
      expect(after[0]).toBe('ext-desc'); // author id stays first
      expect(document.getElementById(after[1])?.textContent?.trim()).toBe(
        'This field is required',
      );
    });
  });

  describe('pending/async validation (§5, DoD 8)', () => {
    let releaseValidation: () => void;
    let pendingGate: Promise<void>;

    @Component({
      imports: [TmInput, TmFormField, FormField],
      template: `
        <tm-form-field label="Handle">
          <input tmInput [formField]="f.handle" />
        </tm-form-field>
      `,
    })
    class Host {
      // Starts sync-invalid (required + empty) so the async validator does
      // NOT run at setup — async validation only runs once sync passes.
      readonly model = signal({ handle: '' });
      readonly f = form(this.model, (p) => {
        required(p.handle);
        validateAsync(p.handle, {
          params: (ctx) => ctx.value(),
          factory: (params) =>
            resource({
              params,
              loader: async ({ params: handle }) => {
                await pendingGate;
                return handle === 'taken' ? 'taken' : 'free';
              },
            }),
          onError: () => undefined,
          onSuccess: (result) =>
            result === 'taken' ? { kind: 'handleTaken', message: 'Handle is taken' } : undefined,
        });
      });
    }

    it('sets aria-busy + shows the spinner while pending, and holds errors', async () => {
      pendingGate = new Promise<void>((resolve) => {
        releaseValidation = resolve;
      });
      const { fixture } = await setup(Host);
      const host = fixture.componentInstance;
      const inputEl = fixture.nativeElement.querySelector('input') as HTMLInputElement;
      const errorEl = fixture.nativeElement.querySelector(
        '.tm-form-field__error',
      ) as HTMLElement;

      // While the gated resource is in flight the app is deliberately
      // UNSTABLE — whenStable()/harness auto-stabilize would hang, so this
      // block drives the DOM raw and flushes change detection manually.
      inputEl.focus();
      inputEl.value = 'taken';
      inputEl.dispatchEvent(new Event('input', { bubbles: true }));
      inputEl.blur();
      await new Promise((resolve) => setTimeout(resolve, 20));
      fixture.detectChanges();

      // Async validation in flight: busy, spinner visible, NO stale verdict.
      expect(host.f.handle().pending()).toBe(true);
      expect(inputEl.getAttribute('aria-busy')).toBe('true');
      expect(fixture.nativeElement.querySelector('.tm-form-field__spinner')).toBeTruthy();
      expect(errorEl.textContent?.trim()).toBe('');

      releaseValidation();
      await new Promise((resolve) => setTimeout(resolve, 20));
      await fixture.whenStable();

      expect(host.f.handle().pending()).toBe(false);
      expect(inputEl.getAttribute('aria-busy')).toBeNull();
      expect(fixture.nativeElement.querySelector('.tm-form-field__spinner')).toBeNull();
      expect(errorEl.textContent?.trim()).toBe('Handle is taken');
    });
  });
});
