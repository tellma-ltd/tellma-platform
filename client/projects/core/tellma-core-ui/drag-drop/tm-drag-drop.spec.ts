// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { TmDragDrop, type TmDragPayload, type TmDrop } from './tm-drag-drop';
import { TmDragSource } from './tm-drag-source';
import { TmDropTarget } from './tm-drop-target';

/** The drag-id type a source writes beside its text. */
const DRAG_TYPE = 'application/x-tellma-drag';

const path: TmDragPayload = { kind: 'tm.path', data: { key: 'Code' }, text: 'Code', label: 'Code' };

@Component({
  imports: [TmDragSource, TmDropTarget],
  template: `
    <div id="outer" tmDropTarget [accepts]="outerAccepts()" (dropped)="outerDrops.push($event)">
      <span id="inside" [tmDragSource]="insidePayload()" (dragEnded)="ends.push($event)"
        >Inside</span
      >
      <div id="inner" tmDropTarget [accepts]="['tm.path']" (dropped)="innerDrops.push($event)">
        <b id="inner-child">Child</b>
      </div>
    </div>
    @if (showSource()) {
      <span id="source" [tmDragSource]="payload()" (dragEnded)="ends.push($event)">Code</span>
    }
  `,
})
class Host {
  readonly payload = signal<TmDragPayload | null>(path);
  readonly insidePayload = signal<TmDragPayload | null>(path);
  readonly outerAccepts = signal<readonly string[]>(['tm.path']);
  readonly showSource = signal(true);
  readonly outerDrops: TmDrop[] = [];
  readonly innerDrops: TmDrop[] = [];
  readonly ends: { dropped: boolean }[] = [];
}

/**
 * A drag's data, as a plain object: outside a real drag the browser's own
 * DataTransfer ignores writes to its effects, which these tests read.
 */
function transfer(extraTypes: readonly string[] = []): DataTransfer {
  const data = new Map<string, string>();
  const fake = {
    effectAllowed: 'none',
    dropEffect: 'none',
    get types(): string[] {
      return [...data.keys(), ...extraTypes];
    },
    setData: (type: string, value: string): void => void data.set(type, value),
    getData: (type: string): string => data.get(type) ?? '',
  };
  return fake as unknown as DataTransfer;
}

/** A synthetic drag event carrying `data`. */
function drag(type: string, data: DataTransfer): DragEvent {
  const event = new DragEvent(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'dataTransfer', { value: data });
  return event;
}

function setup(): { fixture: ComponentFixture<Host>; host: Host; at: (id: string) => HTMLElement } {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    host: fixture.componentInstance,
    at: (id) => root.querySelector<HTMLElement>(`#${id}`)!,
  };
}

/** Starts a drag on `source` and returns its data. */
function start(source: HTMLElement): DataTransfer {
  const data = transfer();
  source.dispatchEvent(drag('dragstart', data));
  return data;
}

describe('tmDragSource', () => {
  it('is draggable only with a payload', () => {
    const { fixture, host, at } = setup();
    expect(at('source').getAttribute('draggable')).toBe('true');
    host.payload.set(null);
    fixture.detectChanges();
    expect(at('source').hasAttribute('draggable')).toBe(false);
  });

  it('writes the text and a drag id, allows copy and move, and records the payload', () => {
    const { at } = setup();
    const data = start(at('source'));
    expect(data.effectAllowed).toBe('copyMove');
    expect(data.getData('text/plain')).toBe('Code');
    expect(data.getData(DRAG_TYPE)).toMatch(/^tm-drag-/);
    const dragDrop = TestBed.inject(TmDragDrop);
    expect(dragDrop.current()).toBe(path);
    at('source').dispatchEvent(drag('dragend', data));
    expect(dragDrop.current()).toBeNull();
  });

  it('reports a cancelled drag as not dropped, and a drop into a text field as dropped', () => {
    const { host, at } = setup();
    let data = start(at('source'));
    data.dropEffect = 'none';
    at('source').dispatchEvent(drag('dragend', data));
    data = start(at('source'));
    data.dropEffect = 'copy';
    at('source').dispatchEvent(drag('dragend', data));
    expect(host.ends).toEqual([{ dropped: false }, { dropped: true }]);
  });

  it('ends the drag of a source destroyed mid-drag at the next sign of no drag', () => {
    const { fixture, host, at } = setup();
    start(at('source'));
    host.showSource.set(false);
    fixture.detectChanges();
    const dragDrop = TestBed.inject(TmDragDrop);
    expect(dragDrop.current()).toBe(path);
    window.dispatchEvent(new PointerEvent('pointermove'));
    expect(dragDrop.current()).toBeNull();
  });

  it('ends a drag whose own dragend is lost at the first drag end or drop the window sees', () => {
    const { host, at } = setup();
    const dragDrop = TestBed.inject(TmDragDrop);
    for (const type of ['dragend', 'drop']) {
      start(at('source'));
      expect(dragDrop.current()).toBe(path);
      window.dispatchEvent(new DragEvent(type));
      expect(dragDrop.current()).toBeNull();
    }
    // The source never saw its dragend: it reports nothing.
    expect(host.ends).toEqual([]);
  });
});

