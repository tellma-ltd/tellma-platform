// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked, type InputSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Check } from 'lucide';
import {
  TmDisplayName,
  TmDisplayNames,
  TmEntities,
  TmLocalization,
  TmPermissions,
  TmUrls,
  type TmBlobKindPolicy,
  type TmColumnPlan,
  type TmDisplayNameParts,
  type TmId,
  type TmJson,
  type TmJsonObject,
  type TmPathResolution,
  type TmPlanRow,
  type TmPropertyDescriptor,
  type TmValueDescriptor,
} from '@tellma/core';
import { TmAvatar } from '@tellma/core-ui/avatar';
import { TmBadge } from '@tellma/core-ui/badge';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmImage } from '@tellma/core-ui/image';

import { TmFormats, type TmValueStyle } from './tm-formats';
import { ɵtmPlanBlobPath } from './tm-select-plan';

// A typed value in view mode (spec 0029 §12.2's text column): the text
// `TmFormats` gives, and the markup some types take beyond text. An id
// renders its row's display name, a link when the user may open it, with an
// avatar when the entity has an image; an enum value renders a badge; an
// image renders a thumbnail, requested only when the blob lists one; a file
// renders its name as a download link; a guid or a time renders mono.

/** One related row a value names. */
interface TmValueItem {
  /** The row's display name, as its parts; `null` while it is unknown. */
  readonly parts: TmDisplayNameParts | null;
  /** The row's details page, when the user may open it. */
  readonly href: string | null;
  /** The row's avatar, when the entity has an image. */
  readonly avatar: { readonly name: string; readonly colorKey: string; readonly src: string | null } | null;
}

/** What a value renders as. */
type TmValueView =
  | { readonly kind: 'text'; readonly text: string; readonly mono: boolean; readonly dir: string }
  | { readonly kind: 'none'; readonly text: string }
  | { readonly kind: 'badges'; readonly labels: readonly string[] }
  | { readonly kind: 'flag'; readonly on: boolean; readonly label: string }
  | { readonly kind: 'rows'; readonly items: readonly TmValueItem[] }
  | { readonly kind: 'image'; readonly src: string; readonly alt: string }
  | { readonly kind: 'file'; readonly name: string; readonly href: string };

/** A blob a value names, with what the value knows of it. */
interface TmBlobRef {
  /** The blob's id. */
  readonly id: number;
  /** The variants the blob has, comma separated; `undefined` when the value does not say. */
  readonly variants?: string;
  /** The file's name, when the value says. */
  readonly fileName?: string;
}

/** A blob URL is complete as built; the image asks for no size of its own. */
const AS_IS = (src: string): string => src;

/** The width and height of a thumbnail, by surface. */
const THUMBNAIL: Readonly<Record<TmValueStyle, number>> = { grid: 32, details: 96 };

/** Whether a value is absent: `null`, `undefined` or empty text. */
function empty(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}

/** A wire value as an object, or `null`. */
function objectOf(value: unknown): TmJsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as TmJsonObject) : null;
}

/**
 * Renders a typed value in view mode: formatted text, an enum's badge, a
 * related row's display name as a link with its avatar, a thumbnail, or a
 * file's download link. `property` takes a value descriptor, a path
 * resolution, or a select plan's column, whose `value` is then the row.
 *
 * @example
 * ```html
 * <tm-value [property]="customer.property('CreditLimit')!" [value]="record.creditLimit" />
 * <tm-value [property]="column" [value]="row" surface="grid" />
 * ```
 */
