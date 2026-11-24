// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, type Signal } from '@angular/core';
import {
  TmEntities,
  type TmEntityType,
  TmLocalization,
  TmServerText,
  type TmJson,
  ɵTmClientMetadataStore,
} from '@tellma/core';
import { tmStripBidiControls } from '@tellma/core-ui/l10n';

import { tmRegisterAttentionStrings } from './tm-attention-strings';
import { tmIcuArguments, tmIcuObject } from './tm-icu-arguments';

// Server text on the client (spec 0028 §11): notification texts, job labels
// and assignment captions travel as keys and arguments, and render in the
// reader's language through TmServerText. Every result is a signal, so a
// language, content-language or calendar switch re-renders it with no
// request. The keys follow the server's: a dotted key's `.` becomes `_`.

/** A placeholder's opening delimiter: a private-use character no message holds. */
const OPEN = '';
/** A placeholder's closing delimiter: a private-use character no message holds. */
const CLOSE = '';

/** An assignment title's key. */
const TITLE_KEY = 'tm.attention.assignment.title';
/** The arguments an assignment title formats with placeholders, so the code can be split out. */
const TITLE_ARGS = ['entity', 'code'] as const;

/** One piece of a message split around its isolated arguments. */
export interface TmTextSegment {
  /** The piece's text. */
  readonly text: string;
  /** The argument this piece is the value of, which renders in its own isolate; absent for the message's own text. */
  readonly arg?: string;
}

/** A notification's rendered text. */
export interface TmNotificationText {
  /** The title; the type's key when no strings hold its title. */
  readonly title: string;
  /** The second line, when the type has one. */
  readonly detail: string | null;
}

/** An assignment's rendered caption. */
export interface TmAssignmentCaption {
  /** "{entity} {code}" as plain text, for toasts and accessible names. */
  readonly title: string;
  /** The title split around the code, which a row renders in a `<bdi>`. */
  readonly titleSegments: readonly TmTextSegment[];
  /** The caption's detail line, when the resource's strings and the caption both have one. */
  readonly detail: string | null;
  /** The amount, with its currency's code when it has one. */
  readonly amount: string | null;
}

/** A job, as far as its label needs. */
export interface TmJobLabelSource {
  /** The job's handler key, such as `core.export`. */
  readonly handlerKey: string;
  /** The job's own label key; absent, the handler's label. */
  readonly labelKey?: string | null;
  /** The label's arguments, as JSON. */
  readonly labelArgumentsJson?: string | null;
}

/** An assignment's stored caption: every member optional, the amount a decimal string. */
interface StoredCaption {
  readonly code?: TmJson;
  readonly amount?: TmJson;
  readonly currency?: TmJson;
  readonly detail?: TmJson;
}

/** A dotted key as a resource name: every `.` replaced by `_`. */
function resourceOf(key: string): string {
  return key.replaceAll('.', '_');
}

/** A string member, else `undefined`. */
function textOf(value: TmJson | undefined): string | undefined {
  return typeof value === 'string' && value !== '' ? value : undefined;
}

