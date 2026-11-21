// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ɵtmMergeAppDeclarations,
  type TmAppConfig,
  type TmClientMetadata,
  type TmEntitiesChunk,
  type TmFeatureDefinition,
  type TmLanguageDefinition,
  type TmLazyComponent,
  type TmMetadataLoaders,
  type TmShellSlot,
  tmCodedReport,
  tmEntity,
  tmFeature,
  tmOverride,
  tmPage,
  tmWidget,
} from '@tellma/core';
import { tmTestEntitiesChunk, tmTestMetadata } from '@tellma/core-testing';

import { tmCheckAppConfig } from './tm-check-app-config';
import type { TmConfigProblem } from './tm-config-problem';

/** A lazy component that is never loaded. */
const lazy: TmLazyComponent = () => Promise.resolve(class {});

/** Loaders the check never calls. */
const loaders: TmMetadataLoaders = {
  core: () => Promise.reject(new Error('unused')),
  entities: () => Promise.reject(new Error('unused')),
  contracts: () => Promise.reject(new Error('unused')),
  strings: {},
};

/** A language definition. */
function language(code: string): TmLanguageDefinition {
  return { code, dir: code === 'ar' ? 'rtl' : 'ltr', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) };
}

/** The widget type the test metadata registers. */
const cashPosition = tmWidget('acme.cash-position', {
  labelKey: 'Acme_Widget_CashPosition',
  icon: 'wallet',
  component: lazy,
  defaultSize: { w: 4, h: 4 },
  minSize: { w: 3, h: 3 },
});

/** The `gl` feature of the sound configuration. */
const gl: TmFeatureDefinition = {
  key: 'gl',
  sections: [{ id: 'financials', labelKey: 'Nav_Financials', icon: 'coins', order: 10 }],
  entities: [tmEntity('gl.Center', { section: 'financials', columns: ['Name', 'Manager.Name'], orderBy: 'Code:desc' })],
};

/** The `acme` feature of the sound configuration. */
const acme: TmFeatureDefinition = {
  key: 'acme',
  sections: [{ id: 'sales', labelKey: 'Nav_Sales', icon: 'users', order: 20 }],
  entities: [
    tmEntity('acme.Customer', {
      section: 'sales',
      details: lazy,
      columns: ['Name', { path: 'Center', select: ['Center.Code'] }, { expression: 'CreditLimit * 2', labelKey: 'Double' }],
      tile: { title: 'Name', subtitle: 'Code', topEnd: 'CreditLimit' },
      picker: { description: ['Code', 'Center.Manager.Email'], orderBy: 'Name' },
      actions: [{ name: 'set-credit-limit' }],
    }),
  ],
  pages: [tmPage('credit-review', { component: lazy, titleKey: 'Acme_CreditReview', section: 'sales' })],
  reports: [tmCodedReport('acme-credit-exposure', { section: 'sales' })],
  widgets: [cashPosition],
};

/** A configuration with the given features, which the test metadata accepts when they are `gl` and `acme`. */
function config(features: readonly TmFeatureDefinition[] = [gl, acme], extra: Partial<TmAppConfig> = {}): TmAppConfig {
  return { metadata: loaders, languages: [language('en'), language('ar')], features: features.map(tmFeature), ...extra };
}

/** The problems of a check as `code key feature` lines. */
function lines(problems: readonly TmConfigProblem[]): string[] {
  return problems.map((problem) => `${problem.code} ${problem.key} ${problem.feature}`);
}

/** Checks a configuration against (patched) test metadata. */
function check(
  appConfig: TmAppConfig,
  metadata: Partial<TmClientMetadata> = {},
  entities: Partial<TmEntitiesChunk> = {},
  strings: Readonly<Record<string, Readonly<Record<string, string>>>> = {},
): readonly TmConfigProblem[] {
  return tmCheckAppConfig(appConfig, tmTestMetadata(metadata), tmTestEntitiesChunk(entities), strings);
}

/** An app-bar slot. */
function slot(id: string): TmShellSlot {
  return { id, icon: 'bell', labelKey: `Slot_${id}`, panel: lazy };
}

