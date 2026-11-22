// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLanguageMetadata } from '../metadata/tm-client-metadata';
import type { TmDigitStyle } from '../state/launch/tm-launch-docs';

// The resolution rules of the localization service (spec 0024 §7.1, §7.7),
// as pure functions of their inputs, so the service only wires signals to
// them and the rules test without a browser app: the active language, the
// region and its locale string, the browser check of that string, the
// content slot and the calendar code. The server resolves the same values in
// the same order (spec 0013 §9.2), so both ends format alike.

/** A language's subtag, lower-cased: `ar` for `ar`, `ar-SA` and `AR`. */
export function tmLanguageSubtag(code: string): string {
  return code.split('-')[0].toLowerCase();
}

/**
 * The offered language a code names, matched by its language subtag.
 *
 * @param offered - The offered language codes.
 * @param code - The code, such as a preference or a tenant's content language; absent for none.
 * @returns The offered code, or `undefined` when the code names none.
 */
export function tmOfferedLanguage(offered: readonly string[], code: string | null | undefined): string | undefined {
  if (!code) {
    return undefined;
  }
  const subtag = tmLanguageSubtag(code);
  return offered.find((candidate) => tmLanguageSubtag(candidate) === subtag);
}

/** What the active language is resolved from. */
export interface TmLanguageInputs {
  /** The offered languages' codes; the first is the distribution's default. */
  readonly offered: readonly string[];
  /** The boot record's language. */
  readonly bootLang: string;
  /** In a tenant: the user's preferred language, and the tenant's content languages, primary first. */
  readonly tenant?: { readonly preferred?: string | null; readonly contentLanguages: readonly string[] };
}

/**
 * The active language: in a tenant the user's preference, then the boot
 * record's language, then the tenant's primary language; outside one the
 * boot record's language; the distribution's default when none is offered.
 *
 * @param inputs - The offered languages, the boot record's language and the tenant's inputs.
 * @returns The offered language's code.
 */
export function tmResolveLanguage(inputs: TmLanguageInputs): string {
  const { offered, bootLang, tenant } = inputs;
  return (
    (tenant && tmOfferedLanguage(offered, tenant.preferred)) ??
    tmOfferedLanguage(offered, bootLang) ??
    (tenant && tmOfferedLanguage(offered, tenant.contentLanguages[0])) ??
    offered[0]
  );
}

/**
 * The languages the language switch offers: outside a tenant every offered
 * language; in one the tenant's content languages that are offered, then the
 * active language when it is not among them.
 *
 * @param offered - The offered language codes.
 * @param active - The active language.
 * @param contentLanguages - The tenant's content languages, or `undefined` outside a tenant.
 * @returns The codes, in the order the switch lists them.
 */
export function tmSwitchLanguages(
  offered: readonly string[],
  active: string,
  contentLanguages: readonly string[] | undefined,
): readonly string[] {
  if (contentLanguages === undefined) {
    return offered;
  }
  const codes: string[] = [];
  for (const code of contentLanguages) {
    const match = tmOfferedLanguage(offered, code);
    if (match !== undefined && !codes.includes(match)) {
      codes.push(match);
    }
  }
  return codes.includes(active) ? codes : [...codes, active];
}

/**
 * The content slot the active language reads: the 1-based position of the
 * tenant's content language whose language subtag is the active one's, else 1.
 *
 * @param language - The active language.
 * @param contentLanguages - The tenant's content languages, or `undefined` outside a tenant.
 * @returns The slot.
 */
export function tmContentSlot(language: string, contentLanguages: readonly string[] | undefined): 1 | 2 | 3 {
  const subtag = tmLanguageSubtag(language);
  const index = (contentLanguages ?? []).findIndex((code) => tmLanguageSubtag(code) === subtag);
  return index === 1 || index === 2 ? ((index + 1) as 2 | 3) : 1;
}

/**
 * The calendar code: in a tenant the user's preference when it is one of
 * the tenant's calendars, else the tenant's primary; outside a tenant `gc`.
 *
 * @param calendars - The tenant's calendars, primary first, or `undefined` outside a tenant.
 * @param preferred - The user's preferred calendar.
 * @returns The code.
 */
export function tmResolveCalendarCode(calendars: readonly string[] | undefined, preferred: string | null | undefined): string {
  if (calendars === undefined || calendars.length === 0) {
    return 'gc';
  }
  return preferred && calendars.includes(preferred) ? preferred : calendars[0];
}

