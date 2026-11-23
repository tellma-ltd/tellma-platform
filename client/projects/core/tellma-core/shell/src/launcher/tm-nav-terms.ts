// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { TmEntities, TmLocalization, type TmNavItem } from '@tellma/core';

import { TM_LAUNCHER_SCOPE, TM_LAUNCHER_STRINGS_EN } from './tm-launcher-strings';

// The terms the launcher's search matches an entry by (spec 0027 §9.2): its
// label and its synonyms, in the active language and in English. The
// synonyms string is comma-separated, Arabic's comma counting too. The
// reader brings the launcher's English strings, the platform entries'
// synonyms among them, so they load with the launcher and not before.

/** An entry's label and synonyms in one language. */
export interface TmNavTerms {
  /** The label. */
  readonly label: string;
  /** The synonyms: the synonyms string split on `,` and `،`, trimmed, empty parts dropped. */
  readonly synonyms: readonly string[];
}

/** The launcher index's reader of an entry's terms in a language. */
@Injectable({ providedIn: 'root' })
export class TmNavTermsReader {
  readonly #transloco = inject(TranslocoService);
  readonly #l10n = inject(TmLocalization);
  readonly #entities = inject(TmEntities);

  /** Creates the reader, which registers the launcher's English strings under `tm.launcher`. */
  constructor() {
    // A language's table that holds a key wins over the registered English.
    this.#l10n.ɵregisterStrings(TM_LAUNCHER_SCOPE, TM_LAUNCHER_STRINGS_EN);
  }

  /**
   * An entry's label and synonyms in a language. The active language's label
   * is the entry's own; in another language, a missing label reads as the
   * entity's English fallback, else as its key. A missing synonyms string
   * means none.
   *
   * @param item - The entry.
   * @param language - The language, such as `en`, its strings loaded.
   * @returns The terms.
   */
  read(item: TmNavItem, language: string): TmNavTerms {
    const table = this.#transloco.getTranslation(language) as Readonly<Record<string, unknown>>;
    // Transloco answers a key it lacks with the key itself, so presence is read from the table.
    const has = (key: string): boolean => typeof table[key] === 'string' && table[key] !== '';
    const label =
      language === this.#l10n.language()
        ? item.label
        : has(item.labelKey)
          ? this.#transloco.translate<string>(item.labelKey, {}, language)
          : ((item.entity && this.#entities.summary(item.entity)?.labelFallback) ?? item.labelKey);
    const synonyms = has(item.synonymsKey) ? this.#transloco.translate<string>(item.synonymsKey, {}, language) : '';
    return {
      label,
      synonyms: synonyms
        .split(/[,،]/)
        .map((part) => part.trim())
        .filter((part) => part !== ''),
    };
  }
}
