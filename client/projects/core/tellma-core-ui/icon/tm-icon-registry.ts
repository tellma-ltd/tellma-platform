// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Injectable,
  inject,
  provideEnvironmentInitializer,
  signal,
  type EnvironmentProviders,
} from '@angular/core';

/**
 * Icon data: the SVG children of one 24×24 glyph, each written as
 * `[tag, attributes]`, optionally with a third member holding that
 * element's own children.
 *
 * This is Lucide's `IconNode` format, so an icon imported from the `lucide`
 * package (`import { HandCoins } from 'lucide'`) is a `TmIconNode` as it
 * stands. Attributes whose value is `undefined` are not drawn. The `key`
 * attribute some Lucide packages add for framework list rendering is not an
 * SVG attribute and is not drawn either.
 */
export type TmIconNode = readonly (readonly [
  tag: string,
  attrs: Readonly<Record<string, string | number | undefined>>,
  children?: TmIconNode,
])[];

/**
 * One icon as a caller hands it over: bare {@link TmIconNode} data, or an
 * object carrying that data under `node`.
 *
 * The object form adds `mirrorInRtl`, which flips the glyph horizontally
 * whenever the icon sits in right-to-left text. Give it to every glyph that
 * points along the reading direction (chevrons, arrows, undo and redo, log
 * in and log out, panel toggles), and never to one that does not (a
 * magnifier, a check, a cross). The `@lucide/icons` package's icon objects
 * fit the object form as they are.
 */
export type TmIconSource =
  TmIconNode | { readonly node: TmIconNode; readonly mirrorInRtl?: boolean };

/** One resolved, validated icon: its data and whether it mirrors in RTL. */
interface TmIconEntry {
  /** The validated SVG children. */
  readonly node: TmIconNode;
  /** Whether the glyph flips horizontally under a right-to-left direction. */
  readonly mirrorInRtl: boolean;
}

// The SVG elements an icon may draw: shapes and groups, nothing that runs
// script, loads a resource, embeds foreign content or animates attributes
// (an <animate> or <set> can write a link onto its parent). Every Lucide
// glyph uses only these.
const ALLOWED_TAGS: ReadonlySet<string> = new Set([
  'circle',
  'ellipse',
  'g',
  'line',
  'path',
  'polygon',
  'polyline',
  'rect',
]);

// A name `setAttribute` accepts without throwing (an XML name without the
// rarer Unicode ranges), checked up front so drawing can never fail.
const ATTRIBUTE_NAME = /^[A-Za-z_][\w.:-]*$/;

// Registry names: lowercase words joined by single hyphens ('chevron-right').
const KEBAB_NAME = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/** The error refused icon data throws, naming the icon. */
function refuse(what: string, problem: string): Error {
  return new Error(`TmIconRegistry: ${what} ${problem}`);
}

/**
 * Validates one icon's data and returns it with its mirror flag, or throws.
 * `what` names the icon in the error ('icon "plus"', 'tm-icon [svg] data').
 *
 * The guards: only the shape and group elements above, attribute names
 * `setAttribute` accepts, string or number values, and no attribute that
 * starts with `on` (event handlers), `href` or `xlink:href` (links). Icons
 * are built as DOM nodes, never parsed from markup, so these guards are what
 * keep icon data from carrying script.
 *
 * @internal Shared by the registry and `tm-icon`'s direct data; not exported
 *   from the entry point.
 */
export function tmResolveIconSource(source: TmIconSource, what: string): TmIconEntry {
  // Section: the two accepted shapes.
  let node: unknown;
  let mirrorInRtl = false;
  if (Array.isArray(source)) {
    node = source;
  } else if (typeof source === 'object' && source !== null && 'node' in source) {
    node = source.node;
    if (source.mirrorInRtl !== undefined && typeof source.mirrorInRtl !== 'boolean') {
      throw refuse(what, `has a mirrorInRtl that is not a boolean.`);
    }
    mirrorInRtl = source.mirrorInRtl === true;
  }
  if (!Array.isArray(node)) {
    throw refuse(what, `is not icon data — pass a Lucide icon node or an object with a node.`);
  }
  validateElements(node, what);
  return { node: node as TmIconNode, mirrorInRtl };
}

