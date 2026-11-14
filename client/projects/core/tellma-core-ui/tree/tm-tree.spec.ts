// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Dir } from '@angular/cdk/bidi';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import type { TmDragPayload } from '@tellma/core-ui/drag-drop';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmTreeHarness } from '@tellma/core-ui-testing';

import { TmTree } from './tm-tree';
import { TmTreeNode, type TmTreeNodeState } from './tm-tree-node';

/** A field of the test tree. */
interface Field {
  readonly key: string;
  readonly label: string;
  readonly children?: readonly Field[];
  readonly context?: boolean;
}

const FIELDS: readonly Field[] = [
  {
    key: 'Agent',
    label: 'Agent',
    children: [
      { key: 'Agent.Code', label: 'Code' },
      { key: 'Agent.Name', label: 'Name' },
    ],
  },
  { key: 'Code', label: 'Code' },
  { key: 'Date', label: 'Date', context: true },
];

/** A promise and the functions that settle it. */
function deferred<T>(): {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason: unknown) => void;
} {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

@Component({
  imports: [Dir, TmTree, TmTreeNode],
  template: `
    <div [dir]="dir()">
      <tm-tree
        [nodes]="nodes()"
        [nodeKey]="keyOf"
        [label]="labelOf"
        [children]="children()"
        [selection]="selection()"
        [(selected)]="selected"
        [(expanded)]="expanded"
        [context]="contextOf"
        [dragData]="dragData()"
        ariaLabel="Fields"
        (activate)="activated.push($event.key)"
      >
        @if (custom()) {
          <ng-template tmTreeNode [tmTreeNodeOf]="nodes()" let-node let-state="state">
            <span class="custom">{{ node.label }}</span>
            <i class="state">{{ describe(state) }}</i>
          </ng-template>
        }
      </tm-tree>
    </div>
  `,
})
class Host {
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly nodes = signal<readonly Field[]>(FIELDS);
  readonly children = signal<
    ((node: Field) => readonly Field[] | Promise<readonly Field[]> | null) | undefined
  >((node) => node.children ?? null);
  readonly selection = signal<'none' | 'single' | 'multiple'>('none');
  readonly selected = signal<ReadonlySet<string>>(new Set());
  readonly expanded = signal<ReadonlySet<string>>(new Set());
  readonly dragData = signal<((node: Field) => TmDragPayload | null) | undefined>(undefined);
  readonly custom = signal(false);
  readonly activated: string[] = [];
  readonly keyOf = (node: Field): string => node.key;
  readonly labelOf = (node: Field): string => node.label;
  readonly contextOf = (node: Field): boolean => node.context === true;
  readonly describe = (state: TmTreeNodeState): string =>
    `${state.level} ${state.leaf ? 'leaf' : 'branch'} ${state.expanded ? 'open' : 'closed'}`;
}

/** The unit page loads no token stylesheet; the reserved slots need one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

async function setup(configure: (host: Host) => void = () => undefined) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await fixture.whenStable();
  const tree = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmTreeHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector('tm-tree')!;
  return { fixture, host: fixture.componentInstance, tree, element };
}

describe('tm-tree', () => {
  it('renders the roots, a twisty for each branch and none for a leaf', async () => {
    const { tree } = await setup();
    expect(await tree.getLabel()).toBe('Fields');
    expect(await tree.getNodeTexts()).toEqual(['Agent', 'Code', 'Date']);
    const [agent, code] = await tree.getNodes();
    expect(await agent.isExpandable()).toBe(true);
    expect(await agent.isExpanded()).toBe(false);
    expect(await code.isExpandable()).toBe(false);
    expect(await agent.getLevel()).toBe(1);
    expect(await code.isActive()).toBe(false);
    expect(await agent.isActive()).toBe(true);
  });

  it('expands and collapses with the twisty, writing expanded', async () => {
    const { host, tree } = await setup();
    const agent = await tree.getNode({ key: 'Agent' });
    await agent.expand();
    expect(host.expanded()).toEqual(new Set(['Agent']));
    expect(await tree.getNodeTexts()).toEqual(['Agent', 'Code', 'Name', 'Code', 'Date']);
    expect(await (await tree.getNode({ key: 'Agent.Name' })).getLevel()).toBe(2);
    await agent.collapse();
    expect(host.expanded()).toEqual(new Set());
    expect(await tree.getNodeTexts()).toEqual(['Agent', 'Code', 'Date']);
  });

  it('follows expanded when the host writes it', async () => {
    const { host, tree } = await setup();
    host.expanded.set(new Set(['Agent']));
    expect(await (await tree.getNode({ key: 'Agent' })).isExpanded()).toBe(true);
    expect(await tree.getNodes({ key: 'Agent.Code' })).toHaveLength(1);
  });

  it('moves with Up and Down, expands with Right and returns to the parent with Left', async () => {
    const { host, tree } = await setup();
    await tree.press('ArrowRight');
    expect(host.expanded().has('Agent')).toBe(true);
    await tree.press('ArrowRight');
    expect(await (await tree.getNode({ key: 'Agent.Code' })).isFocused()).toBe(true);
    await tree.press('ArrowDown');
    expect(await (await tree.getNode({ key: 'Agent.Name' })).isFocused()).toBe(true);
    await tree.press('ArrowLeft');
    expect(await (await tree.getNode({ key: 'Agent' })).isFocused()).toBe(true);
    await tree.press('ArrowLeft');
    expect(host.expanded().has('Agent')).toBe(false);
    await tree.press('End');
    expect(await (await tree.getNode({ key: 'Date' })).isFocused()).toBe(true);
  });

  it('mirrors Right and Left in a right-to-left page', async () => {
    const { host, tree } = await setup((h) => h.dir.set('rtl'));
    await tree.press('ArrowLeft');
    expect(host.expanded().has('Agent')).toBe(true);
    await tree.press('ArrowRight');
    expect(host.expanded().has('Agent')).toBe(false);
  });

  it('jumps by the first letters of a label', async () => {
    const { tree } = await setup();
    const agent = await tree.getNode({ key: 'Agent' });
    await agent.focus();
    await agent.type('d');
    expect(await (await tree.getNode({ key: 'Date' })).isFocused()).toBe(true);
  });

  it('selects nothing in none mode, and activates on Enter and on a double-click on a leaf', async () => {
    const { host, tree } = await setup();
    const code = await tree.getNode({ key: 'Code' });
    await code.click();
    expect(await code.isSelected()).toBe(false);
    expect(host.selected()).toEqual(new Set());
    await code.press('Enter');
    await code.doubleClick();
    expect(host.activated).toEqual(['Code', 'Code']);
  });

  it('expands a branch on a double-click, without activating it', async () => {
    const { host, tree } = await setup();
    const agent = await tree.getNode({ key: 'Agent' });
    await agent.doubleClick();
    expect(host.expanded()).toEqual(new Set(['Agent']));
    expect(host.activated).toEqual([]);
    await agent.press('Enter');
    expect(host.activated).toEqual(['Agent']);
  });

  it('selects one node in single mode by click and by Enter, which also activates', async () => {
    const { host, tree } = await setup((h) => h.selection.set('single'));
    const [agent, code] = await tree.getNodes();
    await code.click();
    expect(host.selected()).toEqual(new Set(['Code']));
    expect(await code.isSelected()).toBe(true);
    await agent.press('Enter');
    expect(host.selected()).toEqual(new Set(['Agent']));
    expect(host.activated).toEqual(['Agent']);
    expect(await tree.isMultiSelectable()).toBe(false);
  });

  it('toggles nodes in multiple mode by click, Space and Enter, and never activates', async () => {
    const { host, tree } = await setup((h) => h.selection.set('multiple'));
    expect(await tree.isMultiSelectable()).toBe(true);
    const [agent, code, date] = await tree.getNodes();
    await code.click();
    await date.click();
    expect(host.selected()).toEqual(new Set(['Code', 'Date']));
    await code.click();
    expect(host.selected()).toEqual(new Set(['Date']));
    await agent.press('Space');
    await agent.press('Enter');
    await agent.press('Space');
    expect(host.selected()).toEqual(new Set(['Date', 'Agent']));
    await code.doubleClick();
    expect(host.activated).toEqual([]);
  });

  it('does not select when the twisty is clicked', async () => {
    const { host, tree } = await setup((h) => h.selection.set('single'));
    await (await tree.getNode({ key: 'Agent' })).toggle();
    expect(host.expanded()).toEqual(new Set(['Agent']));
    expect(host.selected()).toEqual(new Set());
  });

  it('draws context nodes muted and makes rows drag sources with dragData', async () => {
    const { host, tree } = await setup();
    expect(await (await tree.getNode({ key: 'Date' })).isContext()).toBe(true);
    expect(await (await tree.getNode({ key: 'Code' })).isContext()).toBe(false);
    expect(await (await tree.getNode({ key: 'Code' })).isDraggable()).toBe(false);
    host.dragData.set((node) =>
      node.children ? null : { kind: 'tm.path', data: node, text: node.key, label: node.label },
    );
    expect(await (await tree.getNode({ key: 'Code' })).isDraggable()).toBe(true);
    expect(await (await tree.getNode({ key: 'Agent' })).isDraggable()).toBe(false);
  });

  it('gives a row template the node and its state', async () => {
    const { tree, element } = await setup((h) => h.custom.set(true));
    await (await tree.getNode({ key: 'Agent' })).expand();
    const states = Array.from(element.querySelectorAll('.state')).map((node) => node.textContent);
    expect(states).toEqual([
      '1 branch open',
      '2 leaf closed',
      '2 leaf closed',
      '1 leaf closed',
      '1 leaf closed',
    ]);
  });

  describe('lazy children', () => {
    it('shows a spinner in the reserved slot while a promise loads, then the children', async () => {
      const load = deferred<readonly Field[]>();
      const { fixture, tree, element } = await setup((h) =>
        h.children.set((node) => (node.key === 'Agent' ? load.promise : null)),
      );
      const agent = await tree.getNode({ key: 'Agent' });
      const content = element.querySelector('[data-key="Agent"] .tm-tree__content')!;
      const before = content.getBoundingClientRect().left;
      expect(await agent.isExpandable()).toBe(true);
      expect(await agent.hasSpinner()).toBe(false);
      await agent.expand();
      expect(await agent.isLoading()).toBe(true);
      expect(await agent.hasSpinner()).toBe(true);
      expect(content.getBoundingClientRect().left).toBe(before);
      const row = element.querySelector('[data-key="Agent"]')!;
      const names = row
        .getAttribute('aria-labelledby')!
        .split(' ')
        .map((id) => document.getElementById(id)!.textContent!.trim());
      expect(names).toEqual(['Agent', 'Loading']);

      load.resolve(FIELDS[0].children!);
      await fixture.whenStable();
      expect(await agent.isLoading()).toBe(false);
      expect(await agent.hasSpinner()).toBe(false);
      expect(await tree.getNodeTexts()).toEqual(['Agent', 'Code', 'Name', 'Code', 'Date']);
    });

    it('runs children once per node until nodes changes', async () => {
      const calls: string[] = [];
      const { fixture, host, tree } = await setup((h) =>
        h.children.set((node) => {
          calls.push(node.key);
          return node.children ?? null;
        }),
      );
      const agent = await tree.getNode({ key: 'Agent' });
      await agent.expand();
      await agent.collapse();
      await agent.expand();
      expect(calls.filter((key) => key === 'Agent')).toEqual(['Agent']);
      host.nodes.set([...FIELDS]);
      await fixture.whenStable();
      expect(calls.filter((key) => key === 'Agent')).toEqual(['Agent', 'Agent']);
    });

    it('turns a branch whose promise brings no children into a leaf', async () => {
      const load = deferred<readonly Field[]>();
      const { fixture, host, tree } = await setup((h) =>
        h.children.set((node) => (node.key === 'Agent' ? load.promise : null)),
      );
      const agent = await tree.getNode({ key: 'Agent' });
      await agent.expand();
      load.resolve([]);
      await fixture.whenStable();
      expect(await agent.isExpandable()).toBe(false);
      expect(host.expanded()).toEqual(new Set());
    });

    it('collapses a node whose load fails, announces it, and tries again on the next expansion', async () => {
      let load = deferred<readonly Field[]>();
      const { fixture, host, tree } = await setup((h) =>
        h.children.set((node) => (node.key === 'Agent' ? load.promise : null)),
      );
      const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
      const agent = await tree.getNode({ key: 'Agent' });
      await agent.expand();
      const first = load;
      load = deferred<readonly Field[]>();
      first.reject(new Error('offline'));
      await fixture.whenStable();
      expect(host.expanded()).toEqual(new Set());
      expect(await agent.isExpanded()).toBe(false);
      expect(announce).toHaveBeenCalledWith("Couldn't load Agent", 'polite');

      await agent.expand();
      expect(await agent.isLoading()).toBe(true);
      load.resolve(FIELDS[0].children!);
      await fixture.whenStable();
      expect(await tree.getNodeTexts()).toEqual(['Agent', 'Code', 'Name', 'Code', 'Date']);
    });

    it('fails quietly for a node nobody opened', async () => {
      const load = deferred<readonly Field[]>();
      const { fixture, tree } = await setup((h) =>
        h.children.set((node) => (node.key === 'Agent' ? load.promise : null)),
      );
      const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
      load.reject(new Error('offline'));
      await fixture.whenStable();
      expect(announce).not.toHaveBeenCalled();
      expect(await (await tree.getNode({ key: 'Agent' })).isExpandable()).toBe(true);
    });
  });

  it('warns once in a dev build when more than 500 nodes are open', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const many: Field[] = Array.from({ length: 501 }, (_, index) => ({
      key: `F${index}`,
      label: `Field ${index}`,
    }));
    const { fixture, host } = await setup((h) => h.nodes.set(many));
    host.nodes.set([...many]);
    await fixture.whenStable();
    expect(warn).toHaveBeenCalledTimes(1);
    expect(warn.mock.calls[0][0]).toContain('501 nodes are open');
    warn.mockRestore();
  });
});
