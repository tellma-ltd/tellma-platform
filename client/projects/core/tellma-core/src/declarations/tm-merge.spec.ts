// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmTestMetadata } from '@tellma/core-testing';

import { tmCodedReport } from '../reports/tm-coded-report';
import { tmWidget } from '../dashboards/tm-widget';
import { tmEntity } from '../entity/tm-entity-declaration';
import type { TmNavEntry, TmNavSection } from '../nav/tm-nav-declarations';
import type { TmShellSlot } from '../nav/tm-shell-slot';
import { tmFeature, type TmFeatureDefinition } from './tm-feature';
import { ɵtmMergeAppDeclarations, type ɵTmMergeResult } from './tm-merge';
import { tmOverride } from './tm-override';
import { tmPage } from './tm-page';
import type { TmLazyComponent } from './tm-declaration-types';

/** A lazy component that is never loaded. */
const lazy: TmLazyComponent = () => Promise.resolve(class {});

/** A launcher section. */
function section(id: string, order = 10): TmNavSection {
  return { id, labelKey: `Nav_${id}`, icon: 'folder', order };
}

/** A launcher entry. */
function entry(id: string, sectionId?: string): TmNavEntry {
  return { id, labelKey: `Nav_${id}`, url: (t) => `/${String(t)}/${id}`, ...(sectionId === undefined ? {} : { section: sectionId }) };
}

/** An app-bar slot. */
function slot(id: string): TmShellSlot {
  return { id, icon: 'bell', labelKey: `Slot_${id}`, panel: lazy };
}

/** Merges features against the test metadata. */
function merge(...definitions: TmFeatureDefinition[]): ɵTmMergeResult {
  return ɵtmMergeAppDeclarations(definitions.map(tmFeature), tmTestMetadata());
}

/** The problems of a merge as `code key feature` lines. */
function problems(result: ɵTmMergeResult): string[] {
  return result.problems.map((problem) => `${problem.code} ${problem.key} ${problem.feature}`);
}