describe('tmCheckAppConfig', () => {
  it('finds nothing wrong with a sound configuration', () => {
    expect(check(config())).toEqual([]);
  });

  it('never calls a lazy member of the configuration, in declarations or in overrides', () => {
    // Every function the config holds is a spy; the check must call none.
    const spies: { readonly mock: { readonly calls: readonly unknown[] } }[] = [];
    const lazySpy = (): TmLazyComponent => {
      const spy = vi.fn(() => Promise.resolve(class {}));
      spies.push(spy);
      return spy;
    };
    const callSpy = <R>(implementation: () => R): (() => R) => {
      const spy = vi.fn(implementation);
      spies.push(spy);
      return spy;
    };
    const fnSpy = <R>(result: R): (() => R) => callSpy(() => result);
    const rejectSpy = (): (() => Promise<never>) => callSpy(() => Promise.reject(new Error('unused')));
    const widgetDefinition = {
      labelKey: 'W',
      icon: 'w',
      component: lazySpy(),
      settingsEditor: lazySpy(),
      defaultSize: { w: 1, h: 1 },
      minSize: { w: 1, h: 1 },
    };
    const entityHatches = {
      search: lazySpy(),
      details: lazySpy(),
      defaults: fnSpy({}),
      clone: fnSpy({}),
      validate: fnSpy(undefined),
      beforeSave: fnSpy({}),
      rowLink: fnSpy(null),
      visibleWhen: fnSpy(true),
    };

    // --- Declarations: the gl and acme features with a spy in every function member ---
    const glFeature: TmFeatureDefinition = {
      ...gl,
      entities: [
        tmEntity('gl.Center', {
          ...entityHatches,
          section: 'financials',
          columns: ['Name', { path: 'Manager', display: lazySpy(), select: ['Manager.Name'] }],
          tile: { component: lazySpy() },
        }),
      ],
      entries: [{ id: 'gl.overdue', labelKey: 'L', url: fnSpy('/'), visibleWhen: fnSpy(true), section: 'financials' }],
      pages: [tmPage('cash', { component: lazySpy(), titleKey: 'T', visibleWhen: fnSpy(true) })],
      settingsTabs: [{ category: 'general', component: lazySpy() }],
      appBar: [{ ...slot('chat'), panel: lazySpy(), badge: fnSpy(null) as never }],
      icons: { coins: [['circle', { r: 8 }]] },
      providers: [{ provide: 'gl.token', useFactory: fnSpy(1) }],
      home: lazySpy(),
    };
    const acmeFeature: TmFeatureDefinition = {
      ...acme,
      entities: [
        tmEntity('acme.Customer', {
          ...entityHatches,
          section: 'sales',
          actions: [{ name: 'set-credit-limit', dialog: lazySpy(), visible: fnSpy(true) }],
        }),
      ],
      pages: [tmPage('credit-review', { component: lazySpy(), titleKey: 'T', visibleWhen: fnSpy(true) })],
      reports: [tmCodedReport('acme-credit-exposure', { component: lazySpy(), visibleWhen: fnSpy(true) })],
      widgets: [tmWidget('acme.cash-position', widgetDefinition)],
    };

    // --- Overrides: every kind of patch, each carrying function members ---
    const zeta: TmFeatureDefinition = {
      key: 'zeta',
      overrides: [
        tmEntity('gl.Center', { search: lazySpy(), details: lazySpy(), visibleWhen: fnSpy(false) }),
        tmPage('cash', { component: lazySpy(), titleKey: 'T2', visibleWhen: fnSpy(false) }),
        tmCodedReport('acme-credit-exposure', { component: lazySpy(), visibleWhen: fnSpy(false) }),
        tmWidget('acme.cash-position', { ...widgetDefinition, component: lazySpy() }),
        tmOverride('entry:gl.overdue', { url: fnSpy('/x'), visibleWhen: fnSpy(false) }),
        tmOverride('appBar:chat', { panel: lazySpy(), badge: fnSpy(null) }),
        tmOverride('settingsTab:general', { component: lazySpy() }),
      ],
      providers: [{ provide: 'zeta.token', useFactory: fnSpy(2) }],
      home: lazySpy(),
    };

    const appConfig: TmAppConfig = {
      metadata: {
        core: rejectSpy(),
        entities: rejectSpy(),
        contracts: rejectSpy(),
        strings: { en: rejectSpy() },
      },
      languages: [
        { code: 'en', dir: 'ltr', strings: rejectSpy() },
        { code: 'ar', dir: 'rtl', strings: rejectSpy() },
      ],
      calendars: [{ code: 'hj', load: rejectSpy() }],
      features: [glFeature, acmeFeature, zeta].map(tmFeature),
    };
    const problems = tmCheckAppConfig(
      appConfig,
      tmTestMetadata({ calendars: ['gc', 'hj'], features: [...tmTestMetadata().features, { key: 'zeta', version: '1' }] }),
      tmTestEntitiesChunk(),
    );
    // Every declaration and every patch was accepted, so each path ran over its spies.
    expect(problems).toEqual([]);
    expect(spies.length).toBeGreaterThan(40);
    expect(spies.filter((spy) => spy.mock.calls.length > 0)).toEqual([]);
  });

  describe('the merge rule', () => {
    it('reports duplicate-key', () => {
      const result = check(config([gl, { ...acme, sections: [...(acme.sections ?? []), ...(gl.sections ?? [])] }]));
      expect(lines(result)).toEqual(['duplicate-key section:financials acme']);
      expect(result[0]?.message).toBe(
        "'section:financials' is declared more than once; a later feature changes an earlier declaration only through overrides and hide.",
      );
      expect(result[0]?.severity).toBe('error');
    });

    it('reports dangling-override', () => {
      const result = check(config([gl, { ...acme, overrides: [tmOverride('section:nowhere', { order: 1 })] }]));
      expect(lines(result)).toEqual(['dangling-override section:nowhere acme']);
      expect(result[0]?.message).toBe("The override of 'section:nowhere' names no declaration of an earlier feature.");
    });

    it('reports dangling-hide', () => {
      const result = check(config([gl, { ...acme, hide: ['page:nowhere'] }]));
      expect(lines(result)).toEqual(['dangling-hide page:nowhere acme']);
      expect(result[0]?.message).toBe("hide names 'page:nowhere', which no earlier feature declares.");
    });

    it('reports unknown-section on entities, pages, coded reports and entries', () => {
      const result = check(
        config([
          gl,
          {
            ...acme,
            sections: [],
            entries: [{ id: 'acme.overdue', labelKey: 'L', url: () => '/', section: 'sales' }],
          },
        ]),
      );
      expect(lines(result)).toEqual([
        'unknown-section entity:acme.Customer acme',
        'unknown-section page:credit-review acme',
        'unknown-section report:acme-credit-exposure acme',
        'unknown-section entry:acme.overdue acme',
      ]);
      expect(result[0]?.message).toBe("'entity:acme.Customer' names the section 'sales', which no feature declares.");
    });

    it('accepts a section a later feature hid', () => {
      expect(check(config([gl, acme, { key: 'acme2', hide: ['section:sales'] }]), {
        features: [...tmTestMetadata().features, { key: 'acme2', version: '1.0.0' }],
      })).toEqual([]);
    });

    it('reports app-bar-overflow and app-bar-reserved-id', () => {
      const result = check(config([gl, { ...acme, appBar: [slot('one'), slot('help'), slot('two'), slot('three'), slot('four')] }]));
      expect(lines(result)).toEqual(['app-bar-reserved-id appBar:help acme', 'app-bar-overflow appBar:four acme']);
      expect(result.map((problem) => problem.message)).toEqual([
        "'appBar:help' takes an id the platform reserves for its own app-bar slot, so it is dropped.",
        "'appBar:four' is beyond the 3 app-bar slots all features together may add, so it is dropped.",
      ]);
    });

    it('reports unknown-entity', () => {
      const result = check(config([gl, { ...acme, entities: [...(acme.entities ?? []), tmEntity('acme.Ghost')] }]));
      expect(lines(result)).toEqual(['unknown-entity entity:acme.Ghost acme']);
      expect(result[0]?.message).toBe("The metadata has no entity 'acme.Ghost'.");
    });
  });

  describe('page paths', () => {
    /** The problems of one page path declared by `acme`. */
    function pathProblems(path: string, section?: string): string[] {
      const page = tmPage(path, { component: lazy, titleKey: 'T', ...(section === undefined ? {} : { section }) });
      return check(config([gl, { ...acme, pages: [page] }])).map((problem) => `${problem.code}:${String(problem.message)}`);
    }

    it('accepts kebab-case literals and camelCase parameters after a literal first segment', () => {
      for (const path of ['credit-review', 'reports/:id/studio', 'a1/b-2/:itemId', 'x']) {
        expect(pathProblems(path)).toEqual([]);
      }
    });

    it('reports invalid-path for every break of the grammar', () => {
      expect(pathProblems('Credit-Review')).toEqual([
        "invalid-path:The segment 'Credit-Review' of 'page:Credit-Review' is neither a kebab-case literal nor a camelCase ':name' parameter.",
      ]);
      expect(pathProblems('credit--review')[0]).toMatch(/^invalid-path:/);
      expect(pathProblems('credit-review-')[0]).toMatch(/^invalid-path:/);
      expect(pathProblems('1credit')[0]).toMatch(/^invalid-path:/);
      expect(pathProblems('credit/:Id')[0]).toMatch(/^invalid-path:/);
      expect(pathProblems('credit/:item-id')[0]).toMatch(/^invalid-path:/);
      expect(pathProblems(':id/credit')).toEqual([
        "invalid-path:The path of 'page::id/credit' starts with the parameter ':id'; its first segment must be a literal.",
      ]);
      expect(pathProblems('credit//review')).toEqual(["invalid-path:The path of 'page:credit//review' has an empty segment."]);
      expect(pathProblems('/credit')[0]).toMatch(/has an empty segment/);
      expect(pathProblems('credit/new')).toEqual([
        "invalid-path:The path of 'page:credit/new' has the segment 'new', which the platform reserves for create pages.",
      ]);
      expect(pathProblems('credit/:id/:id')).toEqual(["invalid-path:The path of 'page:credit/:id/:id' names the parameter ':id' twice."]);
      expect(pathProblems('credit/:id', 'sales')).toEqual([
        "invalid-path:'page:credit/:id' has a parameter and a section; a page with a parameter has no launcher entry, so it takes no section.",
      ]);
    });

    it('reports reserved-path for a routing word, new, and another feature\'s entity segment', () => {
      for (const word of ['settings', 'me', 'list', 'tenants', 'signed-out', 'api', 'hub', 'blobs', 'health', 'new']) {
        expect(pathProblems(`${word}/x`)).toEqual([`reserved-path:'page:${word}/x' starts with '${word}', a word the platform's routes reserve.`]);
      }
      // `centers` is the segment of gl.Center, which gl declares, not acme.
      expect(pathProblems('centers/map')).toEqual([
        "reserved-path:'page:centers/map' starts with 'centers', the route segment of the entity 'gl.Center', which the feature 'gl' declares.",
      ]);
      // `roles` is core.Role's segment; no feature declares it, and the entity's routes exist regardless.
      expect(pathProblems('roles/matrix')).toEqual([
        "reserved-path:'page:roles/matrix' starts with 'roles', the route segment of the entity 'core.Role', whose routes exist whether or not a feature declares it.",
      ]);
      // `customers` is acme.Customer's, which acme itself declares.
      expect(pathProblems('customers/map')).toEqual([]);
    });
  });

  describe('coded reports', () => {
    /** Test metadata with an extra coded report. */
    function withReport(key: string, route?: string): Partial<TmClientMetadata> {
      const base = tmTestMetadata().codedReports[0];
      if (base === undefined) {
        throw new Error('The test metadata lacks its coded report.');
      }
      const added = {
        key,
        feature: 'acme',
        securable: base.securable,
        kind: base.kind,
        titleKey: `Report_${key}`,
        export: 'None' as const,
        parameters: [],
        ...(route === undefined ? {} : { route }),
      };
      return { codedReports: [base, added] };
    }

    it('reports invalid-report-key on a patched key only', () => {
      const result = check(config([gl, { ...acme, reports: [tmCodedReport('acme_Aging', { order: 1 })] }]), withReport('acme_Aging', 'GET /x'));
      expect(lines(result)).toEqual(['invalid-report-key report:acme_Aging acme']);
      expect(result[0]?.message).toBe("The coded report key 'acme_Aging' is not kebab-case with at least one hyphen.");
      expect(check(config(), withReport('acme_Aging', 'GET /x'))).toEqual([]);
    });

    it('reports reserved-path on a patched key that takes a routing word or another feature\'s entity segment', () => {
      expect(lines(check(config([gl, { ...acme, reports: [tmCodedReport('signed-out', {})] }]), withReport('signed-out', 'GET /x')))).toEqual([
        'reserved-path report:signed-out acme',
      ]);
      const metadata: Partial<TmClientMetadata> = {
        ...withReport('credit-notes', 'GET /x'),
        entities: [...tmTestMetadata().entities, { ...(tmTestMetadata().entities[0] as TmClientMetadata['entities'][number]), name: 'gl.CreditNote', segment: 'credit-notes' }],
      };
      const glWithNotes = { ...gl, entities: [...(gl.entities ?? []), tmEntity('gl.CreditNote')] };
      const notes = check(config([glWithNotes, { ...acme, reports: [tmCodedReport('credit-notes', {})] }]), metadata);
      expect(lines(notes)).toEqual(['reserved-path report:credit-notes acme']);
      expect(notes[0]?.message).toBe(
        "'report:credit-notes' starts with 'credit-notes', the route segment of the entity 'gl.CreditNote', which the feature 'gl' declares.",
      );
    });

    it('reports coded-report-not-registered', () => {
      const result = check(config([gl, { ...acme, reports: [tmCodedReport('acme-ghost', {})] }]));
      expect(lines(result)).toEqual(['coded-report-not-registered report:acme-ghost acme']);
      expect(result[0]?.message).toBe("No coded report the server declares has the key 'acme-ghost'.");
    });

    it('reports coded-report-redeclared for each member the server owns', () => {
      const patch = tmCodedReport('acme-credit-exposure', { titleKey: 'Mine', route: 'GET /mine', parameters: [] } as never);
      const result = check(config([gl, { ...acme, overrides: [patch] }]));
      expect(lines(result)).toEqual([
        'coded-report-redeclared report:acme-credit-exposure acme',
        'coded-report-redeclared report:acme-credit-exposure acme',
        'coded-report-redeclared report:acme-credit-exposure acme',
      ]);
      expect(result[0]?.message).toBe("A patch of 'report:acme-credit-exposure' sets 'titleKey', which the server declares; the patch's other members apply.");
    });

    it('refuses every member the server owns, one at a time, and applies the rest of the patch', () => {
      const owned: Readonly<Record<string, unknown>> = {
        feature: 'gl',
        route: 'GET /mine',
        securable: ['acme.Customer', 'Delete'],
        kind: 'List',
        titleKey: 'Mine',
        descriptionKey: 'Mine_Description',
        export: 'None',
        parameters: [],
      };
      const server = new Map(Object.entries(tmTestMetadata().codedReports[0] ?? {}));
      for (const [member, value] of Object.entries(owned)) {
        const patch = tmCodedReport('acme-credit-exposure', { [member]: value, order: 7 } as never);
        const merged = ɵtmMergeAppDeclarations([tmFeature({ ...acme, reports: [patch] })], tmTestMetadata());
        expect(merged.problems.map((problem) => [problem.code, problem.facts['member']])).toEqual([
          ['coded-report-redeclared', member],
        ]);
        const report = new Map(Object.entries(merged.declarations.reports[0] ?? {}));
        expect(report.get(member)).toEqual(server.get(member));
        expect(report.get('order')).toBe(7);
      }
    });

    it('reports coded-report-source for a route with run, and a hatch with neither or both', () => {
      const run = () => Promise.resolve(null);
      expect(lines(check(config([gl, { ...acme, reports: [tmCodedReport('acme-credit-exposure', { run })] }])))).toEqual([
        'coded-report-source report:acme-credit-exposure acme',
      ]);

      const hatch = withReport('acme-aging');
      expect(lines(check(config(), hatch))).toEqual(['coded-report-source report:acme-aging acme']);
      expect(check(config(), hatch)[0]?.message).toBe("'report:acme-aging' has no server route, so a patch must give it 'run' or 'component'.");
      const both = config([gl, acme, { key: 'zeta', reports: [tmCodedReport('acme-aging', { run, component: lazy })] }]);
      const zeta = { features: [...tmTestMetadata().features, { key: 'zeta', version: '1' }], ...hatch };
      expect(lines(check(both, zeta))).toEqual(['coded-report-source report:acme-aging zeta']);
      expect(check(config([gl, { ...acme, reports: [tmCodedReport('acme-aging', { component: lazy })] }]), hatch)).toEqual([]);
      expect(check(config([gl, { ...acme, reports: [tmCodedReport('acme-aging', { run })] }]), hatch)).toEqual([]);
    });
  });

  describe('server facts', () => {
    it('reports feature-not-composed, and never for core', () => {
      const result = check(config([gl, acme, { key: 'zeta' }]));
      expect(lines(result)).toEqual(['feature-not-composed feature:zeta zeta']);
      expect(result[0]?.message).toBe("The server composes no feature 'zeta'.");

      // The metadata below lists no `core`. A feature an app lists under that key
      // is a repeat of the implicit feature, never an uncomposed one.
      const withoutCore = { features: tmTestMetadata().features.filter((feature) => feature.key !== 'core') };
      expect(check(config(), withoutCore)).toEqual([]);
      expect(lines(check(config([{ key: 'core' }, gl, acme]), withoutCore))).toEqual(['duplicate-key feature:core core']);
    });

    it('reports language-missing, language-not-offered and default-language-mismatch', () => {
      expect(lines(check(config(undefined, { languages: [language('en')] })))).toEqual(['language-missing language:ar acme']);
      expect(lines(check(config(undefined, { languages: [language('en'), language('ar'), language('fr')] })))).toEqual([
        'language-not-offered language:fr acme',
      ]);
      const mismatch = check(config(undefined, { languages: [language('ar'), language('en')] }));
      expect(lines(mismatch)).toEqual(['default-language-mismatch language:ar acme']);
      expect(mismatch[0]?.message).toBe("The app's first language is 'ar', but the server's default is 'en'.");
      expect(lines(check(config(undefined, { languages: [language('en'), language('ar'), language('en')] })))).toEqual([
        'duplicate-key language:en acme',
      ]);
    });

    it('reports calendar-missing unless the platform or the app defines the calendar', () => {
      const metadata = { calendars: ['gc', 'uq', 'et', 'hj'] };
      expect(lines(check(config(), metadata))).toEqual(['calendar-missing calendar:hj acme']);
      const calendars = [{ code: 'hj', load: () => Promise.reject(new Error('unused')) }];
      expect(check(config(undefined, { calendars }), metadata)).toEqual([]);
    });

    it('reports string-key-collision for keys in the client scopes', () => {
      const result = check(config(), {}, {}, { en: { 'tm.nav.home': 'x', 'tmUi.ok': 'y', tmx: 'z', Acme_Customer: 'c' } });
      expect(lines(result)).toEqual(['string-key-collision string:tm.nav.home acme', 'string-key-collision string:tmUi.ok acme']);
      expect(result[0]?.message).toBe("The en string pack has the key 'tm.nav.home', which collides with the client string scopes 'tm' and 'tmUi'.");
    });

    it('reports unknown-setting-category', () => {
      const result = check(config([gl, { ...acme, settingsTabs: [{ category: 'general', component: lazy }, { category: 'acme', component: lazy }] }]));
      expect(lines(result)).toEqual(['unknown-setting-category settingsTab:acme acme']);
    });

    it('reports widget-missing and widget-not-registered', () => {
      expect(lines(check(config([gl, { ...acme, widgets: [] }])))).toEqual(['widget-missing widget:acme.cash-position acme']);
      const extra = tmWidget('gl.trial-balance', { labelKey: 'W', icon: 'w', component: lazy, defaultSize: { w: 1, h: 1 }, minSize: { w: 1, h: 1 } });
      expect(lines(check(config([{ ...gl, widgets: [extra] }, acme])))).toEqual(['widget-not-registered widget:gl.trial-balance gl']);
    });
  });

  describe('entity declarations', () => {
    /** The problems of one extra entity declaration by `acme`, merged as an override of acme.Customer. */
    function customer(members: Readonly<Record<string, unknown>>): string[] {
      return lines(check(config([gl, acme, { key: 'acme2', overrides: [tmEntity('acme.Customer', members)] }]), {
        features: [...tmTestMetadata().features, { key: 'acme2', version: '1' }],
      }));
    }

    it('reports unknown-path in columns, orderBy, tile and picker paths, against the feature that set them', () => {
      expect(customer({ columns: ['Nope', { path: 'Center.Nope' }, { path: 'Name', select: ['Center.Manager.Nope'] }] })).toEqual([
        'unknown-path entity:acme.Customer acme2',
        'unknown-path entity:acme.Customer acme2',
        'unknown-path entity:acme.Customer acme2',
      ]);
      expect(customer({ orderBy: 'Nope:desc' })).toEqual(['unknown-path entity:acme.Customer acme2']);
      expect(customer({ tile: { image: 'Nope', badge: 'Center.Manager' } })).toEqual(['unknown-path entity:acme.Customer acme2']);
      expect(customer({ tile: { component: lazy } })).toEqual([]);
      expect(customer({ picker: { description: ['Code', 'Nope'], orderBy: 'Center.Nope' } })).toEqual([
        'unknown-path entity:acme.Customer acme2',
        'unknown-path entity:acme.Customer acme2',
      ]);
      expect(customer({ columns: ['name', 'center.manager.EMAIL', 'Center'] })).toEqual([]);
      const [problem] = check(config([gl, { ...acme, entities: [tmEntity('acme.Customer', { section: 'sales', orderBy: 'Center.Nope' })] }]));
      expect(problem?.message).toBe("The orderBy path 'Center.Nope' of 'entity:acme.Customer' does not resolve.");
    });

    it('reports unknown-action', () => {
      expect(customer({ actions: [{ name: 'set-credit-limit' }, { name: 'archive' }] })).toEqual(['unknown-action entity:acme.Customer acme2']);
    });

    it('reports operation-missing and entity-not-routable', () => {
      const blob = check(config([gl, { ...acme, entities: [...(acme.entities ?? []), tmEntity('core.Blob', { details: lazy, columns: ['Name'], section: 'sales' })] }]));
      expect(blob.map((problem) => problem.message)).toEqual([
        "'entity:core.Blob' sets 'details', which needs the 'details' operation the entity lacks.",
        "'entity:core.Blob' sets 'columns', which needs the 'query' operation the entity lacks.",
        "'entity:core.Blob' sets a section, but the entity has no route segment.",
      ]);
      expect(blob.map((problem) => problem.code)).toEqual(['operation-missing', 'operation-missing', 'entity-not-routable']);

      const users = tmTestMetadata().entities.map((summary) =>
        summary.name === 'core.User' ? { ...summary, operations: ['details' as const] } : summary,
      );
      const userMetadata = tmTestEntitiesChunk().entities.map((entity) =>
        entity.name === 'core.User' ? { ...entity, operations: ['details' as const] } : entity,
      );
      const noQuery = check(
        config([gl, { ...acme, entities: [...(acme.entities ?? []), tmEntity('core.User', { section: 'sales' })] }]),
        { entities: users },
        { entities: userMetadata },
      );
      expect(noQuery.map((problem) => problem.message)).toEqual(["'entity:core.User' sets a section, but the entity has no 'query' operation."]);
    });
  });

  describe('labels', () => {
    it('warns label-missing once per label key, and passes', () => {
      const [first] = tmTestEntitiesChunk().entities;
      if (first === undefined) {
        throw new Error('The test metadata lacks entities.');
      }
      const summaries = tmTestMetadata().entities.map((summary) =>
        summary.name === first.name ? { ...summary, labelFallback: 'Customer' } : summary,
      );
      const entities = tmTestEntitiesChunk().entities.map((entity) =>
        entity.name === first.name
          ? { ...entity, labelFallback: 'Customer', properties: entity.properties.map((p) => (p.name === 'Code' ? { ...p, labelFallback: 'Code' } : p)) }
          : entity,
      );
      const result = check(config(), { entities: summaries }, { entities });
      expect(lines(result)).toEqual(['label-missing label:acme_Customer acme', 'label-missing label:acme_Customer_Code acme']);
      expect(result.map((problem) => problem.severity)).toEqual(['warning', 'warning']);
      expect(result[0]?.message).toBe("No string resource declares 'acme_Customer', so the emit wrote the fallback 'Customer'.");
    });

    it('warns label-missing for every kind of label the emit writes a fallback for', () => {
      const metadata = tmTestMetadata();
      const customer = tmTestEntitiesChunk().entities.find((entity) => entity.name === 'acme.Customer');
      const [action] = customer?.actions ?? [];
      const [securable] = metadata.securables;
      const [handler] = metadata.jobHandlers;
      if (customer === undefined || action === undefined || securable === undefined || handler === undefined) {
        throw new Error('The test metadata lacks acme.Customer, its action, a securable or a job handler.');
      }
      const withFallbacks = {
        ...customer,
        navigations: customer.navigations.map((navigation) => ({ ...navigation, labelFallback: 'Center' })),
        children: [
          {
            name: 'Notes',
            json: 'notes',
            entity: 'acme.CustomerNote',
            parentKey: 'CustomerId',
            maxCount: 100,
            labelKey: 'acme_Customer_Notes',
            labelFallback: 'Notes',
          },
        ],
        actions: [
          {
            ...action,
            labelFallback: 'Set credit limit',
            fields: [
              {
                name: 'CreditLimit',
                labelKey: 'acme_Customer_CreditLimit',
                labelFallback: 'Credit limit',
                required: true,
                value: { type: 'decimal' as const, nullable: false },
              },
            ],
          },
        ],
      };
      const result = check(
        config(),
        {
          securables: [{ ...securable, resourceLabelFallback: 'Customers', actionLabelFallback: 'Read', featureLabelFallback: 'Acme' }],
          jobHandlers: [{ ...handler, labelFallback: 'Notification delivery' }],
        },
        {
          entities: tmTestEntitiesChunk().entities.map((entity) => (entity.name === customer.name ? withFallbacks : entity)),
          enums: [{ name: 'gl.Tier', values: [{ value: 'A', labelKey: 'gl_Tier_A', labelFallback: 'A' }, { value: 'B', label: 'B' }] }],
        },
      );
      expect(lines(result)).toEqual([
        'label-missing label:acme_Customer_Center acme',
        'label-missing label:acme_Customer_Notes acme',
        'label-missing label:acme_Customer_Action_set-credit-limit acme',
        'label-missing label:acme_Customer_CreditLimit acme',
        'label-missing label:gl_Tier_A gl',
        'label-missing label:acme_Customer_Plural acme',
        'label-missing label:Action_Read acme',
        'label-missing label:Feature_acme acme',
        'label-missing label:Job_NotificationDelivery core',
      ]);
      expect(result.every((problem) => problem.severity === 'warning')).toBe(true);
    });
  });
});
