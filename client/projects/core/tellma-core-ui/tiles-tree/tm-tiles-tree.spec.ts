// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The tree's behaviour (levels, expansion, the arrows, the batched lazy
// loads and their failure) is covered with the tiles' own tests, whose host
// applies this directive. These tests pin the seam itself: a tiles without
// the directive carries no tree, and the chrome the directive hands the
// tiles draws what each row's place asks for.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import type { TmRowId } from '@tellma/core-ui/contracts';
import { TmTiles, type TmTileLayout } from '@tellma/core-ui/tiles';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmTilesHarness } from '@tellma/core-ui-testing';

import { TmTilesTree } from './tm-tiles-tree';

/** A row of the test tree. */
interface Account {
  readonly id: number;
  readonly name: string;
  readonly parentId: number | null;
  readonly lazy?: boolean;
}

/** A lazy root, a leaf root, and a root with one loaded child. */
const ACCOUNTS: readonly Account[] = [
  { id: 1, name: 'Assets', parentId: null, lazy: true },
  { id: 2, name: 'Income', parentId: null },
  { id: 3, name: 'Equity', parentId: null },
  { id: 31, name: 'Capital', parentId: 3 },
];

/** The tile layout: a title only. */
const LAYOUT: TmTileLayout<Account> = { title: (row) => row.name };

/** The row id reader. */
const idOf = (row: Account): number => row.id;

/** Tiles without tree mode: the directive is not imported. */
@Component({
  imports: [TmTiles],
  template: `
    <tm-tiles
      style="display: block; block-size: 400px; inline-size: 600px"
      ariaLabel="Accounts"
      [data]="rows"
      [rowId]="idOf"
      [layout]="layout"
    />
  `,
})
class FlatHost {
  readonly rows = ACCOUNTS;
  readonly idOf = idOf;
  readonly layout = LAYOUT;
}

/** Tiles with tree mode applied, its parent reader and lazy loader switchable. */
@Component({
  imports: [TmTiles, TmTilesTree],
  template: `
    <tm-tiles
      style="display: block; block-size: 400px; inline-size: 600px"
      ariaLabel="Accounts"
      [data]="rows"
      [rowId]="idOf"
      [layout]="layout"
      [parentId]="parentId()"
      [hasChildren]="hasChildren"
      [loadChildren]="loadChildren()"
      [(expandedIds)]="expanded"
    />
  `,
})
class TreeHost {
  readonly rows = ACCOUNTS;
  readonly idOf = idOf;
  readonly layout = LAYOUT;
  readonly parentId = signal<((row: Account) => TmRowId | null) | undefined>(
    (row) => row.parentId,
  );
  readonly hasChildren = (row: Account): boolean => row.lazy === true;
  readonly loadChildren = signal<((rows: readonly Account[]) => Promise<void>) | undefined>(
    undefined,
  );
  readonly expanded = signal<ReadonlySet<TmRowId>>(new Set([3]));
}

/** The unit page loads no token stylesheet; the geometry reads the tokens. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

/** Renders a host and returns its tiles element and harness. */
async function setup<H>(host: new () => H, configure: (host: H) => void = () => undefined) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await fixture.whenStable();
  const tiles = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmTilesHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector('tm-tiles')!;
  return { fixture, host: fixture.componentInstance, tiles, element };
}

/** The tree chrome each rendered tile shows, by tile title. */
function chrome(element: Element): { title: string; chevron: boolean; slot: boolean }[] {
  return [...element.querySelectorAll('.tm-tiles__item[data-index]')].map((tile) => ({
    title: tile.querySelector('.tm-tiles__title')!.textContent!.trim(),
    chevron: tile.querySelector('[data-tm-twisty] tm-icon') !== null,
    slot: tile.querySelector('.tm-tiles__spinner-slot') !== null,
  }));
}

describe('tm-tiles[parentId]', () => {
  it('leaves tiles without it flat, with no tree chrome or tree states', async () => {
    const { tiles, element } = await setup(FlatHost);
    expect(await tiles.isTree()).toBe(false);
    expect(await tiles.getTileTitles()).toEqual(['Assets', 'Income', 'Equity', 'Capital']);
    expect(element.querySelector('tm-tiles-tree-affordance')).toBeNull();
    expect(element.querySelector('[aria-level], [aria-expanded], [aria-busy]')).toBeNull();
  });

  it('keeps the tiles flat while no parent reader is bound', async () => {
    const { tiles, element } = await setup(TreeHost, (host) => host.parentId.set(undefined));
    expect(await tiles.isTree()).toBe(false);
    expect(await tiles.getTileTitles()).toEqual(['Assets', 'Income', 'Equity', 'Capital']);
    expect(element.querySelector('tm-tiles-tree-affordance')).toBeNull();
    expect(element.querySelector('[aria-level]')).toBeNull();
  });

  it('gives every tree tile a twisty, with a chevron only on the rows that expand', async () => {
    const { tiles, element } = await setup(TreeHost);
    expect(await tiles.isTree()).toBe(true);
    expect(element.querySelectorAll('tm-tiles-tree-affordance')).toHaveLength(4);
    expect(element.querySelectorAll('[data-tm-twisty]')).toHaveLength(4);
    // No lazy loader is bound, so no tile reserves a loading slot.
    expect(chrome(element)).toEqual([
      { title: 'Assets', chevron: true, slot: false },
      { title: 'Income', chevron: false, slot: false },
      { title: 'Equity', chevron: true, slot: false },
      { title: 'Capital', chevron: false, slot: false },
    ]);
    // Only the expanded row's twisty turns open.
    const open = [...element.querySelectorAll('.tm-tiles__twisty--open')];
    const titleOf = (twisty: Element): string =>
      twisty.closest('.tm-tiles__item')!.querySelector('.tm-tiles__title')!.textContent!.trim();
    expect(open.map(titleOf)).toEqual(['Equity']);
  });

  it('reserves a loading slot on every tile once children load lazily, with a spinner while they load', async () => {
    const { fixture, host, tiles, element } = await setup(TreeHost, (h) =>
      h.loadChildren.set(() => new Promise<never>(() => undefined)),
    );
    expect(chrome(element).every((tile) => tile.slot)).toBe(true);
    expect(element.querySelector('tm-spinner')).toBeNull();
    host.expanded.set(new Set([1, 3]));
    await fixture.whenStable();
    const assets = await tiles.getTile({ title: 'Assets' });
    expect(await assets.isLoading()).toBe(true);
    expect(element.querySelectorAll('.tm-tiles__spinner-slot tm-spinner')).toHaveLength(1);
  });

  it('turns the tiles flat again when the parent reader is unbound', async () => {
    const { fixture, host, tiles, element } = await setup(TreeHost);
    host.parentId.set(undefined);
    await fixture.whenStable();
    expect(await tiles.isTree()).toBe(false);
    expect(await tiles.getTileTitles()).toEqual(['Assets', 'Income', 'Equity', 'Capital']);
    expect(element.querySelector('tm-tiles-tree-affordance')).toBeNull();
  });
});