/**
 * The region: the first of the user's and the tenant's regions that is
 * offered with the language, else the language's default region; outside a
 * tenant the default region.
 *
 * @param language - The language's metadata, or `undefined` before the Core chunk loads.
 * @param candidates - The user's region, then the tenant's; empty outside a tenant.
 * @returns The region, or `undefined` before the Core chunk loads.
 */
export function tmResolveRegion(
  language: TmLanguageMetadata | undefined,
  candidates: readonly (string | null | undefined)[],
): string | undefined {
  if (language === undefined) {
    return undefined;
  }
  return candidates.find((region): region is string => !!region && language.regions.includes(region)) ?? language.region;
}

/** The default numbering systems of locales, by locale tag. */
const defaultDigits = new Map<string, string>();

/** The numbering system a locale tag writes numbers in by default. */
function defaultDigitsOf(tag: string): string {
  let digits = defaultDigits.get(tag);
  if (digits === undefined) {
    digits = new Intl.NumberFormat(tag).resolvedOptions().numberingSystem;
    defaultDigits.set(tag, digits);
  }
  return digits;
}

/**
 * The locale string of a language, a region and a digits choice:
 * `<language>-<region>`, then `-u-nu-latn` for Latin digits or
 * `-u-nu-<system>` for the language's native digits, each only when it
 * changes the region's default digits, so the string stays canonical.
 *
 * @param base - `<language>-<region>`, or the language alone when the region is unknown.
 * @param digits - The digits choice; `null` for the region's default.
 * @param nativeDigits - The language's native numbering system, or `null` when its digits are Latin.
 * @returns The locale string, such as `ar-SA-u-nu-latn`.
 */
export function tmLocaleString(base: string, digits: TmDigitStyle | null, nativeDigits: string | null): string {
  const wanted = digits === 'Latin' ? 'latn' : digits === 'Native' ? nativeDigits : null;
  return wanted !== null && defaultDigitsOf(base) !== wanted ? `${base}-u-nu-${wanted}` : base;
}

/** The browser check's verdicts, by `<language>-<region>`. */
const resolvedTags = new Map<string, boolean>();

/**
 * Whether this browser formats a locale tag as itself: whether both
 * `Intl.DateTimeFormat` and `Intl.NumberFormat` resolve it exactly. Checked
 * once per tag.
 *
 * @param tag - The tag, such as `en-AE`.
 * @returns Whether the browser resolves it.
 */
export function tmBrowserResolves(tag: string): boolean {
  let resolves = resolvedTags.get(tag);
  if (resolves === undefined) {
    try {
      resolves =
        new Intl.DateTimeFormat(tag).resolvedOptions().locale === tag &&
        new Intl.NumberFormat(tag).resolvedOptions().locale === tag;
    } catch {
      // A tag the engine refuses outright resolves to nothing.
      resolves = false;
    }
    resolvedTags.set(tag, resolves);
  }
  return resolves;
}

/** The locale the client formats with, and whether the browser check fell back to reach it. */
export interface TmFormattingLocale {
  /** The tag the client formats with, digits extension included. */
  readonly locale: string;
  /** Whether the browser lacked `<language>-<region>`, so the tag is a fallback. */
  readonly fellBack: boolean;
}

/**
 * The browser check: `<language>-<region>` when the browser resolves it,
 * else the language with its default region, else the default language's
 * locale. The digits choice applies to whichever tag wins.
 *
 * @param language - The active language's metadata.
 * @param region - The resolved region.
 * @param digits - The digits choice; `null` for the region's default.
 * @param fallback - The default language's metadata, the last resort.
 * @returns The locale to format with.
 */
export function tmFormattingLocale(
  language: TmLanguageMetadata,
  region: string,
  digits: TmDigitStyle | null,
  fallback: TmLanguageMetadata,
): TmFormattingLocale {
  const candidates = [`${language.code}-${region}`, `${language.code}-${language.region}`, `${fallback.code}-${fallback.region}`];
  const tag = candidates.find(tmBrowserResolves) ?? candidates[2];
  return {
    locale: tmLocaleString(tag, digits, tag.startsWith(`${language.code}-`) ? language.nativeDigits : fallback.nativeDigits),
    fellBack: tag !== candidates[0],
  };
}
