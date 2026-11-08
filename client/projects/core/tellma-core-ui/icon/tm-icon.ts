// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  ElementRef,
  ErrorHandler,
  ViewEncapsulation,
  computed,
  effect,
  inject,
  input,
  isDevMode,
  untracked,
} from '@angular/core';

import {
  TmIconRegistry,
  tmResolveIconSource,
  type TmIconNode,
  type TmIconSource,
} from './tm-icon-registry';

const SVG_NS = 'http://www.w3.org/2000/svg';

// The Lucide drawing contract on every glyph's <svg>. stroke-width is also
// set in CSS from --icon-stroke-width, which wins over this presentation
// attribute; the attribute only keeps a glyph lifted out of the page legible.
const SVG_ATTRIBUTES: Readonly<Record<string, string>> = {
  class: 'tm-icon__svg',
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  'stroke-width': '1.75',
  'stroke-linecap': 'round',
  'stroke-linejoin': 'round',
};

/** What one `tm-icon` should draw, and what went wrong if anything did. */
interface TmIconGlyphState {
  /** The validated glyph, or `undefined` for an empty box. */
  readonly glyph?: { readonly node: TmIconNode; readonly mirrorInRtl: boolean };
  /** The registry name that resolved to nothing, for the development error. */
  readonly unknownName?: string;
  /** Why direct `svg` data was refused. */
  readonly refusal?: Error;
}

// Direct `svg` data is validated once per data object: library glyphs are
// module constants, so every instance after the first is a lookup.
const directSources = new WeakMap<object, TmIconGlyphState>();

// One finished <svg> per icon node, built with createElementNS on first use
// and cloned for every instance after, so a list of a thousand rows costs a
// thousand clones rather than a thousand rebuilds.
const glyphTemplates = new WeakMap<TmIconNode, SVGSVGElement>();

// The unknown names already reported, per registry: one development error
// per name, not one per instance of it.
const reportedUnknownNames = new WeakMap<TmIconRegistry, Set<string>>();

// The refused direct data already reported: one error per data object.
const reportedRefusals = new WeakSet<Error>();

/** Builds (or reuses) the template `<svg>` for an icon node. */
function glyphTemplate(document: Document, node: TmIconNode): SVGSVGElement {
  let template = glyphTemplates.get(node);
  if (template === undefined) {
    template = document.createElementNS(SVG_NS, 'svg');
    for (const [name, value] of Object.entries(SVG_ATTRIBUTES)) {
      template.setAttribute(name, value);
    }
    appendElements(document, template, node);
    glyphTemplates.set(node, template);
  }
  return template;
}

/** Appends validated icon elements (and their children) to an SVG parent. */
function appendElements(document: Document, parent: Element, node: TmIconNode): void {
  for (const [tag, attrs, children] of node) {
    const element = document.createElementNS(SVG_NS, tag);
    for (const [name, value] of Object.entries(attrs)) {
      // `key` is a framework list key some Lucide packages add, not SVG.
      if (value !== undefined && name !== 'key') {
        element.setAttribute(name, String(value));
      }
    }
    if (children !== undefined) {
      appendElements(document, element, children);
    }
    parent.appendChild(element);
  }
}

/**
 * Draws one Lucide icon, by registry name or from icon data passed directly,
 * in a square box sized by `--icon-size` (16px unless a container or the
 * `size` input says otherwise).
 *
 * The glyph is drawn in the change-detection pass that creates the icon, so
 * a control never paints a frame without it. It is built from DOM nodes,
 * never from markup, so it needs no Trusted Types policy, and it strokes in
 * `currentColor` at `--icon-stroke-width` (1.75 by default).
 *
 * Names come from {@link TmIconRegistry}: register them with
 * `provideTellmaIcons({ 'hand-coins': HandCoins })`, importing the data from
 * `lucide`. An unknown name draws an empty box of the same size and, in
 * development, logs one error naming it. `svg` takes icon data directly and
 * bypasses the registry; when both are set, `svg` wins.
 *
 * A container sizes the icons inside it by setting `--icon-size`; the
 * `size` input pins one icon to a step of the ladder instead. A glyph registered with `mirrorInRtl` flips horizontally wherever its
 * text direction is right-to-left, and follows a live direction change.
 *
 * @tmGroup media
 * @tmA11yNotes Decorative by default (aria-hidden). Set `label` when the
 *   icon carries meaning on its own: the host then has role="img" and that
 *   label as its accessible name. An icon-only button names the button, not
 *   the icon.
 */
