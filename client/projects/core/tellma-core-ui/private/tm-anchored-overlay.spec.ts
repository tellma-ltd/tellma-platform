// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CdkConnectedOverlay } from '@angular/cdk/overlay';

import {
  tmCreateAnchoredOverlay,
  tmLogicalPositions,
  type TmAnchoredOverlay,
  type TmAnchoredOverlayConfig,
} from './tm-anchored-overlay';

/** A stand-in for the template's CdkConnectedOverlay directive. */
function fakeOverlay(): { directive: CdkConnectedOverlay; updatePosition: ReturnType<typeof vi.fn> } {
  const updatePosition = vi.fn();
  const directive = { overlayRef: { updatePosition } } as unknown as CdkConnectedOverlay;
  return { directive, updatePosition };
}

/**
 * The config the next `createHost` mounts. ONE host class serves every
 * test: declaring a fresh anonymous component per call — each with the same
 * name, selector and template — makes Angular derive the same component id
 * for all of them and warn about the collision (NG0912).
 */
let pendingConfig: TmAnchoredOverlayConfig | null = null;

@Component({ selector: 'tm-anchored-overlay-host', template: `` })
class Host {
  readonly anchored: TmAnchoredOverlay = tmCreateAnchoredOverlay(pendingConfig!);
}

/** Hosts the helper so DestroyRef cleanup ties to the fixture's lifetime. */
function createHost(config: TmAnchoredOverlayConfig) {
  pendingConfig = config;
  const fixture = TestBed.createComponent(Host);
  return { fixture, anchored: fixture.componentInstance.anchored };
}

const macrotask = () => new Promise((resolve) => setTimeout(resolve));

