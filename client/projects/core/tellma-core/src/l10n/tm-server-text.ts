// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, type Signal } from '@angular/core';

import type { TmProblem, TmProblemError } from '../api/tm-api-error';
import type { TmJson } from '../json/tm-json';
import { TmLocalization } from './tm-localization';

// Server text (spec 0024 §7.5): the server's problem and validation codes,
// and any other server key, rendered through the emitted string packs in the
// active language, so a switch re-renders every message on screen without a
// request. A problem's code names `Problem_<code>` and `Problem_<code>_Detail`
// (spec 0013 §10.1); a validation code names its key with `.` → `_`. The
// arguments are raw values, which the messages format in the active locale,
// calendar and zone. An argument that is an array of three texts is a
// localized text and renders the active content slot; `arguments.property`
// renders the property's label (spec 0016 §7.3). A code no pack holds renders
// the server's own text.

/** A problem's title and detail. */
export interface TmProblemText {
  /** The title. */
  readonly title: string;
  /** The detail; empty when the problem has none. */
  readonly detail: string;
}

/** A JSON object, or `undefined` for any other value. */
function objectOf(value: TmJson | undefined): Readonly<Record<string, TmJson>> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Readonly<Record<string, TmJson>>)
    : undefined;
}

/** A JSON string, or `''` for any other value. */
function textOf(value: TmJson | undefined): string {
  return typeof value === 'string' ? value : '';
}

/**
 * The server's codes and keys, rendered in the active language.
 *
 * @example
 * ```ts
 * const text = inject(TmServerText);
 * const { title, detail } = text.problem(error.problem)();
 * const item = text.error(error.problem.errors[0], 'gl.Center')();
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmServerText {
  readonly #l10n = inject(TmLocalization);

  /**
   * A problem's title and detail: `Problem_<code>` and `Problem_<code>_Detail`
   * with the problem's arguments, else the server's own title and detail.
   *
   * @param problem - The problem.
   * @returns The text, which follows the language and the formats.
   */
  problem(problem: TmProblem): Signal<TmProblemText> {
    const key = `Problem_${problem.code.replaceAll('-', '_')}`;
    const args = objectOf(problem['arguments']);
    return computed(() => ({
      title: this.#render(key, args, textOf(problem['title']) || problem.code),
      detail: this.#render(`${key}_Detail`, args, textOf(problem['detail'])),
    }));
  }

  /**
   * A validation item's message: its code with `.` → `_` and its arguments,
   * `arguments.property` rendered as the entity's property label; else the
   * server's own message.
   *
   * @param error - The item.
   * @param entity - The entity its path belongs to, such as `gl.Center`.
   * @returns The message, which follows the language and the formats.
   */
  error(error: TmProblemError, entity?: string): Signal<string> {
    const key = error.code.replaceAll('.', '_');
    const args = objectOf(error['arguments']);
    return computed(() => {
      const property = args?.['property'];
      const resolved =
        typeof property === 'string' && entity !== undefined && property !== ''
          ? { ...args, property: this.#l10n.ɵtext(labelKeyOf(entity, property), undefined, property)() }
          : args;
      return this.#render(key, resolved, error.message);
    });
  }

  /**
   * Any server key with raw arguments.
   *
   * @param key - The key, such as `Calendar_uq`.
   * @param args - The arguments.
   * @returns The message, which follows the language and the formats.
   */
  message(key: string, args?: Readonly<Record<string, TmJson>>): Signal<string> {
    return computed(() => this.#render(key, args, undefined));
  }

  /**
   * Whether the active language's pack or English's holds a key.
   *
   * @param key - The key.
   * @returns Whether a message exists.
   */
  has(key: string): boolean {
    return this.#l10n.ɵhas(key);
  }

  /** A key with its arguments, localized texts in the active slot; the fallback when no pack holds it. */
  #render(key: string, args: Readonly<Record<string, TmJson>> | undefined, fallback: string | undefined): string {
    const params: Record<string, unknown> = {};
    const slot = this.#l10n.contentSlot();
    for (const [name, value] of Object.entries(args ?? {})) {
      params[name] = Array.isArray(value) ? (value[slot - 1] ?? value[0] ?? '') : value;
    }
    return this.#l10n.ɵtext(key, params, fallback)();
  }
}

/**
 * The label key of an entity's property: `<Schema>_<Entity>_<Property>`.
 *
 * @param entity - The entity's name, such as `gl.Center`.
 * @param property - The property, in the wire's camel case, such as `name`.
 * @returns The key, such as `Gl_Center_Name`.
 */
function labelKeyOf(entity: string, property: string): string {
  const pascal = (text: string): string => text.charAt(0).toUpperCase() + text.slice(1);
  return [...entity.split('.'), property].map(pascal).join('_');
}