@Component({
  selector: 'tm-icon',
  // No template: the glyph is appended imperatively (see draw()).
  template: '',
  styleUrl: './tm-icon.css',
  // The stylesheet must reach the imperatively built <svg>, which carries no
  // emulated-encapsulation attribute; every selector is tm-icon scoped.
  encapsulation: ViewEncapsulation.None,
  host: {
    class: 'tm-icon',
    '[class.tm-icon--xs]': 'size() === "xs"',
    '[class.tm-icon--sm]': 'size() === "sm"',
    '[class.tm-icon--md]': 'size() === "md"',
    '[class.tm-icon--lg]': 'size() === "lg"',
    '[class.tm-icon--xl]': 'size() === "xl"',
    '[class.tm-icon--mirror]': 'mirrored()',
    '[attr.role]': 'label() ? "img" : null',
    '[attr.aria-label]': 'label() || null',
    '[attr.aria-hidden]': 'label() ? null : "true"',
  },
})
export class TmIcon {
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly registry = inject(TmIconRegistry);
  private readonly errorHandler = inject(ErrorHandler);

  /** A registry name such as `chevron-right`. */
  readonly name = input<string | undefined>(undefined);

  /**
   * Icon data drawn directly, bypassing the registry: how library code
   * draws its own glyphs. Takes precedence over `name`.
   */
  readonly svg = input<TmIconSource | undefined>(undefined);

  /**
   * A fixed size: `xs` 12px, `sm` 14px, `md` 16px, `lg` 20px, `xl` 24px.
   * Unset, the icon takes `--icon-size` from its container (16px when no
   * container sets one).
   */
  readonly size = input<'xs' | 'sm' | 'md' | 'lg' | 'xl' | undefined>(undefined);

  /**
   * The accessible name, for an icon that carries meaning on its own. Set,
   * the icon is `role="img"` with this `aria-label`; unset, it is hidden
   * from assistive technology.
   */
  readonly label = input<string | undefined>(undefined);

  /** What to draw, resolved from `svg` first and the registry second. */
  private readonly state = computed<TmIconGlyphState>(() => {
    const svg = this.svg();
    if (svg !== undefined) {
      return resolveDirect(svg);
    }
    const name = this.name();
    if (!name) {
      return {};
    }
    const glyph = this.registry.get(name);
    return glyph === undefined ? { unknownName: name } : { glyph };
  });

  /** Whether the drawn glyph flips under a right-to-left direction. */
  protected readonly mirrored = computed(() => this.state().glyph?.mirrorInRtl === true);

  /** The `<svg>` currently in the host, and the node it was cloned from. */
  private drawn: { readonly node: TmIconNode; readonly element: SVGSVGElement } | undefined;

  constructor() {
    // A component effect runs inside the change-detection pass that creates
    // the component, before the frame paints (spec 0023 §3.1 "First frame").
    effect(() => {
      const state = this.state();
      untracked(() => {
        this.draw(state.glyph?.node);
        if (state.refusal !== undefined && !reportedRefusals.has(state.refusal)) {
          reportedRefusals.add(state.refusal);
          this.errorHandler.handleError(state.refusal);
        } else if (state.unknownName !== undefined && isDevMode()) {
          this.reportUnknown(state.unknownName);
        }
      });
    });
  }

  /** Replaces the host's glyph with a clone of `node`'s, or empties the box. */
  private draw(node: TmIconNode | undefined): void {
    if (this.drawn?.node === node) {
      return;
    }
    this.drawn?.element.remove();
    this.drawn = undefined;
    if (node !== undefined) {
      const document = this.host.ownerDocument;
      const element = glyphTemplate(document, node).cloneNode(true) as SVGSVGElement;
      this.host.appendChild(element);
      this.drawn = { node, element };
    }
  }

  /** Logs the development error for an unknown name, once per name. */
  private reportUnknown(name: string): void {
    let reported = reportedUnknownNames.get(this.registry);
    if (reported === undefined) {
      reported = new Set();
      reportedUnknownNames.set(this.registry, reported);
    }
    if (!reported.has(name)) {
      reported.add(name);
      console.error(
        `tm-icon: no icon named "${name}" is registered. Register it with ` +
          `provideTellmaIcons({ '${name}': … }) or TmIconRegistry.register(); ` +
          'an empty box is drawn meanwhile.',
      );
    }
  }
}

/** Validates direct `svg` data once per data object. */
function resolveDirect(source: TmIconSource): TmIconGlyphState {
  let state = directSources.get(source);
  if (state === undefined) {
    try {
      state = { glyph: tmResolveIconSource(source, 'tm-icon [svg] data') };
    } catch (error) {
      state = { refusal: error as Error };
    }
    directSources.set(source, state);
  }
  return state;
}
