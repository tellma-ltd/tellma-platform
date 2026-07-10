import {
  computed,
  DestroyRef,
  inject,
  InjectionToken,
  isDevMode,
  signal,
  type Signal,
} from '@angular/core';
import { TRANSLOCO_TRANSPILER, TranslocoService } from '@jsverse/transloco';

import { TM_UI_STRINGS_EN } from './strings-en';

/**
 * The thin one-function i18n seam: resolves a library string key to a
 * reactive `Signal<string>` — reading the signal in a reactive context makes
 * the consumer re-render when the active locale changes.
 *
 * The returned signal is a plain computed with no subscription of its own:
 * calling the function freely (even building a fresh signal per evaluation
 * of a consumer `computed()`) allocates nothing that outlives the caller.
 */
export type TmUiTranslateFn = (key: string, params?: Record<string, unknown>) => Signal<string>;

/**
 * The namespace holding the library's strings within each language's
 * translation ('tmUi.errors.required'). Locale packs merge their strings
 * under the same namespace into their language
 * (`setTranslation({ tmUi: … }, 'ar', { merge: true })`).
 */
export const TM_UI_I18N_SCOPE = 'tmUi';

/**
 * Ambient ICU parameters merged under every message resolution (explicit
 * per-call params win on collision). Grammatical context that is a property
 * of the SESSION rather than of any one message lives here — e.g. Arabic
 * imperatives conjugate for the addressee's gender, so Arabic strings
 * branch on `{gender, select, female {…} other {…}}`.
 */
export type TmUiMessageContext = Record<string, unknown>;

/**
 * The session-wide message context as a signal, so a runtime change (a user
 * profile load, a preference switch) re-renders every visible string. The
 * default carries `gender: 'other'`, which every gendered string must
 * treat as its base form.
 */
export const TM_UI_MESSAGE_CONTEXT = new InjectionToken<Signal<TmUiMessageContext>>(
  'TM_UI_MESSAGE_CONTEXT',
  { providedIn: 'root', factory: () => signal<TmUiMessageContext>({ gender: 'other' }).asReadonly() },
);

/**
 * Resolves a key against the packaged English strings ('errors.required' →
 * the English default). Returns null when even English lacks the key (a
 * missing CUSTOM kind — the caller decides the last-resort guard).
 */
export function tmEnglishString(key: string): string | null {
  let node: unknown = TM_UI_STRINGS_EN;
  for (const part of key.split('.')) {
    if (node === null || typeof node !== 'object' || !(part in node)) {
      return null;
    }
    node = (node as Record<string, unknown>)[part];
  }
  return typeof node === 'string' ? node : null;
}

/** Naive `{param}` interpolation for the no-Transloco static fallback. */
function interpolate(template: string, params?: Record<string, unknown>): string {
  if (!params) {
    return template;
  }
  // Strip ICU plural/select blocks down to their 'other' arm, then fill
  // simple placeholders — good enough for the last-resort static path.
  const simplified = template.replace(
    /\{(\w+),\s*(?:plural|select)\s*,[^{}]*other\s*\{([^{}]*)\}[^{}]*\}/g,
    (_, name: string, other: string) => other.replaceAll('#', String(params[name] ?? '')),
  );
  return simplified.replace(/\{(\w+)\}/g, (_, name: string) => String(params[name] ?? `{${name}}`));
}

function missingKeyGuard(key: string): string {
  if (isDevMode()) {
    console.warn(
      `[tellma-ui] Missing library string for "${key}" — even the built-in English set lacks it. ` +
        `If this is a custom validation kind, ship its message via the schema-inline {message} ` +
        `or add the key to your locale resources.`,
    );
  }
  return key.split('.').pop() ?? key;
}

/**
 * The default `TM_UI_TRANSLATE` implementation: Transloco-backed when a
 * `TranslocoService` is provided (the `provideTellmaUi()` path — live locale
 * switching included), else a static English-only resolver (so the library
 * renders sensibly even in a Transloco-less harness).
 *
 * Resolution is SYNCHRONOUS against Transloco's loaded translations (the
 * library's own strings are registered eagerly by `provideTellmaUi()` and
 * the locale packs), re-evaluated through one shared version signal bumped
 * by language changes and translation (re)loads. That keeps the factory to
 * two app-lifetime subscriptions total — resolving a message never
 * subscribes, caches, or serializes params — and a string's very first
 * read already carries text (English until the locale's strings apply),
 * never a blank frame in the error live region.
 */
export function tmDefaultUiTranslate(): TmUiTranslateFn {
  // TranslocoService is providedIn:'root', so an optional inject would still
  // instantiate it and then crash on ITS missing config deps in an app that
  // never called provideTransloco/provideTellmaUi. The transpiler token is
  // only present when Transloco was actually provided — probe that instead.
  const translocoProvided = inject(TRANSLOCO_TRANSPILER, { optional: true }) !== null;
  const context = inject(TM_UI_MESSAGE_CONTEXT);
  if (!translocoProvided) {
    return (key, params) =>
      computed(() => {
        const merged = { ...context(), ...params };
        const english = tmEnglishString(key);
        return english !== null ? interpolate(english, merged) : missingKeyGuard(key);
      });
  }

  const transloco = inject(TranslocoService);
  const version = signal(0);
  const bump = () => version.update((v) => v + 1);
  const langSub = transloco.langChanges$.subscribe(bump);
  const eventSub = transloco.events$.subscribe(bump);
  inject(DestroyRef).onDestroy(() => {
    langSub.unsubscribe();
    eventSub.unsubscribe();
  });

  return (key, params) =>
    computed(() => {
      version();
      const merged = { ...context(), ...params };
      const namespacedKey = `${TM_UI_I18N_SCOPE}.${key}`;
      const text = transloco.translate<string>(namespacedKey, merged);
      if (text === '' || text === namespacedKey || text === key) {
        // Raw-key echo: the key is missing everywhere Transloco looked.
        const english = tmEnglishString(key);
        return english !== null ? interpolate(english, merged) : missingKeyGuard(key);
      }
      return text;
    });
}

/**
 * The i18n escape hatch: a distribution on the default (Transloco-backed)
 * path writes zero config; supplying this token swaps the whole i18n backend.
 */
export const TM_UI_TRANSLATE = new InjectionToken<TmUiTranslateFn>('TM_UI_TRANSLATE', {
  providedIn: 'root',
  factory: tmDefaultUiTranslate,
});
