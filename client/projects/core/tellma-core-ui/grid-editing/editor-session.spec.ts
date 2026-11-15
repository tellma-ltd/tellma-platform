// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  Injector,
  inject,
  viewChild,
  ViewContainerRef,
  type TemplateRef,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmInput } from '@tellma/core-ui/input';

import { ɵTmGridEditorSession, type ɵTmGridEditorTemplateContext } from './editor-session';

// Section: the fixture — an outlet and two editor templates, one with a
// single control and one with two unshielded controls.

@Component({
  imports: [TmInput],
  template: `
    <ng-container #outlet />
    <ng-template #single><input tmInput aria-label="Name" /></ng-template>
    <ng-template #double>
      <input tmInput aria-label="Starts" />
      <input tmInput aria-label="Ends" />
    </ng-template>
  `,
})
class SessionHost {
  readonly injector = inject(Injector);
  readonly outlet = viewChild.required('outlet', { read: ViewContainerRef });
  readonly single = viewChild.required<TemplateRef<ɵTmGridEditorTemplateContext>>('single');
  readonly double = viewChild.required<TemplateRef<ɵTmGridEditorTemplateContext>>('double');
}

/** A session over the host's outlet, with a template mount staged. */
function sessionWith(
  host: SessionHost,
  template: TemplateRef<ɵTmGridEditorTemplateContext>,
): ɵTmGridEditorSession {
  const session = new ɵTmGridEditorSession(host.injector, () => host.outlet());
  session.stage({
    kind: 'template',
    template,
    context: { $implicit: null, row: undefined, readonly: false },
  });
  return session;
}

// Section: the tests.

describe('ɵTmGridEditorSession', () => {
  let host: SessionHost;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(SessionHost);
    fixture.detectChanges();
    host = fixture.componentInstance;
  });

  it('mounts the one control a template registers', () => {
    const session = sessionWith(host, host.single());
    expect(session.mountIfReady()?.kind).toBe('template');
    session.destroy();
  });

  it('refuses, in development, a template whose controls all registered', () => {
    const session = sessionWith(host, host.double());
    expect(() => session.mountIfReady()).toThrowError(/2 controls registered as the cell editor/);
    // Nothing stays mounted.
    expect(session.current()).toBeNull();
    expect(host.outlet().length).toBe(0);
  });
});