@Component({
  selector: 'tm-value',
  imports: [RouterLink, TmAvatar, TmBadge, TmDisplayName, TmIcon, TmImage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-value' },
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--tm-space-2, 0.5rem);
      flex-wrap: wrap;
      min-inline-size: 0;
    }
    .tm-value__mono {
      font-family: var(--tm-font-mono, monospace);
    }
    .tm-value__none {
      color: var(--tm-color-text-subtle, inherit);
    }
    .tm-value__hidden {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
    .tm-value__row {
      display: inline-flex;
      align-items: center;
      gap: var(--tm-space-1, 0.25rem);
    }
  `,
  template: `
    @let shown = view();
    @switch (shown.kind) {
      <!-- Formatted text: mono for a guid or a time, a slot in its content direction -->
      @case ('text') {
        <span [class.tm-value__mono]="shown.mono" [attr.dir]="shown.dir">{{ shown.text }}</span>
      }
      @case ('none') {
        <span class="tm-value__none">{{ shown.text }}</span>
      }
      <!-- An enum value, or each value of a list, as a badge -->
      @case ('badges') {
        @for (label of shown.labels; track $index) {
          <tm-badge tone="neutral">{{ label }}</tm-badge>
        }
      }
      <!-- A flag in a grid: a check for yes, nothing visible for no; each named for assistive technology -->
      @case ('flag') {
        @if (shown.on) {
          <tm-icon size="sm" [svg]="checkIcon" [label]="shown.label" />
        } @else {
          <span class="tm-value__hidden">{{ shown.label }}</span>
        }
      }
      <!-- Related rows: avatar, then the display name, a link when the user may open it -->
      @case ('rows') {
        @for (item of shown.items; track $index) {
          <span class="tm-value__row">
            @if (item.avatar; as avatar) {
              <tm-avatar size="xs" decorative [name]="avatar.name" [colorKey]="avatar.colorKey" [src]="avatar.src" />
            }
            @if (item.href; as href) {
              <a [routerLink]="href"><tm-display-name [parts]="item.parts" /></a>
            } @else {
              <tm-display-name [parts]="item.parts" />
            }
          </span>
        }
      }
      <!-- A thumbnail, its box fixed so the layout never moves -->
      @case ('image') {
        <tm-image mode="view" [src]="shown.src" [alt]="shown.alt" [width]="size()" [height]="size()" [srcForSize]="asIs" />
      }
      <!-- A file's name, downloading it -->
      @case ('file') {
        <a [attr.href]="shown.href" download dir="auto">{{ shown.name }}</a>
      }
    }
  `,
})
export class TmValue {
  readonly #formats = inject(TmFormats);
  readonly #names = inject(TmDisplayNames);
  readonly #entities = inject(TmEntities);
  readonly #permissions = inject(TmPermissions);
  readonly #urls = inject(TmUrls);
  readonly #localization = inject(TmLocalization);

  /** What the value is: its type facts, a path read against an entity, or a select plan's column. */
  readonly property: InputSignal<TmValueDescriptor | TmPathResolution | TmColumnPlan> = input.required<
    TmValueDescriptor | TmPathResolution | TmColumnPlan
  >();

  /** The value as the wire sends it; for a select plan's column, the row. */
  readonly value: InputSignal<unknown> = input<unknown>(null);

  /** The surface: a details view shows "—" for no value and a larger thumbnail; a grid shows nothing. */
  readonly surface: InputSignal<TmValueStyle> = input<TmValueStyle>('details');

  /** The glyph a flag that is set shows in a grid. */
  protected readonly checkIcon = Check;

  /** A blob URL as built, for the thumbnail. */
  protected readonly asIs = AS_IS;

  /** The thumbnail's box, in pixels. */
  protected readonly size = computed(() => THUMBNAIL[this.surface()]);

  /** The ids whose display names are unknown, fetched in the background. */
  readonly #unknown = computed(() => {
    const view = this.view();
    return view.kind === 'rows' && view.items.some((item) => item.parts === null) ? this.#target() : null;
  });

  /** What the value renders as. */
  protected readonly view = computed<TmValueView>(() => {
    const property = this.property();
    const value = this.value();
    const style = this.surface();

    // --- A plan's column: the row's cell, read by the column ---
    if ('orderBy' in property) {
      return this.#columnView(property, value as TmPlanRow, style);
    }

    // --- A path: what its end names ---
    if ('end' in property) {
      const { end } = property;
      switch (end.kind) {
        case 'value':
          return this.#valueView(value, end.property, style);
        case 'text':
          return this.#valueView(value, end.slots[0]!, style);
        case 'navigation':
          return this.#rowsView(end.navigation.target, value, style);
        case 'image':
        case 'file':
          return this.#blobView(end.kind, end.policy, this.#blob(value), property.label(), style);
      }
    }

    // --- Type facts alone ---
    const blob = (property as Partial<TmPropertyDescriptor>).blob;
    if (blob !== undefined) {
      return this.#blobView(blob.image === null ? 'file' : 'image', blob, this.#blob(value), '', style);
    }
    return this.#valueView(value, property, style);
  });

  constructor() {
    // A related row shown by id alone is named once its display name is read.
    effect(() => {
      const unknown = this.#unknown();
      if (unknown !== null) {
        untracked(() => this.#names.resolve(unknown.entity, unknown.ids));
      }
    });
  }

  /** The related entity and the ids the value names, for the background read. */
  readonly #target = computed(() => {
    const property = this.property();
    const value = this.value();
    let entity: string | undefined;
    let ids: readonly unknown[];
    if ('orderBy' in property) {
      entity = property.descriptor?.target;
      ids = [property.value(value as TmPlanRow)];
    } else if ('end' in property) {
      entity = property.end.kind === 'navigation' ? property.end.navigation.target : undefined;
      ids = Array.isArray(value) ? value : [value];
    } else {
      entity = property.target;
      ids = Array.isArray(value) ? value : [value];
    }
    const named = ids.filter((id): id is TmId => typeof id === 'number' || typeof id === 'string');
    return entity === undefined ? null : { entity, ids: named };
  });

  /**
   * A plan's cell.
   *
   * @param column - The column.
   * @param row - The row.
   * @param style - The surface.
   * @returns The view.
   */
  #columnView(column: TmColumnPlan, row: TmPlanRow, style: TmValueStyle): TmValueView {
    const value = column.value(row);
    switch (column.kind) {
      case 'navigation': {
        const target = column.descriptor?.target;
        if (target === undefined || empty(value)) {
          return this.#none(style);
        }
        return { kind: 'rows', items: [this.#item(target, value as TmId, this.#formats.ɵrelated(column, row))] };
      }
      case 'image':
      case 'file': {
        const path = ɵtmPlanBlobPath(column);
        const end = column.resolution?.end;
        if (empty(value) || path === undefined || (end?.kind !== 'image' && end?.kind !== 'file')) {
          return this.#none(style);
        }
        const blob: TmBlobRef = {
          id: Number(value),
          variants: String(row.get(`${path}.Variants`) ?? ''),
          ...(column.kind === 'file' && { fileName: String(row.get(`${path}.FileName`) ?? '') }),
        };
        return this.#blobView(column.kind, end.policy, blob, column.label(), style);
      }
      case 'text': {
        const text = this.#formats.cell(column, row);
        return text === '' ? this.#none(style) : { kind: 'text', text, mono: false, dir: 'auto' };
      }
      default:
        return column.descriptor === null
          ? { kind: 'text', text: this.#formats.cell(column, row), mono: false, dir: 'auto' }
          : this.#valueView(value, column.descriptor, style);
    }
  }

  /**
   * A value by its type facts.
   *
   * @param value - The value.
   * @param of - Its type facts.
   * @param style - The surface.
   * @returns The view.
   */
  #valueView(value: unknown, of: TmValueDescriptor & { readonly contentDir?: string }, style: TmValueStyle): TmValueView {
    if (empty(value) || (Array.isArray(value) && value.length === 0)) {
      return this.#none(style);
    }
    if (of.target !== undefined) {
      return this.#rowsView(of.target, value, style);
    }
    if (of.type === 'bool' && style === 'grid' && !Array.isArray(value)) {
      return { kind: 'flag', on: value === true, label: this.#formats.text(value, of, style) };
    }
    if (of.type === 'enum') {
      const item = { ...of, array: undefined };
      return { kind: 'badges', labels: (Array.isArray(value) ? value : [value]).map((element) => this.#formats.text(element, item, style)) };
    }
    return {
      kind: 'text',
      text: this.#formats.text(value, of, style),
      mono: of.type === 'guid' || of.type === 'time',
      dir: of.contentDir ?? 'auto',
    };
  }

  /**
   * Related rows: by id, or as wire rows a details read returned.
   *
   * @param entity - The related entity.
   * @param value - An id, a wire row, or a list of either.
   * @param style - The surface.
   * @returns The view.
   */
  #rowsView(entity: string, value: unknown, style: TmValueStyle): TmValueView {
    const descriptor = this.#entities.loaded() ? this.#entities.get(entity) : null;
    const keyJson = descriptor?.property(descriptor.key.property)?.json ?? 'id';
    const items = (Array.isArray(value) ? value : [value])
      .filter((element) => !empty(element))
      .map((element) => {
        const row = objectOf(element);
        return this.#item(entity, (row ? row[keyJson] : element) as TmId, row);
      });
    return items.length === 0 ? this.#none(style) : { kind: 'rows', items };
  }

  /**
   * One related row: its name, link and avatar.
   *
   * @param entity - The related entity.
   * @param id - The row's id.
   * @param row - The row as a wire object, when the value carries it.
   * @returns The item.
   */
  #item(entity: string, id: TmId, row: TmJsonObject | null): TmValueItem {
    const parts = row ? this.#names.parts(entity, row)() : this.#names.labelParts(entity, id)();
    const href = this.#permissions.allows(entity, 'details')() ? this.#urls.target(entity, id) : null;

    // --- The avatar, for an entity whose related projection carries an image ---
    let avatar: TmValueItem['avatar'] = null;
    const descriptor = this.#entities.loaded() ? this.#entities.get(entity) : null;
    const image = descriptor?.relatedSelect
      .map((name) => descriptor.property(name))
      .find((property) => property?.blob?.image !== null && property?.blob !== undefined);
    if (descriptor && image) {
      const imageId = row?.[image.json];
      avatar = {
        name: parts ? (parts.prefix === null ? parts.name : `${parts.prefix} - ${parts.name}`) : '',
        colorKey: `${descriptor.name}:${String(id)}`,
        src: typeof imageId === 'number' ? this.#urls.blob(image.blob!.kind, imageId) : null,
      };
    }
    return { parts, href, avatar };
  }

  /**
   * A blob: a thumbnail, or a file's download link.
   *
   * @param kind - Whether the blob is an image or a file.
   * @param policy - Its kind's policy.
   * @param blob - The blob, or `null` for none.
   * @param alt - The image's text alternative.
   * @param style - The surface.
   * @returns The view.
   */
  #blobView(kind: 'image' | 'file', policy: TmBlobKindPolicy, blob: TmBlobRef | null, alt: string, style: TmValueStyle): TmValueView {
    if (blob === null) {
      return this.#none(style);
    }
    if (kind === 'image') {
      // The thumbnail is asked for only when the blob lists it.
      const thumb = policy.thumbnail && (blob.variants ?? '').split(',').some((variant) => variant.trim() === 'thumb');
      return { kind: 'image', src: this.#urls.blob(policy.kind, blob.id, thumb ? { variant: 'thumb' } : undefined), alt };
    }
    return {
      kind: 'file',
      name: blob.fileName || this.#localization.ɵtext('tm.value.file')(),
      href: this.#urls.blob(policy.kind, blob.id, { download: true }),
    };
  }

  /**
   * The blob a value names: an id, or a blob's wire row.
   *
   * @param value - The value.
   * @returns The blob, or `null` for none.
   */
  #blob(value: unknown): TmBlobRef | null {
    const row = objectOf(value);
    const id = row ? row['id'] : value;
    if (typeof id !== 'number' && !(typeof id === 'string' && /^\d+$/.test(id))) {
      return null;
    }
    const text = (member: string): string | undefined => {
      const cell: TmJson | undefined = row?.[member];
      return typeof cell === 'string' ? cell : undefined;
    };
    const variants = text('variants');
    const fileName = text('fileName');
    return { id: Number(id), ...(variants !== undefined && { variants }), ...(fileName !== undefined && { fileName }) };
  }

  /**
   * No value: empty in a grid, "—" in a details view.
   *
   * @param style - The surface.
   * @returns The view.
   */
  #none(style: TmValueStyle): TmValueView {
    return { kind: 'none', text: style === 'grid' ? '' : this.#formats.text(null, { type: 'string', nullable: true }, 'details') };
  }
}
