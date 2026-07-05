import { signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form, type FieldTree, type SchemaOrSchemaFn } from '@angular/forms/signals';

/**
 * The shared `form()` test fixture (spec §10): behavioral tests for
 * `[formField]` binding, precedence, pending state, and message resolution
 * all need a live Signal Form — this helper builds one inside the TestBed
 * injection context with a configurable schema (validators, async
 * validators, `debounce`, inline messages).
 */
export interface TmTestForm<T> {
  readonly model: WritableSignal<T>;
  readonly form: FieldTree<T>;
}

export function tmTestForm<T>(initial: T, schema?: SchemaOrSchemaFn<T>): TmTestForm<T> {
  const model = signal(initial);
  const tree = TestBed.runInInjectionContext(() =>
    schema === undefined ? form(model) : form(model, schema),
  );
  return { model, form: tree };
}