describe('tmDropTarget', () => {
  it('allows an accepted drag, with the copy cursor from outside, and tints while it hovers', () => {
    const { fixture, at } = setup();
    const data = start(at('source'));
    const over = drag('dragover', data);
    at('inner').dispatchEvent(drag('dragenter', data));
    at('inner').dispatchEvent(over);
    fixture.detectChanges();
    expect(over.defaultPrevented).toBe(true);
    expect(data.dropEffect).toBe('copy');
    expect(at('inner').classList).toContain('tm-drop-target--over');
  });

  it('shows the move cursor for a drag that started inside it', () => {
    const { at } = setup();
    const data = start(at('inside'));
    at('outer').dispatchEvent(drag('dragenter', data));
    at('outer').dispatchEvent(drag('dragover', data));
    expect(data.dropEffect).toBe('move');
  });

  it('leaves a drag of another kind to the browser', () => {
    const { fixture, host, at } = setup();
    host.payload.set({ ...path, kind: 'tm.other' });
    fixture.detectChanges();
    const data = start(at('source'));
    const over = drag('dragover', data);
    at('outer').dispatchEvent(drag('dragenter', data));
    at('outer').dispatchEvent(over);
    fixture.detectChanges();
    expect(over.defaultPrevented).toBe(false);
    expect(at('outer').classList).not.toContain('tm-drop-target--over');
  });

  it('stays over while the drag crosses its children, and clears when it leaves', () => {
    const { fixture, at } = setup();
    const data = start(at('source'));
    const inner = at('inner');
    inner.dispatchEvent(drag('dragenter', data));
    at('inner-child').dispatchEvent(drag('dragenter', data));
    inner.dispatchEvent(new DragEvent('dragleave', { bubbles: false }));
    fixture.detectChanges();
    expect(inner.classList).toContain('tm-drop-target--over');
    at('inner-child').dispatchEvent(drag('dragleave', data));
    fixture.detectChanges();
    expect(inner.classList).not.toContain('tm-drop-target--over');
  });

  it('emits a drop once, from the innermost target that accepts it', () => {
    const { host, at } = setup();
    const data = start(at('source'));
    const drop = drag('drop', data);
    at('inner-child').dispatchEvent(drop);
    expect(drop.defaultPrevented).toBe(true);
    expect(host.innerDrops.map((entry) => entry.payload)).toEqual([path]);
    expect(host.outerDrops).toEqual([]);
    at('source').dispatchEvent(drag('dragend', data));
    expect(host.ends).toEqual([{ dropped: true }]);
  });

  it('lets an outer target take what the inner one refuses', () => {
    const { fixture, host, at } = setup();
    host.payload.set({ ...path, kind: 'tm.field' });
    host.outerAccepts.set(['tm.field']);
    fixture.detectChanges();
    const data = start(at('source'));
    at('inner-child').dispatchEvent(drag('drop', data));
    expect(host.innerDrops).toEqual([]);
    expect(host.outerDrops.map((entry) => entry.payload.kind)).toEqual(['tm.field']);
  });

  it('ignores a drop whose id is not the drag in flight, such as a file or another window', () => {
    const { host, at } = setup();
    start(at('source'));
    const foreign = transfer();
    foreign.setData('text/plain', 'Code');
    foreign.setData(DRAG_TYPE, 'tm-drag-0-elsewhere');
    const drop = drag('drop', foreign);
    at('inner').dispatchEvent(drop);
    at('inner').dispatchEvent(drag('drop', transfer()));
    expect(drop.defaultPrevented).toBe(false);
    expect(host.innerDrops).toEqual([]);
  });

  it('takes a drop that lost its drag id when it carries the text of the drag in flight', () => {
    const { host, at } = setup();
    start(at('source'));
    // Some engines deliver only the standard types to the drop.
    const stripped = transfer();
    stripped.setData('text/plain', 'Code');
    const drop = drag('drop', stripped);
    at('inner').dispatchEvent(drop);
    expect(drop.defaultPrevented).toBe(true);
    expect(host.innerDrops.map((entry) => entry.payload)).toEqual([path]);
  });

  it('refuses the id-less match for a drop that carries files, or for a drag with no text', () => {
    const { fixture, host, at } = setup();
    start(at('source'));
    const files = transfer(['Files']);
    files.setData('text/plain', 'Code');
    at('inner').dispatchEvent(drag('drop', files));
    expect(host.innerDrops).toEqual([]);
    // A drag whose text is empty would otherwise match every file drop.
    host.payload.set({ ...path, text: '' });
    fixture.detectChanges();
    start(at('source'));
    const empty = drag('drop', transfer());
    at('inner').dispatchEvent(empty);
    expect(empty.defaultPrevented).toBe(false);
    expect(host.innerDrops).toEqual([]);
  });

  it('ignores a drop without a drag id whose text is not the drag in flight', () => {
    const { host, at } = setup();
    start(at('source'));
    const other = transfer();
    other.setData('text/plain', 'Name');
    const drop = drag('drop', other);
    at('inner').dispatchEvent(drop);
    expect(drop.defaultPrevented).toBe(false);
    expect(host.innerDrops).toEqual([]);
  });
});
