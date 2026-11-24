// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TM_DEV_MODE } from '../../app/tm-dev-mode';
import { TM_STORE_EXTRA_FORMATS } from '../store/tm-doc-formats';
import { TM_LEADER_DOCUMENTS, ɵTM_UNIQUE_LEADER_DOCUMENTS, type TmLeaderDocument } from './tm-leader-documents';

// The leader documents, one per name: what the store's formats, the
// election and the leader read, strict about a name registered twice in
// development and lenient in production.

/** A distribution's leader document. */
const approvals: TmLeaderDocument = { name: 'acme.approvals', route: 'acme/approvals/summary', events: ['acme.approvals.changed'], formatVersion: 1 };

/** Another registration of the same name, at another route and format. */
const approvalsAgain: TmLeaderDocument = { ...approvals, route: 'acme/approvals/count', formatVersion: 2 };

/** Another distribution document. */
const tasks: TmLeaderDocument = { name: 'acme.tasks', route: 'acme/tasks/summary', events: [], formatVersion: 3 };

/** Registers the documents, in development or in production. */
function setUp(docs: readonly TmLeaderDocument[], dev: boolean): void {
  TestBed.configureTestingModule({
    providers: [
      { provide: TM_DEV_MODE, useValue: () => dev },
      ...docs.map((doc) => ({ provide: TM_LEADER_DOCUMENTS, multi: true, useValue: doc })),
    ],
  });
}

describe('the leader documents', () => {
  it('reads each registered document once, with its store format', () => {
    setUp([approvals, tasks], true);
    expect(TestBed.inject(ɵTM_UNIQUE_LEADER_DOCUMENTS)).toEqual([approvals, tasks]);
    expect(TestBed.inject(TM_STORE_EXTRA_FORMATS)).toEqual({ 'acme.approvals': 1, 'acme.tasks': 3 });
  });

  it('throws in development for a name registered twice', () => {
    setUp([approvals, tasks, approvalsAgain], true);
    expect(() => TestBed.inject(ɵTM_UNIQUE_LEADER_DOCUMENTS)).toThrow(/'acme\.approvals' is registered twice/);
  });

  it("throws in development when the store's boot reads the formats of a name registered twice", () => {
    setUp([approvals, approvalsAgain], true);
    expect(() => TestBed.inject(TM_STORE_EXTRA_FORMATS)).toThrow(/'acme\.approvals' is registered twice/);
  });

  it('keeps the first registration of a name in production', () => {
    setUp([approvals, tasks, approvalsAgain], false);
    expect(TestBed.inject(ɵTM_UNIQUE_LEADER_DOCUMENTS)).toEqual([approvals, tasks]);
    expect(TestBed.inject(TM_STORE_EXTRA_FORMATS)).toEqual({ 'acme.approvals': 1, 'acme.tasks': 3 });
  });
});
