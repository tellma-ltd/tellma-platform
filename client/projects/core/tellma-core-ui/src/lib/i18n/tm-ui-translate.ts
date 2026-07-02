import {
  computed,
  inject,
  InjectionToken,
  Injector,
  isDevMode,
  runInInjectionContext,
  signal,
  untracked,
  type Signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';

import { TM_UI_STRINGS_EN } from './strings-en';

/**
 * The thin one-function i18n seam (§7): resolves a library string key to a
 * reactive `Signal<string>` — reading the signal in a reactive context makes
 * the consumer re-render when the active locale changes, so no translated
 * string is ever cached outside the reactive graph.
 *
 * Implementations MUST return a stable signal per (key, params) so consumers
 * can call the function inside `computed()` without churn.
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
 */
export function tmDefaultUiTranslate(): TmUiTranslateFn {
  const injector = inject(Injector);
  const transloco = inject(TranslocoService, { optional: true });
  const cache = new Map<string, Signal<string>>();

  return (key, params) => {
    const cacheKey = `${key}|${params ? JSON.stringify(params) : ''}`;
    let result = cache.get(cacheKey);
    if (result) {
      return result;
    }
    if (transloco) {
      const namespacedKey = `${TM_UI_I18N_SCOPE}.${key}`;
      // untracked: the fn is legitimately called inside computed()s — signal
      // CREATION must not register with the caller's reactive context.
      const translated = untracked(() =>
        runInInjectionContext(injector, () =>
          toSignal(transloco.selectTranslate<string>(namespacedKey, params), {
            initialValue: '',
          }),
        ),
      );
      result = computed(() => {
        const text = translated();
        if (text === '') {
          return ''; // initial async tick — consumers render nothing briefly
        }
        if (text === namespacedKey || text === key) {
          // Raw-key echo: the key is missing everywhere Transloco looked.
          const english = tmEnglishString(key);
          return english !== null ? interpolate(english, params) : missingKeyGuard(key);
        }
        return text;
      });
    } else {
      const english = tmEnglishString(key);
      result = signal(english !== null ? interpolate(english, params) : missingKeyGuard(key));
    }
    cache.set(cacheKey, result);
    return result;
  };
}

/**
 * The i18n escape hatch (§7): a distribution on the default (Transloco-backed)
 * path writes zero config; supplying this token swaps the whole i18n backend.
 */
export const TM_UI_TRANSLATE = new InjectionToken<TmUiTranslateFn>('TM_UI_TRANSLATE', {
  providedIn: 'root',
  factory: tmDefaultUiTranslate,
});