/** Walks one level of icon data (and, recursively, each element's children). */
function validateElements(elements: readonly unknown[], what: string): void {
  for (const element of elements) {
    if (!Array.isArray(element) || element.length < 2 || element.length > 3) {
      throw refuse(what, `has an element that is not [tag, attributes].`);
    }
    const [tag, attrs, children] = element as [unknown, unknown, unknown];
    if (typeof tag !== 'string' || !ALLOWED_TAGS.has(tag)) {
      throw refuse(
        what,
        `draws a <${String(tag)}> element; icons may draw only ` +
          `${[...ALLOWED_TAGS].join(', ')}.`,
      );
    }
    if (typeof attrs !== 'object' || attrs === null || Array.isArray(attrs)) {
      throw refuse(what, `has a <${tag}> whose attributes are not an object.`);
    }
    for (const [name, value] of Object.entries(attrs)) {
      if (value === undefined) {
        continue;
      }
      const lower = name.toLowerCase();
      if (lower.startsWith('on') || lower.startsWith('href') || lower.startsWith('xlink:href')) {
        throw refuse(
          what,
          `sets the refused attribute "${name}" on a <${tag}>; icon data ` +
            'may carry neither event handlers nor links.',
        );
      }
      if (!ATTRIBUTE_NAME.test(name)) {
        throw refuse(what, `sets an invalid attribute name "${name}".`);
      }
      if (typeof value !== 'string' && typeof value !== 'number') {
        throw refuse(what, `sets "${name}" to a ${typeof value}; values are strings or numbers.`);
      }
    }
    if (children !== undefined) {
      if (!Array.isArray(children)) {
        throw refuse(what, `has a <${tag}> whose children are not an array.`);
      }
      validateElements(children, what);
    }
  }
}

/** The kebab-case spelling of a name, for the error that refuses it. */
function kebabSuggestion(name: string): string {
  return name
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .replace(/[\s_]+/g, '-')
    .toLowerCase();
}

/**
 * The application-wide icon registry: kebab-case names mapped to Lucide icon
 * data, which `<tm-icon name="…">` draws.
 *
 * The library registers nothing itself. The application framework registers
 * the platform's names, and a feature or distribution adds its own, usually
 * through {@link provideTellmaIcons}. A later registration of a name
 * replaces the earlier one, and icons already on screen redraw.
 *
 * Every registration is validated whole before any name is added: a
 * refused attribute or element, data that is not icon data, or a name that
 * is not kebab-case throws, and the registry is left as it was.
 */
@Injectable({ providedIn: 'root' })
export class TmIconRegistry {
  /** The registered icons, by name. */
  private readonly icons = new Map<string, TmIconEntry>();

  /** Bumped on every registration, so lookups made in a reactive context re-run. */
  private readonly version = signal(0);

  /**
   * Adds icons by name, replacing any earlier icon of the same name.
   *
   * @param icons - Kebab-case names mapped to icon data, e.g.
   *   `{ 'hand-coins': HandCoins }` with `HandCoins` imported from `lucide`.
   * @throws Error when a name is not kebab-case or any icon's data is
   *   refused; nothing from the call is registered then.
   */
  register(icons: Readonly<Record<string, TmIconSource>>): void {
    // Section: validate the whole batch first, so a refusal registers nothing.
    const staged: [string, TmIconEntry][] = [];
    for (const [name, source] of Object.entries(icons)) {
      if (!KEBAB_NAME.test(name)) {
        throw new Error(
          `TmIconRegistry: "${name}" is not a kebab-case icon name; write '${kebabSuggestion(name)}'.`,
        );
      }
      staged.push([name, tmResolveIconSource(source, `icon "${name}"`)]);
    }

    // Section: commit, then notify reactive readers once for the batch.
    for (const [name, entry] of staged) {
      this.icons.set(name, entry);
    }
    if (staged.length > 0) {
      this.version.update((version) => version + 1);
    }
  }

  /**
   * Looks a name up. Read inside a reactive context (a template, `computed`
   * or `effect`), the lookup re-runs when a later registration lands.
   *
   * @param name - A registry name such as `chevron-right`.
   * @returns The icon's validated data and mirror flag, or `undefined` when
   *   no icon has that name.
   */
  get(name: string): { readonly node: TmIconNode; readonly mirrorInRtl: boolean } | undefined {
    this.version();
    return this.icons.get(name);
  }
}

/**
 * Registers icons with the {@link TmIconRegistry} when the injector that
 * holds these providers is created: the application's, or a lazily loaded
 * route's. Call it once per feature; a name registered by a later call
 * replaces the earlier icon.
 *
 * @example
 * ```ts
 * import { HandCoins } from 'lucide';
 *
 * providers: [provideTellmaIcons({ 'hand-coins': HandCoins })]
 * ```
 *
 * @param icons - Kebab-case names mapped to icon data.
 * @returns Environment providers for an application or route `providers` array.
 */
export function provideTellmaIcons(
  icons: Readonly<Record<string, TmIconSource>>,
): EnvironmentProviders {
  return provideEnvironmentInitializer(() => inject(TmIconRegistry).register(icons));
}
