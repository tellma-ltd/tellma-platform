import { Component, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { email, form, FormField, required } from '@angular/forms/signals';

import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';
import { TmOption, TmSelect } from '@tellma/core-ui/select';

/**
 * Locale-pack demo host (DoD 13): runtime language switching re-renders
 * already-visible library strings; Arabic content pulls the pack's font on
 * demand (unicode-range).
 */
@Component({
  imports: [TmInput, TmFormField, TmSelect, TmOption, FormField],
  template: `
    <h2>i18n / locale packs</h2>

    <p>
      <button type="button" data-testid="lang-en" (click)="setLang('en')">English</button>
      <button type="button" data-testid="lang-ar" (click)="setLang('ar')">العربية</button>
      <span data-testid="active-lang">{{ activeLang() }}</span>
    </p>

    <div class="grid">
      <tm-form-field label="Email" data-testid="ff-email">
        <input tmInput [formField]="f.email" data-testid="input-email" />
      </tm-form-field>

      <tm-form-field label="Status" data-testid="ff-status">
        <tm-select [(value)]="status" data-testid="select-status">
          <tm-option [value]="1" label="نشط">نشط</tm-option>
          <tm-option [value]="2" label="موقوف">موقوف</tm-option>
        </tm-select>
      </tm-form-field>
    </div>
  `,
  styles: `
    .grid {
      display: grid;
      gap: 16px;
      max-inline-size: 360px;
    }
  `,
})
export class I18nStory {
  private readonly transloco = inject(TranslocoService);

  readonly activeLang = signal('en');
  readonly status = signal<number | undefined>(undefined);

  readonly model = signal({ email: '' });
  readonly f = form(this.model, (p) => {
    required(p.email);
    email(p.email);
  });

  setLang(lang: string): void {
    this.transloco.setActiveLang(lang);
    this.activeLang.set(lang);
    document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
    document.documentElement.lang = lang;
  }
}