describe('tmCreateAnchoredOverlay', () => {
  it('always emits disableClose (the consumer owns Esc) and defaults to the inline popover host', () => {
    const { anchored } = createHost({
      origin: () => null,
      positions: tmLogicalPositions('block-end', 'start'),
    });
    const config = anchored.overlayConfig();
    expect(config.disableClose).toBe(true);
    expect(config.usePopover).toBe('inline');
    expect(config.matchWidth).toBeUndefined();
    expect('origin' in config).toBe(false);
  });

  it('passes matchWidth through only when requested', () => {
    const { anchored } = createHost({
      origin: () => document.body,
      positions: tmLogicalPositions('block-end', 'start'),
      matchWidth: true,
    });
    expect(anchored.overlayConfig().matchWidth).toBe(true);
  });

  it('re-reads a reactive origin and normalizes DOMRect to the CDK point shape', () => {
    const origin = signal<Element | DOMRect | null>(null);
    const { anchored } = createHost({
      origin,
      positions: tmLogicalPositions('block-end', 'start'),
    });
    expect('origin' in anchored.overlayConfig()).toBe(false);

    origin.set(document.body);
    expect(anchored.overlayConfig().origin).toBe(document.body);

    origin.set(new DOMRect(10, 20, 30, 40));
    expect(anchored.overlayConfig().origin).toEqual({ x: 10, y: 20, width: 30, height: 40 });
  });

  it('resolves a function-valued popover host at read time', () => {
    const host = signal<Element>(document.body);
    const { anchored } = createHost({
      origin: () => document.body,
      positions: tmLogicalPositions('block-end', 'start'),
      popoverHost: () => ({ type: 'parent', element: host() }),
    });
    expect(anchored.overlayConfig().usePopover).toEqual({ type: 'parent', element: document.body });
  });

  it('accepts positions as an array or a function, emitting a fresh mutable copy', () => {
    const fromArray = createHost({
      origin: () => null,
      positions: tmLogicalPositions('block-end', 'start'),
    });
    const positions = tmLogicalPositions('block-start', 'end');
    const fromFn = createHost({ origin: () => null, positions: () => positions });
    expect(fromArray.anchored.overlayConfig().positions).toHaveLength(2);
    expect(fromFn.anchored.overlayConfig().positions).toEqual(positions);
    expect(fromFn.anchored.overlayConfig().positions).not.toBe(positions);
  });

  it('runs the attach/detach/outside-click hooks', () => {
    const onAttach = vi.fn();
    const onDetach = vi.fn();
    const onOutsideClick = vi.fn();
    const { anchored } = createHost({
      origin: () => null,
      positions: tmLogicalPositions('block-end', 'start'),
      onAttach,
      onDetach,
      onOutsideClick,
    });
    anchored.handleAttach();
    anchored.handleDetach();
    const event = new MouseEvent('click');
    anchored.handleOutsideClick(event);
    expect(onAttach).toHaveBeenCalledTimes(1);
    expect(onDetach).toHaveBeenCalledTimes(1);
    expect(onOutsideClick).toHaveBeenCalledWith(event);
  });

  it("'macrotask' re-measures one macrotask after attach", async () => {
    const overlay = fakeOverlay();
    const { anchored } = createHost({
      overlay: () => overlay.directive,
      origin: () => document.body,
      positions: tmLogicalPositions('block-end', 'start'),
      remeasure: 'macrotask',
    });
    anchored.handleAttach();
    expect(overlay.updatePosition).not.toHaveBeenCalled();
    await macrotask();
    expect(overlay.updatePosition).toHaveBeenCalledTimes(1);
  });

  it("'macrotask' never fires after destroy — a disposed overlay's updatePosition throws", async () => {
    const overlay = fakeOverlay();
    const { fixture, anchored } = createHost({
      overlay: () => overlay.directive,
      origin: () => document.body,
      positions: tmLogicalPositions('block-end', 'start'),
      remeasure: 'macrotask',
    });
    anchored.handleAttach();
    fixture.destroy();
    await macrotask();
    expect(overlay.updatePosition).not.toHaveBeenCalled();
  });

  it("'afterNextRender' re-measures after the next render pass", async () => {
    const overlay = fakeOverlay();
    const { fixture, anchored } = createHost({
      overlay: () => overlay.directive,
      origin: () => document.body,
      positions: tmLogicalPositions('block-end', 'start'),
      remeasure: 'afterNextRender',
    });
    anchored.handleAttach();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(overlay.updatePosition).toHaveBeenCalledTimes(1);
  });

  it("'none' schedules no re-measure", async () => {
    const overlay = fakeOverlay();
    const { fixture, anchored } = createHost({
      overlay: () => overlay.directive,
      origin: () => document.body,
      positions: tmLogicalPositions('block-end', 'start'),
    });
    anchored.handleAttach();
    fixture.detectChanges();
    await fixture.whenStable();
    await macrotask();
    expect(overlay.updatePosition).not.toHaveBeenCalled();
  });

  it('reanchor() re-measures without re-running onAttach', async () => {
    const overlay = fakeOverlay();
    const onAttach = vi.fn();
    const { anchored } = createHost({
      overlay: () => overlay.directive,
      origin: () => document.body,
      positions: tmLogicalPositions('block-end', 'start'),
      remeasure: 'macrotask',
      onAttach,
    });
    anchored.reanchor();
    await macrotask();
    expect(overlay.updatePosition).toHaveBeenCalledTimes(1);
    expect(onAttach).not.toHaveBeenCalled();
  });
});

describe('tmLogicalPositions', () => {
  it('block-end prefers below with the aligned edge, flipping above', () => {
    expect(tmLogicalPositions('block-end', 'start')).toEqual([
      { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top' },
      { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom' },
    ]);
    expect(tmLogicalPositions('block-end', 'center')[0].originX).toBe('center');
    expect(tmLogicalPositions('block-end', 'end')[0].originX).toBe('end');
  });

  it('block-start prefers above, flipping below', () => {
    expect(tmLogicalPositions('block-start', 'start')).toEqual([
      { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom' },
      { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top' },
    ]);
  });

  it('inline sides map alignment onto the block axis and flip to the opposite side', () => {
    expect(tmLogicalPositions('inline-end', 'start')).toEqual([
      { originX: 'end', originY: 'top', overlayX: 'start', overlayY: 'top' },
      { originX: 'start', originY: 'top', overlayX: 'end', overlayY: 'top' },
    ]);
    expect(tmLogicalPositions('inline-start', 'center')).toEqual([
      { originX: 'start', originY: 'center', overlayX: 'end', overlayY: 'center' },
      { originX: 'end', originY: 'center', overlayX: 'start', overlayY: 'center' },
    ]);
    expect(tmLogicalPositions('inline-end', 'end')[0].originY).toBe('bottom');
  });
});
