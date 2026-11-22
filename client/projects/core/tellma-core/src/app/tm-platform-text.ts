// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, signal, type Signal } from '@angular/core';

import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import type { TmStringPack } from '../metadata/tm-metadata-loaders';
import type { TmStringTable, TmStringTree } from './tm-app-config';

// The text the platform's own chrome reads before the localization service
// exists: the route titles, the route host's error views and the display
// name (spec 0024 §4.3, §4.4). The boot sequence loads the active language's
// string pack and client tables, and English's, and hands them over here.
// A key resolves in the active language, then in English, then to the
// caller's English fallback; `tm.` and `tmUi.` keys read the client tables,
// any other key the server's string pack. Spec 0024 §7's localization
// service replaces this resolver with its own.

/** One language's loaded strings: the server's string pack and the client tables. */
export interface TmLoadedStrings {
  /** The server's string pack. */
  readonly pack: TmStringPack;
  /** The client string tables, when the app ships them for the language. */
  readonly tables?: TmStringTable;
}

/** Parameters of a message: `{name}` placeholders and their values. */
export type TmTextParams = Readonly<Record<string, string>>;

/**
 * The platform's text before the localization service: the loaded string
 * packs of the active language and English, and the display name.
 */
@Injectable({ providedIn: 'root' })
export class TmPlatformText {
  readonly #metadata = inject(TmClientMetadataStore);

  /** The loaded strings, the active language's first. */
  readonly #strings = signal<readonly TmLoadedStrings[]>([]);

  /** The distribution's display name: its string, else the Core chunk's. */
  readonly displayName: Signal<string> = computed(
    () => this.#find('Distribution_DisplayName') ?? this.#metadata.peek()?.distribution.displayName ?? '',
  );

  /**
   * Keeps the loaded strings. Internal: the boot sequence calls it.
   *
   * @param strings - The active language's strings, then English's when it differs.
   */
  ɵset(strings: readonly TmLoadedStrings[]): void {
    this.#strings.set(strings);
  }

  /**
   * A message, in the active language, else in English, else the fallback,
   * else the key itself, with its `{name}` placeholders filled.
   *
   * @param key - The string key, such as `tm.admin.settings.title` or `Acme_Customers`.
   * @param fallback - The English text when no loaded string has the key.
   * @param params - The placeholders' values.
   * @returns The message, which follows the loaded strings.
   */
  text(key: string, fallback?: string, params?: TmTextParams): Signal<string> {
    return computed(() => {
      const message = this.#find(key) ?? fallback ?? key;
      return params ? message.replace(/\{(\w+)\}/g, (all, name: string) => params[name] ?? all) : message;
    });
  }

  /** The first loaded string of a key, or `undefined`. */
  #find(key: string): string | undefined {
    for (const strings of this.#strings()) {
      const found = key.startsWith('tm.')
        ? leaf(strings.tables?.tm, key.slice(3))
        : key.startsWith('tmUi.')
          ? leaf(strings.tables?.tmUi, key.slice(5))
          : strings.pack[key];
      if (found !== undefined) {
        return found;
      }
    }
    return undefined;
  }
}

/**
 * The message at a dotted path of a client string tree.
 *
 * @param tree - The tree, or `undefined` when the language ships none.
 * @param path - The dotted path below the tree, such as `admin.settings.title`.
 * @returns The message, or `undefined` when the path names none.
 */
function leaf(tree: TmStringTree | undefined, path: string): string | undefined {
  let node: TmStringTree | string | undefined = tree;
  for (const part of path.split('.')) {
    node = typeof node === 'object' ? node[part] : undefined;
  }
  return typeof node === 'string' ? node : undefined;
}
