// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 *
 * `rows`, `rowId`, `rowTile` and the two-way signals are members of the
 * consuming component — a template literal cannot express
 * function-valued inputs. Real usage looks like:
 *
 * ```ts
 * readonly rowId = (row: Customer): number => row.id;
 * readonly rowTile: TmTileLayout<Customer> = {
 *   image: (c) => ({ src: c.imageUrl, alt: c.name }), // src null: a placeholder glyph
 *   title: (c) => c.name,
 *   href: (c) => `/customers/${c.id}`,
 *   topEnd: (c) => ({ text: this.l10n.formatNumber(c.balance), numeric: true }),
 *   bottomStart: (c) => c.code,
 *   bottomEnd: (c) => ({ text: c.isActive ? 'Active' : 'Inactive', badge: c.isActive ? 'success' : 'neutral' }),
 * };
 * ```
 */

/** Records as tiles: a click, a tap or Enter opens one, Mod-click or a middle click in a new tab. */
export const Basic = {
  template: `
    <tm-tiles
      style="block-size: 320px"
      ariaLabel="Products"
      [data]="rows"
      [rowId]="rowId"
      [layout]="rowTile"
      (rowActivate)="onRow($event)"
    />
  `,
};

/** Checkable tiles: the checkbox toggles a tile, Space the active one, Ctrl+Shift+Space every one. */
export const Selectable = {
  template: `
    <tm-tiles
      style="block-size: 320px"
      ariaLabel="Products"
      [data]="rows"
      [rowId]="rowId"
      [layout]="rowTile"
      selectable
      [(selectedIds)]="selected"
      [(activeRowId)]="active"
    />
  `,
};

/** A search field drives the tiles: ArrowDown and ArrowUp move the active tile, Enter opens it. */
export const SearchDriver = {
  template: `
    <input tmInput type="search" aria-label="Search products" [tmGridDriver]="products" />
    <tm-tiles
      #products
      style="block-size: 320px"
      ariaLabel="Products"
      [data]="rows"
      [rowId]="rowId"
      [layout]="rowTile"
      (rowActivate)="onRow($event)"
    />
  `,
};

/** A custom tile body at the same size, plus the loading and empty templates. */
export const CustomContent = {
  template: `
    <tm-tiles
      style="block-size: 320px"
      ariaLabel="Products"
      [data]="rows"
      [rowId]="rowId"
      [layout]="rowTile"
    >
      <ng-template tmTileContent [tmTileContentOf]="rows" let-row>
        <strong>{{ row.name }}</strong> · {{ row.qty }}
      </ng-template>
      <tm-empty-state *tmTilesEmpty heading="No products yet" />
      <tm-spinner *tmTilesLoading />
    </tm-tiles>
  `,
};
