import { minLength, required } from '@angular/forms/signals';

import { tmTestForm } from './form-fixture';

describe('tmTestForm fixture (§10)', () => {
  it('builds a live Signal Form with the given schema', () => {
    const { model, form } = tmTestForm({ name: '' }, (p) => {
      required(p.name);
      minLength(p.name, 3);
    });

    expect(form.name().invalid()).toBe(true);
    expect(form.name().errors().some((e) => e.kind === 'required')).toBe(true);

    model.set({ name: 'ok' });
    expect(form.name().errors().some((e) => e.kind === 'minLength')).toBe(true);

    model.set({ name: 'Ahmad' });
    expect(form.name().valid()).toBe(true);
    expect(form.name().value()).toBe('Ahmad');
  });

  it('builds a schema-less form', () => {
    const { form } = tmTestForm({ ok: true });
    expect(form.ok().value()).toBe(true);
    expect(form.ok().valid()).toBe(true);
  });
});
