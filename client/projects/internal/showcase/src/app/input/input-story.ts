import { Component, resource, signal } from '@angular/core';
import { email, form, FormField, minLength, required, validateAsync } from '@angular/forms/signals';

import { TmInput } from '@tellma/core-ui/input';
import { TmFormField } from '@tellma/core-ui/form-field';

/**
 * tmInput + tm-form-field demo host — drives the Playwright battery and the
 * visual verification pass (light/dark x LTR/RTL via the story query params).
 */
@Component({
  imports: [TmInput, TmFormField, FormField],
  template: `
    <h2>Text input</h2>

    <div class="grid">
      <tm-form-field label="Email" hint="Your work email" data-testid="ff-email">
        <input tmInput [formField]="f.email" data-testid="input-email" />
      </tm-form-field>

      <tm-form-field label="Username" hint="Checked against the server" data-testid="ff-username">
        <input tmInput [formField]="f.username" data-testid="input-username" />
      </tm-form-field>

      <tm-form-field label="Search" data-testid="ff-adorned">
        <svg tmPrefix width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
          <circle cx="7" cy="7" r="4.5" stroke="currentColor" />
          <path d="m10.5 10.5 3 3" stroke="currentColor" stroke-linecap="round" />
        </svg>
        <input tmInput [formField]="f.search" placeholder="Search records" />
        <span tmSuffix class="suffix-text">⌘K</span>
      </tm-form-field>

      <tm-form-field label="Small" size="sm" data-testid="ff-sm">
        <input tmInput [formField]="f.small" />
      </tm-form-field>

      <tm-form-field label="Large" size="lg" data-testid="ff-lg">
        <input tmInput [formField]="f.large" />
      </tm-form-field>

      <tm-form-field label="Unbound disabled" data-testid="ff-disabled">
        <input tmInput disabled value="Cannot touch this" />
      </tm-form-field>

      <tm-form-field label="Mixed bidi (Arabic-first)" data-testid="ff-bidi-ar">
        <input tmInput [formField]="f.bidiArabic" data-testid="input-bidi-ar" />
      </tm-form-field>

      <tm-form-field label="Mixed bidi (English-first)" data-testid="ff-bidi-en">
        <input tmInput [formField]="f.bidiEnglish" data-testid="input-bidi-en" />
      </tm-form-field>
    </div>
  `,
  styles: `
    .grid {
      display: grid;
      gap: 16px;
      max-inline-size: 420px;
    }
    .suffix-text {
      font-size: 12px;
      color: var(--text-secondary);
    }
  `,
})
export class InputStory {
  readonly model = signal({
    email: '',
    username: '',
    search: '',
    small: '',
    large: '',
    bidiArabic: 'مرحبا ABC-123',
    bidiEnglish: 'Order رقم 42',
  });

  readonly f = form(this.model, (p) => {
    required(p.email);
    email(p.email);
    required(p.username, { message: 'Pick a username first' });
    minLength(p.username, 5);
    validateAsync(p.username, {
      params: (ctx) => ctx.value(),
      factory: (params) =>
        resource({
          params,
          loader: async ({ params: name }) => {
            await new Promise((resolve) => setTimeout(resolve, 800));
            return name === 'taken' ? 'taken' : 'free';
          },
        }),
      onError: () => undefined,
      onSuccess: (result) =>
        result === 'taken' ? { kind: 'usernameTaken', message: 'Username is taken' } : undefined,
    });
  });
}