describe('ɵtmMergeAppDeclarations', () => {
  describe('order and keys', () => {
    it('merges the core feature first and then the features in order', () => {
      const result = merge(
        { key: 'gl', sections: [section('financials')] },
        { key: 'acme', sections: [section('sales')] },
      );
      expect(result.declarations.features).toEqual(['core', 'gl', 'acme']);
      expect(result.declarations.sections.map((s) => s.id)).toEqual(['financials', 'sales']);
      expect(result.problems).toEqual([]);
    });

    it('reports a key a later feature adds again, and keeps the earlier declaration', () => {
      const result = merge(
        { key: 'gl', sections: [section('financials', 10)], pages: [tmPage('cash', { component: lazy, titleKey: 'Gl_Cash' })] },
        {
          key: 'acme',
          sections: [section('financials', 99)],
          pages: [tmPage('cash', { component: lazy, titleKey: 'Acme_Cash' })],
        },
      );
      expect(problems(result)).toEqual(['duplicate-key section:financials acme', 'duplicate-key page:cash acme']);
      expect(result.declarations.sections[0]?.order).toBe(10);
      expect(result.declarations.pages.map((page) => page.titleKey)).toEqual(['Gl_Cash']);
    });

    it('reports every kind of repeated key', () => {
      const result = merge(
        {
          key: 'gl',
          entities: [tmEntity('gl.Center')],
          entries: [entry('gl.overdue')],
          widgets: [tmWidget('acme.cash-position', { labelKey: 'W', icon: 'wallet', component: lazy, defaultSize: { w: 4, h: 4 }, minSize: { w: 3, h: 3 } })],
          settingsTabs: [{ category: 'general', component: lazy }],
          appBar: [slot('chat')],
        },
        {
          key: 'acme',
          entities: [tmEntity('gl.Center')],
          entries: [entry('gl.overdue')],
          widgets: [tmWidget('acme.cash-position', { labelKey: 'W2', icon: 'wallet', component: lazy, defaultSize: { w: 4, h: 4 }, minSize: { w: 3, h: 3 } })],
          settingsTabs: [{ category: 'general', component: lazy }],
          appBar: [slot('chat')],
        },
        { key: 'gl' },
      );
      expect(problems(result)).toEqual([
        'duplicate-key entity:gl.Center acme',
        'duplicate-key entry:gl.overdue acme',
        'duplicate-key widget:acme.cash-position acme',
        'duplicate-key settingsTab:general acme',
        'duplicate-key appBar:chat acme',
        'duplicate-key feature:gl gl',
      ]);
      expect(result.declarations.features).toEqual(['core', 'gl', 'acme']);
    });

    it('takes derived entry ids from entities, pages and coded reports', () => {
      const result = merge({
        key: 'acme',
        entities: [tmEntity('acme.Customer')],
        pages: [tmPage('acme-credit-exposure', { component: lazy, titleKey: 'T' })],
        entries: [entry('acme.Customer')],
      });
      expect(problems(result)).toEqual([
        'duplicate-key entry:acme-credit-exposure acme',
        'duplicate-key entry:acme.Customer acme',
      ]);
      expect(result.problems[0]?.facts).toEqual({ derivedFrom: 'page:acme-credit-exposure', takenBy: 'report:acme-credit-exposure' });
      expect(result.problems[1]?.facts).toEqual({ takenBy: 'entity:acme.Customer' });
      // The page itself stays; only its entry id was taken.
      expect(result.declarations.pages.map((page) => page.path)).toEqual(['acme-credit-exposure']);
    });

    it('derives no entry from a page with a parameter or an entity the launcher cannot open', () => {
      const result = merge({
        key: 'acme',
        pages: [tmPage('reports/:id/studio', { component: lazy, titleKey: 'T' })],
        entities: [tmEntity('core.Blob')],
        entries: [entry('reports/:id/studio'), entry('core.Blob')],
      });
      expect(result.problems).toEqual([]);
    });

    it('reports an entity the metadata lacks, and leaves it out', () => {
      const result = merge({ key: 'acme', entities: [tmEntity('acme.Ghost'), tmEntity('acme.Customer')] });
      expect(problems(result)).toEqual(['unknown-entity entity:acme.Ghost acme']);
      expect([...result.declarations.entities.keys()]).toEqual(['acme.Customer']);
    });
  });

  describe('overrides', () => {
    it('patches an earlier entity member by member, replacing arrays whole', () => {
      const result = merge(
        { key: 'gl', entities: [tmEntity('gl.Center', { section: 'financials', order: 10, columns: ['Name', 'Code'] })], sections: [section('financials')] },
        { key: 'acme', overrides: [tmEntity('gl.Center', { columns: ['Code'], icon: 'building' })] },
      );
      expect(result.problems).toEqual([]);
      expect(result.declarations.entities.get('gl.Center')).toEqual({
        name: 'gl.Center',
        section: 'financials',
        order: 10,
        columns: ['Code'],
        icon: 'building',
      });
      expect(result.origin('entity:gl.Center')).toBe('gl');
      expect(result.origin('entity:gl.Center', 'section')).toBe('gl');
      expect(result.origin('entity:gl.Center', 'columns')).toBe('acme');
      expect(Object.isFrozen(result.declarations.entities.get('gl.Center'))).toBe(true);
    });

    it('patches pages and widget types with their own declaration values', () => {
      const widget = { labelKey: 'W', icon: 'wallet', component: lazy, defaultSize: { w: 4, h: 4 }, minSize: { w: 3, h: 3 } };
      const result = merge(
        { key: 'gl', pages: [tmPage('cash', { component: lazy, titleKey: 'Gl_Cash' })], widgets: [tmWidget('acme.cash-position', widget)] },
        {
          key: 'acme',
          overrides: [
            tmPage('cash', { component: lazy, titleKey: 'Acme_Cash' }),
            tmWidget('acme.cash-position', { ...widget, minSize: { w: 2, h: 2 } }),
          ],
        },
      );
      expect(result.problems).toEqual([]);
      expect(result.declarations.pages[0]?.titleKey).toBe('Acme_Cash');
      expect(result.declarations.widgets[0]?.['minSize']).toEqual({ w: 2, h: 2 });
    });

    it('patches sections, entries, settings tabs and app-bar slots with tmOverride', () => {
      const tab = lazy;
      const result = merge(
        {
          key: 'gl',
          sections: [section('administration', 900)],
          entries: [entry('gl.overdue', 'administration')],
          settingsTabs: [{ category: 'general', component: lazy }],
          appBar: [slot('chat')],
          pages: [tmPage('cash', { component: lazy, titleKey: 'Gl_Cash', order: 1 })],
        },
        {
          key: 'acme',
          overrides: [
            tmOverride('section:administration', { order: 10, id: 'administration' }),
            tmOverride('entry:gl.overdue', { labelKey: 'Acme_Overdue' }),
            tmOverride('settingsTab:general', { component: tab }),
            tmOverride('appBar:chat', { icon: 'message' }),
            tmOverride('page:cash', { order: 2 }),
          ],
        },
      );
      expect(result.problems).toEqual([]);
      expect(result.declarations.sections[0]?.order).toBe(10);
      expect(result.declarations.entries[0]?.labelKey).toBe('Acme_Overdue');
      expect(result.declarations.settingsTabs[0]?.component).toBe(tab);
      expect(result.declarations.appBar[0]?.icon).toBe('message');
      expect(result.declarations.pages[0]?.order).toBe(2);
      expect(result.origin('section:administration', 'order')).toBe('acme');
      expect(result.origin('section:administration', 'labelKey')).toBe('gl');
    });

    it('reports an override that names no earlier declaration', () => {
      const copy = { ...tmEntity('gl.Center', { order: 1 }) };
      const result = merge(
        { key: 'gl', sections: [section('financials')], entities: [tmEntity('gl.Center')] },
        {
          key: 'acme',
          sections: [section('sales')],
          overrides: [
            tmOverride('section:sales', { order: 1 }),
            tmOverride('section:financials', { id: 'money' }),
            tmOverride('entry:gl.Center', { order: 1 }),
            tmOverride('report:acme-credit-exposure' as never, { order: 1 }),
            tmEntity('acme.Customer', { order: 1 }),
            tmEntity('acme.Ghost', { order: 1 }),
            tmPage('missing', { component: lazy, titleKey: 'T' }),
            tmWidget('acme.cash-position', { labelKey: 'W', icon: 'w', component: lazy, defaultSize: { w: 1, h: 1 }, minSize: { w: 1, h: 1 } }),
            copy,
          ],
        },
      );
      expect(problems(result)).toEqual([
        'dangling-override section:sales acme',
        'dangling-override section:financials acme',
        'dangling-override entry:gl.Center acme',
        'dangling-override report:acme-credit-exposure acme',
        'dangling-override entity:acme.Customer acme',
        'unknown-entity entity:acme.Ghost acme',
        'dangling-override page:missing acme',
        'dangling-override widget:acme.cash-position acme',
        'dangling-override override:8 acme',
      ]);
      expect(result.problems.map((problem) => problem.facts['reason'])).toEqual([
        'missing',
        'key-member',
        'derived',
        'unknown-kind',
        'missing',
        undefined,
        'missing',
        'missing',
        'not-a-declaration',
      ]);
      expect(result.declarations.sections.map((s) => s.id)).toEqual(['financials', 'sales']);
    });
  });

  describe('coded reports', () => {
    it('lists every coded report the server declares, patched or not', () => {
      const result = merge({ key: 'acme' });
      expect(result.declarations.reports).toEqual([tmTestMetadata().codedReports[0]]);
      expect(result.reportPatchers.size).toBe(0);
    });

    it('merges two features patching one coded report in feature order', () => {
      const result = merge(
        { key: 'gl', reports: [tmCodedReport('acme-credit-exposure', { section: 'financials', order: 5 })], sections: [section('financials'), section('sales')] },
        { key: 'acme', overrides: [tmCodedReport('acme-credit-exposure', { section: 'sales' })] },
      );
      expect(result.problems).toEqual([]);
      const [report] = result.declarations.reports;
      expect(report?.section).toBe('sales');
      expect(report?.order).toBe(5);
      expect(report?.titleKey).toBe('Acme_Report_CreditExposure');
      expect(result.reportPatchers.get('acme-credit-exposure')).toEqual(['gl', 'acme']);
      expect(result.origin('report:acme-credit-exposure', 'section')).toBe('acme');
    });

    it('refuses a patch of an unknown key and the members the server owns', () => {
      const result = merge({
        key: 'acme',
        reports: [
          tmCodedReport('acme-unknown', { order: 1 }),
          tmCodedReport('acme-credit-exposure', { order: 1, titleKey: 'Mine', kind: 'List' } as never),
        ],
      });
      expect(problems(result)).toEqual([
        'coded-report-not-registered report:acme-unknown acme',
        'coded-report-redeclared report:acme-credit-exposure acme',
        'coded-report-redeclared report:acme-credit-exposure acme',
      ]);
      expect(result.problems.slice(1).map((problem) => problem.facts['member'])).toEqual(['titleKey', 'kind']);
      expect(result.declarations.reports[0]?.titleKey).toBe('Acme_Report_CreditExposure');
      expect(result.declarations.reports[0]?.order).toBe(1);
    });
  });

  describe('hide', () => {
    it('drops a page, a settings tab and an app-bar slot', () => {
      const result = merge(
        {
          key: 'gl',
          pages: [tmPage('cash', { component: lazy, titleKey: 'T' })],
          settingsTabs: [{ category: 'general', component: lazy }],
          appBar: [slot('chat')],
        },
        { key: 'acme', hide: ['page:cash', 'settingsTab:general', 'appBar:chat'] },
      );
      expect(result.problems).toEqual([]);
      expect(result.declarations.pages).toEqual([]);
      expect(result.declarations.settingsTabs).toEqual([]);
      expect(result.declarations.appBar).toEqual([]);
    });

    it('drops only the launcher entry of an entry key, derived or declared', () => {
      const result = merge(
        { key: 'gl', entities: [tmEntity('gl.Center')], entries: [entry('gl.overdue')] },
        { key: 'acme', hide: ['entry:gl.Center', 'entry:gl.overdue', 'entry:acme-credit-exposure'] },
      );
      expect(result.problems).toEqual([]);
      expect([...result.declarations.entities.keys()]).toEqual(['gl.Center']);
      expect(result.declarations.entries).toEqual([]);
      expect([...result.declarations.hiddenEntries].sort()).toEqual(['acme-credit-exposure', 'gl.Center', 'gl.overdue']);
    });

    it('drops a section and every entry under it', () => {
      const result = merge(
        {
          key: 'gl',
          sections: [section('financials'), section('sales')],
          entities: [tmEntity('gl.Center', { section: 'financials' })],
          pages: [tmPage('cash', { component: lazy, titleKey: 'T', section: 'financials' })],
          entries: [entry('gl.overdue', 'financials'), entry('gl.open', 'sales')],
          reports: [tmCodedReport('acme-credit-exposure', { section: 'financials' })],
        },
        { key: 'acme', hide: ['section:financials'] },
      );
      expect(result.problems).toEqual([]);
      expect(result.declarations.sections.map((s) => s.id)).toEqual(['sales']);
      expect(result.declarations.entries.map((e) => e.id)).toEqual(['gl.open']);
      expect([...result.declarations.hiddenEntries].sort()).toEqual(['acme-credit-exposure', 'cash', 'gl.Center', 'gl.overdue']);
      expect(result.declarations.pages.map((page) => page.path)).toEqual(['cash']);
      expect(result.declaredSections).toEqual(new Set(['financials', 'sales']));
    });

    it('reports a hide that names no earlier declaration, and keeps a hidden key taken', () => {
      const result = merge(
        { key: 'gl', sections: [section('financials')] },
        { key: 'acme', hide: ['section:financials', 'section:sales', 'widget:x' as never] },
        {
          key: 'zeta',
          hide: ['section:financials'],
          overrides: [tmOverride('section:financials', { order: 1 })],
          sections: [section('financials')],
        },
      );
      expect(problems(result)).toEqual([
        'dangling-hide section:sales acme',
        'dangling-hide widget:x acme',
        'dangling-override section:financials zeta',
        'dangling-hide section:financials zeta',
        'duplicate-key section:financials zeta',
      ]);
    });
  });

  describe('home and the app bar', () => {
    it('takes the home page of the last feature that sets one', () => {
      const first: TmLazyComponent = () => Promise.resolve(class {});
      const last: TmLazyComponent = () => Promise.resolve(class {});
      expect(merge({ key: 'gl', home: first }, { key: 'acme', home: last }, { key: 'zeta' }).declarations.home).toBe(last);
      expect(merge({ key: 'gl' }).declarations.home).toBeNull();
    });

    it('drops app-bar slots beyond three and slots with a reserved id', () => {
      const result = merge(
        { key: 'gl', appBar: [slot('one'), slot('help'), slot('two')] },
        { key: 'acme', appBar: [slot('three'), slot('assistant'), slot('four')] },
      );
      expect(problems(result)).toEqual([
        'app-bar-reserved-id appBar:help gl',
        'app-bar-reserved-id appBar:assistant acme',
        'app-bar-overflow appBar:four acme',
      ]);
      expect(result.declarations.appBar.map((s) => s.id)).toEqual(['one', 'two', 'three']);
    });

    it('lets the platform feature fill a reserved slot beside three others', () => {
      const core = tmFeature({ key: 'core', appBar: [slot('help')] });
      const result = ɵtmMergeAppDeclarations(
        [tmFeature({ key: 'acme', appBar: [slot('one'), slot('two'), slot('three')] })],
        tmTestMetadata(),
        core,
      );
      expect(result.problems).toEqual([]);
      expect(result.declarations.appBar.map((s) => s.id)).toEqual(['help', 'one', 'two', 'three']);
    });
  });
});