/**
 * Renders the attention surfaces' server text in the reader's language:
 * notification titles and details, job labels and messages, and assignment
 * captions.
 *
 * @example
 * ```ts
 * const text = inject(TmAttentionText);
 * const notice = text.notification(row.type, row.argumentsJson);
 * const label = text.job({ handlerKey: 'core.export', labelKey: 'Excel.Export.Label', labelArgumentsJson: '{"entity":"acme.SalesInvoice"}' });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmAttentionText {
  readonly #l10n = inject(TmLocalization);
  readonly #text = inject(TmServerText);
  readonly #entities = inject(TmEntities);
  readonly #metadata = inject(ɵTmClientMetadataStore);

  /** Creates the service, which registers the attention strings. */
  constructor() {
    tmRegisterAttentionStrings(this.#l10n);
  }

  /**
   * A notification's title, `Notification_<Key>`, and detail,
   * `Notification_<Key>_Detail`, formatted with its arguments. A missing
   * title renders the type's key; a missing detail is `null`.
   *
   * @param type - The notification's type, such as `core.export.ready`.
   * @param argumentsJson - Its arguments, as JSON.
   * @returns The text, which follows the language.
   */
  notification(type: string, argumentsJson: string | null): Signal<TmNotificationText> {
    const key = `Notification_${resourceOf(type)}`;
    const args = tmIcuArguments(argumentsJson);
    const title = this.#text.message(key, args);
    const detail = this.#text.message(`${key}_Detail`, args);
    return computed(() => ({
      title: this.#text.has(key) ? title() : type,
      detail: this.#text.has(`${key}_Detail`) ? detail() : null,
    }));
  }

  /**
   * A job's label, the first that renders of: the job's own label key with
   * its arguments, whose `entity` argument becomes that entity's plural
   * label; the handler's label key; the handler's English fallback; the
   * handler's key itself.
   *
   * @param job - The job, or a bare handler key's `{ handlerKey }`.
   * @returns The label, which follows the language.
   */
  job(job: TmJobLabelSource): Signal<string> {
    const own = job.labelKey ? resourceOf(job.labelKey) : null;
    const args = tmIcuArguments(job.labelArgumentsJson);
    return computed(() => {
      // --- 1. The job's own label, its entity argument as the plural label ---
      if (own !== null && this.#text.has(own)) {
        const entity = args?.['entity'];
        const resolved =
          typeof entity === 'string' && this.#entities.summary(entity) !== undefined
            ? { ...args, entity: this.#entities.label(entity, true)() }
            : args;
        return this.#text.message(own, resolved)();
      }
      // --- 2. The handler's label; 3. its fallback; 4. its key ---
      const handler = this.#metadata.current()?.jobHandlers.find((candidate) => candidate.key === job.handlerKey);
      const handlerKey = handler?.labelKey ?? `JobHandler_${resourceOf(job.handlerKey)}`;
      return this.#text.has(handlerKey) ? this.#text.message(handlerKey)() : (handler?.labelFallback ?? job.handlerKey);
    });
  }

  /**
   * A job's message, such as its progress key or error code, formatted with
   * its arguments; the key itself when no strings hold it.
   *
   * @param key - The message's key, such as `Excel.Export.Progress`, or `null`.
   * @param argumentsJson - Its arguments, as JSON.
   * @returns The message, or `null` without a key.
   */
  jobMessage(key: string | null, argumentsJson: string | null): Signal<string | null> {
    if (key === null) {
      return computed(() => null);
    }
    const resource = resourceOf(key);
    const message = this.#text.message(resource, tmIcuArguments(argumentsJson));
    return computed(() => (this.#text.has(resource) ? message() : key));
  }

  /**
   * An assignment's caption: the title "{entity} {code}", with the target's
   * id when the caption has no code; the detail
   * `Assignment_<Resource>_Detail` with the caption's detail as its
   * arguments; and the amount, exactly as stored, with its currency's code.
   *
   * @param row - The assignment.
   * @returns The caption, which follows the language.
   */
  caption(row: TmEntityType<'core.Assignment'>): Signal<TmAssignmentCaption> {
    const resource = typeof row['resource'] === 'string' ? row['resource'] : '';
    const caption = parseCaption(row['captionJson']);
    const code = textOf(caption.code) ?? `#${String(row['targetId'] ?? '')}`;
    const detailKey = `Assignment_${resourceOf(resource)}_Detail`;
    // A caption without a detail renders none even when the resource has a
    // detail key (spec 0028 §11.3 drops it only without the key): the
    // message's placeholders would otherwise render raw.
    const detailArgs = tmIcuObject(caption.detail);
    const detail = detailArgs && this.#text.message(detailKey, detailArgs);
    const amount = textOf(caption.amount);
    const currency = textOf(caption.currency);
    // The title formats once with placeholders for both arguments; the
    // entity's label, which follows the language too, fills its own in.
    const entity = this.#entities.label(resource);
    const title = this.#l10n.ɵtext(TITLE_KEY, placeholdersFor(TITLE_ARGS));
    return computed(() => {
      const titleSegments = split(title(), TITLE_ARGS, { entity: entity(), code }, ['code']);
      return {
        title: titleSegments.map((segment) => segment.text).join(''),
        titleSegments,
        detail: detail !== undefined && this.#text.has(detailKey) ? detail() : null,
        amount: amount === undefined ? null : formatAmount(amount, currency, this.#l10n.locale()),
      };
    });
  }

  /**
   * A platform message split around the arguments that render in isolates:
   * the message is formatted with a placeholder for each, then split on the
   * placeholders, so the plain text around them keeps its translation's
   * order while each value renders in a `<bdi>` of its own.
   *
   * @param key - The platform key, such as `tm.attention.assignment.title`.
   * @param args - The message's arguments.
   * @param isolated - The arguments that render in isolates.
   * @returns The segments in reading order, which follow the language.
   */
  isolate(key: string, args: Readonly<Record<string, unknown>>, isolated: readonly string[]): Signal<readonly TmTextSegment[]> {
    const message = this.#l10n.ɵtext(key, { ...args, ...placeholdersFor(isolated) });
    const values = Object.fromEntries(isolated.map((name) => [name, String(args[name] ?? '')]));
    return computed(() => split(message(), isolated, values, isolated));
  }
}

/** A numbered placeholder for each argument, by name. */
function placeholdersFor(names: readonly string[]): Record<string, string> {
  return Object.fromEntries(names.map((name, index) => [name, `${OPEN}${index}${CLOSE}`]));
}

/**
 * Splits a message formatted with placeholders: each placeholder becomes its
 * argument's value, a segment of its own when the argument is isolated, else
 * merged into the text around it.
 *
 * @param message - The message, formatted with `placeholdersFor(names)`.
 * @param names - The arguments, in their placeholders' order.
 * @param values - Each argument's value.
 * @param isolated - The arguments whose values are segments of their own.
 * @returns The segments in reading order.
 */
function split(
  message: string,
  names: readonly string[],
  values: Readonly<Record<string, string>>,
  isolated: readonly string[],
): TmTextSegment[] {
  const segments: TmTextSegment[] = [];
  let text = '';
  for (const [index, piece] of message.split(OPEN).entries()) {
    // The first piece precedes every placeholder; each later one starts with one.
    const close = index === 0 ? -1 : piece.indexOf(CLOSE);
    const name = close < 0 ? undefined : names[Number(piece.slice(0, close))];
    if (name !== undefined && isolated.includes(name)) {
      if (text !== '') {
        segments.push({ text });
      }
      segments.push({ text: values[name] ?? '', arg: name });
      text = '';
    } else if (name !== undefined) {
      text += values[name] ?? '';
    }
    text += piece.slice(close + 1);
  }
  if (text !== '') {
    segments.push({ text });
  }
  return segments;
}

/** A stored caption's members; an unreadable caption has none. */
function parseCaption(json: TmJson | undefined): StoredCaption {
  if (typeof json !== 'string') {
    return {};
  }
  try {
    const parsed: unknown = JSON.parse(json);
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed) ? (parsed as StoredCaption) : {};
  } catch {
    return {};
  }
}

/**
 * Formats a decimal string exactly, at its own scale: as a currency with its
 * code, else as a plain number. A currency the browser does not know shows
 * its code before the plain number; text that is not a decimal shows as it is.
 */
function formatAmount(amount: string, currency: string | undefined, locale: string): string {
  if (!/^-?\d+(\.\d+)?$/.test(amount)) {
    return amount;
  }
  const scale = amount.split('.')[1]?.length ?? 0;
  const digits = { minimumFractionDigits: scale, maximumFractionDigits: scale };
  // Intl formats a decimal string exactly, with no float rounding; the
  // workspace's library types still declare numbers alone.
  const format = (options: Intl.NumberFormatOptions): string =>
    tmStripBidiControls(new Intl.NumberFormat(locale, { ...digits, ...options }).format(amount as unknown as number));
  if (currency === undefined) {
    return format({});
  }
  try {
    return format({ style: 'currency', currency, currencyDisplay: 'code' });
  } catch {
    return `${currency} ${format({})}`;
  }
}
