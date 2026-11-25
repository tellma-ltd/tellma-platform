// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { TmLocalization, type TmShortcut } from '@tellma/core';
import { TmSearchFieldHarness } from '@tellma/core-ui-testing';
import { TM_GRID_KEYS } from '@tellma/core-ui/grid';

import { tmAppRig, tmSettle } from '../../../src/routes/tm-app.testing.util';
import { tmRegisterShellOverlaysStrings } from '../session/tm-shell-overlays-strings';
import { TmShortcutsDialog } from './tm-shortcuts-dialog';

// The Shortcuts dialog in the test app: its sections from the registrations
// it opened with (General, and This page only when the page has some), the
// grid's keys with shared labels in one row, the launcher's keys, the
// tables' headers, and the filter through the search's text folding.

/** A registration. */
function shortcut(id: string, keys: string, labelKey: string, scope: TmShortcut['scope']): TmShortcut {
  return { id, keys, labelKey, scope, run: () => undefined } as TmShortcut;
}

describe('the Shortcuts dialog', () => {
  let fixture: ComponentFixture<TmShortcutsDialog>;

  afterEach(() => TestBed.resetTestingModule());

  /** Renders the dialog with the registrations it opened with. */
  async function open(registered: readonly TmShortcut[], strings: Readonly<Record<string, string>> = {}): Promise<HTMLElement> {
    await tmAppRig({ strings: { en: strings } });
    // The overlays' chunk, which opens the dialog, registers the label of its key.
    tmRegisterShellOverlaysStrings(TestBed.inject(TmLocalization));
    fixture = TestBed.createComponent(TmShortcutsDialog);
    fixture.componentRef.setInput('registered', registered);
    await tmSettle();
    return fixture.nativeElement as HTMLElement;
  }

  /** Each section's heading and its rows' labels. */
  const sections = (element: HTMLElement): [string, string[]][] =>
    Array.from(element.querySelectorAll('section')).map((section) => [
      section.querySelector('h3')?.textContent?.trim() ?? '',
      Array.from(section.querySelectorAll('tbody tr td:first-child')).map((cell) => cell.textContent?.trim() ?? ''),
    ]);

  it('lists the general and page registrations, the grid keys and the launcher keys, each a table with headers', async () => {
    const element = await open(
      [
        shortcut('tm.shortcuts.open', 'Mod+/', 'tm.shellOverlays.dialogs.shortcuts', 'global'),
        shortcut('acme.approve', 'Alt+A', 'Acme_Approve', 'page'),
      ],
      { Acme_Approve: 'Approve' },
    );
    const shown = sections(element);
    expect(shown.map(([title]) => title)).toEqual(['General', 'This page', 'Lists and grids', 'Launcher']);
    expect(shown[0][1]).toEqual(['Keyboard shortcuts']);
    expect(shown[1][1]).toEqual(['Approve']);
    expect(shown[2][1]).toHaveLength(new Set(TM_GRID_KEYS.map((entry) => entry.labelKey)).size);
    expect(shown[3][1]).toEqual(['Move between results', 'Open the result', 'Open the result in a new tab', 'Close the launcher']);
    const headers = Array.from(element.querySelectorAll('section:first-of-type th')).map((th) => [th.textContent?.trim(), th.getAttribute('scope')]);
    expect(headers).toEqual([
      ['Action', 'col'],
      ['Keys', 'col'],
    ]);
    // A label shared by several grid keys lists them all in its row.
    const rows = Array.from(element.querySelectorAll('section:nth-of-type(3) tbody tr'));
    expect(rows.some((row) => row.querySelectorAll('tm-kbd').length > 1)).toBe(true);
    expect(element.querySelector('section:nth-of-type(4) tbody tr:nth-child(3)')?.querySelectorAll('tm-kbd')).toHaveLength(2);
  });

  it('hides This page while the page has no registration', async () => {
    const element = await open([shortcut('tm.shortcuts.open', 'Mod+/', 'tm.shellOverlays.dialogs.shortcuts', 'global')]);
    expect(sections(element).map(([title]) => title)).toEqual(['General', 'Lists and grids', 'Launcher']);
  });

  it('filters every section through the folded text, and says when nothing matches', async () => {
    const element = await open([shortcut('tm.shortcuts.open', 'Mod+/', 'tm.shellOverlays.dialogs.shortcuts', 'global')]);
    const filter = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmSearchFieldHarness);
    await filter.setValue('  LAUNCHER ');
    await tmSettle();
    expect(sections(element)).toEqual([['Launcher', ['Close the launcher']]]);
    await filter.setValue('zzz');
    await tmSettle();
    expect(sections(element)).toEqual([]);
    expect(element.querySelector('[role="status"]')?.textContent?.trim()).toBe('No shortcut matches.');
  });
});
