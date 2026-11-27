// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import type { TmEntityType } from '@tellma/core';

import { type TmAssignmentActivation, TmAssignmentList, type TmAssignmentListVariant, type TmAssignmentParty } from './tm-assignment-list';
import { tmAttentionRig, tmSettle, tmTestAssignment } from '../../src/attention/tm-attention.testing.util';

// The assignment list in a whole test app entered into tenant 17: the
// tray's and the home page's variants, either party, the second line by
// the row's facts, the "New" marks against seenAt, the loading skeletons,
// and the row links.

/** The list's inputs, as signals a test sets. */
@Component({
  imports: [TmAssignmentList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-assignment-list
      label="Assignments"
      [items]="items()"
      [variant]="variant()"
      [party]="party()"
      [seenAt]="seenAt()"
      [loading]="loading()"
      (activated)="activations.push($event)"
    />
  `,
})
class Host {
  /** The rows. */
  readonly items = signal<readonly TmEntityType<'core.Assignment'>[]>([]);
  /** The variant. */
  readonly variant = signal<TmAssignmentListVariant>('tray');
  /** The party. */
  readonly party = signal<TmAssignmentParty>('to-me');
  /** The seenAt the rows are marked against. */
  readonly seenAt = signal<string | null>('2026-11-23T08:30:00.000Z');
  /** Whether the list shows its skeletons. */
  readonly loading = signal(false);
  /** The rows chosen. */
  readonly activations: TmAssignmentActivation[] = [];
}

/** The detail the server's caption carries for a center. */
const MANAGED = { manager: ['Al Faris Group', 'مجموعة الفارس', null] };

/** Starts the app and renders the list with the rows given. */
async function setup(items: readonly TmEntityType<'core.Assignment'>[]): Promise<ComponentFixture<Host>> {
  await tmAttentionRig();
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.items.set(items);
  await fixture.whenStable();
  return fixture;
}

/** The rows as they read: title, line and the "New" mark. */
function rows(fixture: ComponentFixture<Host>): { readonly title: string; readonly line: string; readonly new: boolean }[] {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[data-tm-row]')].map((row) => ({
    title: row.querySelector('.tm-attention-row__title')?.textContent?.replace(/\s+/g, ' ').trim() ?? '',
    line: row.querySelector('.tm-attention-row__line')?.textContent?.replace(/\s+/g, ' ').trim() ?? '',
    new: row.querySelector('.tm-assignment-list__new') !== null,
  }));
}

describe('tm-assignment-list', () => {
  afterEach(() => TestBed.resetTestingModule());

  it("reads the tray's second line from the row's facts, a system's comment alone, and marks rows assigned since seenAt by someone else", async () => {
    const fixture = await setup([
      tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }), comment: 'Please review', assignedAt: '2026-11-23T09:00:00.000Z' }),
      tmTestAssignment(2, { captionJson: JSON.stringify({ code: 'HQ-02' }), comment: 'Fix the code', isReturn: true }),
      tmTestAssignment(3, { captionJson: JSON.stringify({ code: 'HQ-03', detail: MANAGED }), assignedAt: '2026-11-23T09:00:00.000Z', assignedById: 42 }),
      tmTestAssignment(4, { captionJson: JSON.stringify({ code: 'HQ-04' }), assignedById: null }),
      tmTestAssignment(5, { captionJson: JSON.stringify({ code: 'HQ-05' }), assignedById: null, comment: 'Please review' }),
    ]);
    const shown = rows(fixture);
    expect(shown.map((row) => row.title)).toEqual(['Center HQ-01', 'Center HQ-02', 'Center HQ-03', 'Center HQ-04', 'Center HQ-05']);
    expect(shown[0]?.line).toMatch(/: Please review$/);
    expect(shown[1]?.line).toMatch(/^Returned by .*: Fix the code$/);
    expect(shown[2]?.line).toBe('Managed by Al Faris Group');
    expect(shown[3]?.line).toBe('Assigned automatically');
    // A system's assignment with a comment shows the comment alone, after no empty name.
    expect(shown[4]?.line).toBe('Please review');
    // Row 3 is new by time, but the user assigned it to themselves.
    expect(shown.map((row) => row.new)).toEqual([true, false, false, false, false]);
  });

  it("marks every row new against the epoch, a user's seenAt before any tray, and none without a seenAt", async () => {
    const fixture = await setup([tmTestAssignment(1), tmTestAssignment(2, { assignedAt: '2020-01-01T00:00:00.000Z' }), tmTestAssignment(3, { assignedById: 42 })]);
    fixture.componentInstance.seenAt.set('1970-01-01T00:00:00Z');
    await fixture.whenStable();
    // Row 3 the user assigned to themselves.
    expect(rows(fixture).map((row) => row.new)).toEqual([true, true, false]);
    fixture.componentInstance.seenAt.set(null);
    await fixture.whenStable();
    expect(rows(fixture).map((row) => row.new)).toEqual([false, false, false]);
  });

  it("names the assignee on the user's own assignments, and marks none of them new", async () => {
    const fixture = await setup([tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }), assignedAt: '2026-11-23T09:00:00.000Z' })]);
    fixture.componentInstance.party.set('by-me');
    await fixture.whenStable();
    const [row] = rows(fixture);
    expect(row?.title).toBe('Center HQ-01');
    expect(row?.line).toMatch(/^With /);
    expect(row?.new).toBe(false);
  });

  it("shows the code alone as the home list's title, with the detail, else the comment", async () => {
    const fixture = await setup([
      tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01', detail: MANAGED }), comment: 'Please review' }),
      tmTestAssignment(2, { captionJson: JSON.stringify({ code: 'HQ-02' }), comment: 'Please review' }),
    ]);
    fixture.componentInstance.variant.set('home');
    await fixture.whenStable();
    expect(rows(fixture).map(({ title, line }) => ({ title, line }))).toEqual([
      { title: 'HQ-01', line: 'Managed by Al Faris Group' },
      { title: 'HQ-02', line: 'Please review' },
    ]);
  });

  it('shows three skeleton rows, read as one busy list, while loading', async () => {
    const fixture = await setup([]);
    fixture.componentInstance.loading.set(true);
    await fixture.whenStable();
    const list = (fixture.nativeElement as HTMLElement).querySelector('ul')!;
    expect(list.getAttribute('aria-busy')).toBe('true');
    expect(list.getAttribute('aria-label')).toBe('Loading assignments');
    expect(list.querySelectorAll('li[aria-hidden="true"]')).toHaveLength(3);
  });

  it("links each row to its target's page, and reports the row a plain click opens", async () => {
    const fixture = await setup([tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'HQ-01' }) })]);
    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('[data-tm-row-control]')!;
    expect(link.getAttribute('href')).toBe('/17/centers/23');
    expect(link.getAttribute('aria-label')).toMatch(/^Center HQ-01, /);
    link.click();
    await tmSettle();
    expect(fixture.componentInstance.activations.map((activation) => activation.position)).toEqual([1]);
  });
});
