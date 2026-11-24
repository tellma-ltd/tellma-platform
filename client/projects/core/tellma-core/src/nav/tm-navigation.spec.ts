// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { tmWarmTenant, type TmFakeApi, type TmFakeTelemetry } from '@tellma/core-testing';

import type { TmLanguageDefinition } from '../app/tm-app-config';
import { tmFeature, type TmFeature, type TmFeatureDefinition } from '../declarations/tm-feature';
import { tmOverride } from '../declarations/tm-override';
import { tmPage } from '../declarations/tm-page';
import { tmEntity } from '../entity/tm-entity-declaration';
import { tmEnglish } from '../l10n/tm-english';
import { TmLocalization } from '../l10n/tm-localization';
import type { TmClientMetadata } from '../metadata/tm-client-metadata';
import { tmCodedReport } from '../reports/tm-coded-report';
import {
  tmAppRig,
  tmLoadTestPage,
  tmNavigate,
  tmRouteTestMetadata,
  tmSettle,
  tmSettleUntil,
  TM_T17,
  TM_T17_DOCS,
  TM_TEST_SUB,
} from '../routes/tm-app.testing.util';
import type { TmLaunchDocs, TmMeResult } from '../state/launch/tm-launch-docs';
import type { TmClientStore } from '../state/store/tm-client-store';
import type { TmDistributionInfo } from '../state/tm-distribution-info';
import type { TmNavEntry } from './tm-nav-declarations';
import { TmNavigation, type TmNavItem } from './tm-navigation';

// The navigation registry in a whole test app: a feature with three sections,
// entities with and without a details page, pages with and without a
// parameter or a securable, a patched coded report and a feature entry, under
// the access each test grants. The distribution document stays absent unless
// a test stores one.

/** A permission, as the access document grants it. */
type TmGrant = readonly [resource: string, action: string];

/** Every permission the feature's entries ask for. */
const ALL: readonly TmGrant[] = [
  ['acme.Customer', 'Read'],
  ['acme.Customer', 'Save'],
  ['gl.Center', 'Read'],
  ['acme.Approval', 'Read'],
  ['core.Settings', 'Read'],
];

/** The feature entry whose permissions are any of two. */
const OVERDUE: TmNavEntry = {
  id: 'acme.overdue',
  section: 'sales',
  order: 60,
  labelKey: 'Acme_Overdue',
  url: (t) => `/${t}/customers?f.Overdue=eq:true`,
  requires: [
    ['acme.Customer', 'Read'],
    ['gl.Center', 'Read'],
  ],
};

/** The test feature, with the members a test replaces. */
function feature(patch: Partial<TmFeatureDefinition> = {}): TmFeature {
  return tmFeature({
    key: 'acme',
    sections: [
      { id: 'sales', labelKey: 'Acme_Nav_Sales', icon: 'users', order: 20 },
      { id: 'finance', labelKey: 'Acme_Nav_Finance', icon: 'wallet', order: 10 },
      { id: 'desk', labelKey: 'Acme_Nav_Desk', icon: 'inbox', order: 30 },
    ],
    entities: [
      tmEntity('acme.Customer', { details: tmLoadTestPage, section: 'sales', order: 10, icon: 'users' }),
      tmEntity('gl.Center', { section: 'finance' }),
      tmEntity('acme.Currency', { details: tmLoadTestPage }),
      tmEntity('acme.SalesInvoiceLine', {}),
      tmEntity('core.Report', { details: tmLoadTestPage }),
    ],
    pages: [
      tmPage('credit-review', { component: tmLoadTestPage, titleKey: 'Acme_CreditReview', section: 'sales', order: 10 }),
      tmPage('centers/:id/map', { component: tmLoadTestPage, titleKey: 'Acme_CenterMap', section: 'finance' }),
      tmPage('approvals', { component: tmLoadTestPage, titleKey: 'Acme_Approvals', securable: ['acme.Approval', 'Read'], section: 'desk' }),
    ],
    reports: [tmCodedReport('acme-credit-exposure', { section: 'analysis' })],
    entries: [OVERDUE],
    ...patch,
  });
}

/** The distribution's strings. */
const STRINGS = {
  en: {
    Acme_Nav_Sales: 'Sales',
    Acme_Nav_Finance: 'Finance',
    Acme_Nav_Desk: 'Desk',
    acme_Customer_Plural: 'Customers',
    acme_Currency_Plural: 'Currencies',
    acme_SalesInvoiceLine_Plural: 'Invoice lines',
    core_Report_Plural: 'Reports',
    Acme_CreditReview: 'Credit review',
    Acme_Approvals: 'Approvals',
    Acme_Report_CreditExposure: 'Credit exposure',
    Acme_Overdue: 'Overdue customers',
  },
  ar: {
    Acme_Nav_Sales: 'المبيعات',
    acme_Customer_Plural: 'العملاء',
    Acme_CreditReview: 'مراجعة الائتمان',
    Acme_Overdue: 'العملاء المتأخرون',
  },
};

/** Arabic, with the platform strings the tests read. */
const ARABIC: TmLanguageDefinition = {
  code: 'ar',
  dir: 'rtl',
  strings: () => Promise.resolve({ tm: { nav: { me: { profile: 'ملفي الشخصي' } } }, tmUi: {} }),
};

/** An access document that grants the permissions. */
function access(grants: readonly TmGrant[]): TmMeResult['access'] {
  return { ...TM_T17_DOCS.me.access, securables: grants.map(([resource, action]) => ({ resource, action, hasFilter: false })) };
}

/** A distribution document whose support switch is on or off. */
function distribution(enabled: boolean): TmDistributionInfo {
  return { support: { enabled, providerAccess: false } } as TmDistributionInfo;
}

/** How a test sets the app up. */
interface TmRigOptions {
  /** The feature, when not the test feature. */
  readonly feature?: TmFeature;
  /** A distribution's feature, merged after the test feature. */
  readonly distro?: Omit<TmFeatureDefinition, 'key'>;
  /** The permissions granted; all of them by default. */
  readonly grants?: readonly TmGrant[];
  /** Whether the user prefers Arabic, which the tenant offers. */
  readonly arabic?: boolean;
  /** Whether the tenant offers Arabic beside English. */
  readonly offersArabic?: boolean;
  /** The Core chunk, when not the route test metadata. */
  readonly metadata?: TmClientMetadata;
  /** The distribution document stored before the app enters the tenant. */
  readonly distribution?: TmDistributionInfo;
}

/** What a test drives and inspects. */
interface TmRig {
  /** The registry. */
  readonly navigation: TmNavigation;
  /** The client store. */
  readonly store: TmClientStore;
  /** The fake telemetry. */
  readonly telemetry: TmFakeTelemetry;
  /** The fake API. */
  readonly api: TmFakeApi;
}

/** Starts the app, enters tenant 17 and returns the registry. */
async function rig(options: TmRigOptions = {}): Promise<TmRig> {
  const { me, settings } = TM_T17_DOCS;
  const docs: TmLaunchDocs = {
    ...TM_T17_DOCS,
    me: { ...me, user: { ...me.user, ...(options.arabic && { preferredLanguage: 'ar' }) }, access: access(options.grants ?? ALL) },
    settings:
      options.arabic || options.offersArabic
      ? { ...settings, languages: [...settings.languages, { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true }] }
      : settings,
  };
  const app = await tmAppRig({
    docs,
    features: [options.feature ?? feature(), tmFeature({ key: 'distro', ...options.distro })],
    languages: [tmEnglish(), ARABIC],
    strings: STRINGS,
    ...(options.metadata && { metadata: options.metadata }),
  });
  // The distribution document stays as the test stores it: its read fails.
  app.api.respond('/api/distribution-info', () => {
    throw new Error('The distribution document is unreachable.');
  });
  if (options.distribution !== undefined) {
    await putDistribution(app.store, options.distribution);
  }
  await tmNavigate('/17/credit-review');
  await tmSettle();
  return { navigation: TestBed.inject(TmNavigation), store: app.store, telemetry: app.telemetry, api: app.api };
}

/** Stores the distribution document. */
async function putDistribution(store: TmClientStore, data: TmDistributionInfo): Promise<void> {
  await store.put({ kind: 'user', sub: TM_TEST_SUB }, 'distribution', { tag: null, savedAt: Date.now(), data });
}

/** The sections as `id: entry ids` lines. */
function outline(navigation: TmNavigation): string[] {
  return navigation.sections().map((section) => `${section.id}: ${section.entries.map((item) => item.id).join(' ')}`);
}

/** The visible entries that no shown section holds, by id. */
function searchOnly(navigation: TmNavigation): string[] {
  const shown = new Set(navigation.sections().flatMap((section) => section.entries));
  return navigation
    .items()
    .filter((item) => !shown.has(item))
    .map((item) => item.id);
}

/** The visible entry of an id. */
function item(navigation: TmNavigation, id: string): TmNavItem {
  const found = navigation.items().find((candidate) => candidate.id === id);
  if (found === undefined) {
    throw new Error(`No visible entry '${id}'.`);
  }
  return found;
}

describe('TmNavigation', () => {
  describe('entries and sections', () => {
    it('derives an entry from every entity, page without a parameter, coded report and feature entry', async () => {
      const { navigation } = await rig();
      expect(outline(navigation)).toEqual([
        'finance: gl.Center',
        'sales: credit-review acme.Customer acme.overdue',
        'desk: approvals',
        'analysis: acme-credit-exposure',
        'administration: core.settings',
      ]);
      expect(navigation.sections().map((section) => section.label)).toEqual(['Finance', 'Sales', 'Desk', 'Analysis', 'Administration']);
      const rows = navigation.items().map((entry) => [entry.id, entry.kind, entry.href, entry.icon, entry.label]);
      expect(rows.slice(0, 7)).toEqual([
        ['gl.Center', 'entity', '/17/centers', 'file-text', 'Centers'],
        ['credit-review', 'page', '/17/credit-review', 'square-dashed', 'Credit review'],
        ['acme.Customer', 'entity', '/17/customers', 'users', 'Customers'],
        ['acme.overdue', 'entry', '/17/customers?f.Overdue=eq:true', 'link', 'Overdue customers'],
        ['approvals', 'page', '/17/approvals', 'square-dashed', 'Approvals'],
        ['acme-credit-exposure', 'report', '/17/reports/acme-credit-exposure', 'chart-column', 'Credit exposure'],
        ['core.settings', 'entry', '/17/settings', 'settings', 'Settings'],
      ]);
    });

    it('lists the entries without a section after the sections, by order and label', async () => {
      const { navigation } = await rig({ grants: [...ALL, ['acme.Currency', 'Read'], ['acme.SalesInvoiceLine', 'Read']] });
      expect(searchOnly(navigation)).toEqual([
        'acme.Currency',
        'core.me.display',
        'core.me.region',
        'core.me.profile',
        'core.me',
        'core.me.notifications',
      ]);
      expect(item(navigation, 'core.me.profile')).toMatchObject({ href: '/17/me/profile', icon: 'user', order: 100, label: 'My profile' });
    });

    it('derives no entry from a page with a parameter or a child entity, which no search page shows', async () => {
      const { navigation } = await rig({ grants: [...ALL, ['acme.SalesInvoiceLine', 'Read']] });
      const ids = navigation.items().map((entry) => entry.id);
      expect(ids).not.toContain('centers/:id/map');
      expect(ids).not.toContain('acme.SalesInvoiceLine');
    });

    it('offers a create page on an entity the user may save that has a details page', async () => {
      const { navigation } = await rig({ grants: [...ALL, ['acme.Currency', 'Read'], ['acme.Currency', 'Save']] });
      expect(item(navigation, 'acme.Customer').createHref).toBe('/17/customers/new');
      expect(item(navigation, 'gl.Center').createHref).toBeNull();
      expect(item(navigation, 'acme.Currency').createHref).toBe('/17/currencies/new');
      expect(item(navigation, 'credit-review').createHref).toBeNull();
    });

    it('defaults the synonyms key to the label key with a suffix', async () => {
      const { navigation } = await rig();
      expect(item(navigation, 'acme.overdue').synonymsKey).toBe('Acme_Overdue_Synonyms');
      expect(item(navigation, 'acme.Customer').synonymsKey).toBe('acme_Customer_Plural_Synonyms');
    });
  });

  describe('permissions', () => {
    it('shows an entry when any one of its permissions passes, and hides a section left empty', async () => {
      const { navigation } = await rig({ grants: [['gl.Center', 'Read']] });
      expect(outline(navigation)).toEqual(['finance: gl.Center', 'sales: credit-review acme.overdue']);
      expect(item(navigation, 'gl.Center').createHref).toBeNull();
    });

    it('shows Settings with Read on any settings category', async () => {
      const without = await rig({ grants: [['gl.Center', 'Read']] });
      expect(without.navigation.items().some((entry) => entry.id === 'core.settings')).toBe(false);
      TestBed.resetTestingModule();
      const granted = await rig({ grants: [['core.Settings', 'Read']] });
      expect(outline(granted.navigation)).toContain('administration: core.settings');
    });

    it('hides Settings and refuses its routes when the Core chunk lists no settings category', async () => {
      const { navigation } = await rig({ metadata: { ...tmRouteTestMetadata(), settingCategories: [] } });
      expect(navigation.items().some((entry) => entry.id === 'core.settings')).toBe(false);
      expect(navigation.resolve('/17/settings')).toBe('no-access');
      expect(navigation.resolve('/17/settings/general')).toBe('no-access');
    });

    it('follows the access when it changes', async () => {
      const { navigation, store } = await rig({ grants: [['gl.Center', 'Read']] });
      expect(navigation.items().some((entry) => entry.id === 'acme.Customer')).toBe(false);
      await store.patch<TmMeResult>(TM_T17, 'me', (me) => me && { ...me, access: access([['acme.Customer', 'Read']]) });
      await tmSettle();
      expect(outline(navigation)).toEqual(['sales: credit-review acme.Customer acme.overdue', 'analysis: acme-credit-exposure']);
    });
  });

  describe('visibleWhen', () => {
    /** The test feature with its credit review page shown only while support is enabled. */
    const gated = (): TmFeature =>
      feature({
        pages: [
          tmPage('credit-review', {
            component: tmLoadTestPage,
            titleKey: 'Acme_CreditReview',
            section: 'sales',
            visibleWhen: ({ distribution }) => distribution.support.enabled,
          }),
        ],
      });

    it('hides an entry while the distribution document is absent, and follows the document', async () => {
      const { navigation, store } = await rig({ feature: gated() });
      expect(navigation.byHref('/17/credit-review')).toBeUndefined();
      expect(navigation.resolve('/17/credit-review')).toBe('unavailable');

      await putDistribution(store, distribution(true));
      await tmSettle();
      expect(navigation.byHref('/17/credit-review')?.label).toBe('Credit review');
      expect(navigation.resolve('/17/credit-review')).toBe('ok');

      await putDistribution(store, distribution(false));
      await tmSettle();
      expect(navigation.byHref('/17/credit-review')).toBeUndefined();
      expect(navigation.resolve('/17/credit-review?q=acme')).toBe('unavailable');
    });

    it('makes the URL of an entry it hides unavailable, though the page there has no predicate', async () => {
      const desk: TmNavEntry = { id: 'acme.desk', labelKey: 'Acme_Nav_Desk', url: (t) => `/${t}/approvals`, visibleWhen: () => false };
      const { navigation } = await rig({ feature: feature({ entries: [OVERDUE, desk] }), distribution: distribution(true) });
      expect(navigation.byHref('/17/approvals')?.id).toBe('approvals');
      expect(navigation.resolve('/17/approvals')).toBe('unavailable');
      expect(navigation.resolve('/17/approvals?q=late')).toBe('unavailable');
    });

    it('hides the entries of an entity it hides, and its routes', async () => {
      const { navigation } = await rig({
        feature: feature({
          entities: [tmEntity('acme.Customer', { details: tmLoadTestPage, section: 'sales', visibleWhen: () => false })],
        }),
        distribution: distribution(true),
      });
      expect(navigation.items().some((entry) => entry.id === 'acme.Customer')).toBe(false);
      expect(navigation.resolve('/17/customers')).toBe('unavailable');
      expect(navigation.resolve('/17/customers/7')).toBe('unavailable');
    });

    it('hides an entry whose predicate throws, and reports it once', async () => {
      const broken: TmNavEntry = {
        id: 'acme.broken',
        labelKey: 'Acme_Broken',
        url: (t) => `/${t}/approvals`,
        visibleWhen: () => {
          throw new Error('The predicate failed.');
        },
      };
      const { navigation, store, telemetry } = await rig({ feature: feature({ entries: [OVERDUE, broken] }), distribution: distribution(true) });
      expect(navigation.items().some((entry) => entry.id === 'acme.broken')).toBe(false);
      await putDistribution(store, distribution(false));
      await tmSettle();
      expect(navigation.items().some((entry) => entry.id === 'acme.broken')).toBe(false);
      const reports = telemetry.recent().filter((entry) => entry.props['where'] === 'tm.nav.visible-when');
      expect(reports.map((entry) => entry.props['entry'])).toEqual(['acme.broken']);
    });
  });

  describe('hide and overrides', () => {
    it('drops hidden entries and sections from the launcher and keeps their routes', async () => {
      const { navigation } = await rig({
        distro: {
          hide: ['entry:acme.Customer', 'section:desk', 'entry:core.settings'],
          overrides: [tmOverride('section:analysis', { order: 5 }), tmOverride('entry:acme.overdue', { labelKey: 'Acme_CreditReview', order: 1 })],
        },
      });
      expect(outline(navigation)).toEqual(['analysis: acme-credit-exposure', 'finance: gl.Center', 'sales: acme.overdue credit-review']);
      expect(navigation.resolve('/17/customers')).toBe('ok');
      expect(navigation.resolve('/17/approvals')).toBe('ok');
      // A hidden Settings entry still gates its route.
      expect(navigation.resolve('/17/settings/general')).toBe('ok');
    });

    it('gates the settings route by the platform rule when Settings is hidden', async () => {
      const { navigation } = await rig({ distro: { hide: ['entry:core.settings'] }, grants: [['gl.Center', 'Read']] });
      expect(navigation.resolve('/17/settings')).toBe('no-access');
    });
  });

  describe('language', () => {
    it('labels and orders the entries in the active language', async () => {
      const { navigation } = await rig({ arabic: true });
      expect(TestBed.inject(TmLocalization).language()).toBe('ar');
      expect(outline(navigation)).toContain('sales: acme.Customer credit-review acme.overdue');
      expect(item(navigation, 'acme.Customer').label).toBe('العملاء');
    });
  });

  describe('recomputation', () => {
    it('labels and re-sorts the entries when the language switches', async () => {
      const { navigation } = await rig({ offersArabic: true });
      expect(outline(navigation)).toContain('sales: credit-review acme.Customer acme.overdue');
      const l10n = TestBed.inject(TmLocalization);
      l10n.setLanguage('ar');
      // In a tenant the switch first loads the locale write's chunk.
      await tmSettleUntil(() => l10n.language() === 'ar');
      expect(outline(navigation)).toContain('sales: acme.Customer credit-review acme.overdue');
      expect(item(navigation, 'acme.Customer').label).toBe('العملاء');
      expect(navigation.sections().find((section) => section.id === 'sales')?.label).toBe('المبيعات');
    });

    it('follows the routed tenant', async () => {
      const { navigation, store } = await rig();
      const t18 = { ...TM_T17, tenantId: 18 };
      const docs = TM_T17_DOCS;
      await tmWarmTenant(store, t18, {
        ...docs,
        settings: { ...docs.settings, tenantId: 18 },
        tenant: { ...docs.tenant, id: 18 },
        me: { ...docs.me, access: access(ALL) },
      });
      expect(navigation.items().every((entry) => entry.href.startsWith('/17/'))).toBe(true);
      await TestBed.inject(Router).navigateByUrl('/18/credit-review');
      await tmSettle();
      expect(navigation.items().length).toBeGreaterThan(0);
      expect(navigation.items().every((entry) => entry.href.startsWith('/18/'))).toBe(true);
      expect(navigation.byHref('/18/centers')?.id).toBe('gl.Center');
      expect(item(navigation, 'acme.Customer').createHref).toBe('/18/customers/new');
    });
  });

  describe('byHref and resolve', () => {
    it('finds the visible entry at a URL, its query included', async () => {
      const { navigation } = await rig();
      expect(navigation.byHref('/17/centers')?.id).toBe('gl.Center');
      expect(navigation.byHref('/17/customers?f.Overdue=eq:true')?.id).toBe('acme.overdue');
      expect(navigation.byHref('/17/customers?q=acme')).toBeUndefined();
    });

    it('judges a stored URL by its route and the permission check', async () => {
      const { navigation } = await rig({ grants: [['gl.Center', 'Read'], ['acme.SalesInvoiceLine', 'Read']] });
      const states = Object.fromEntries(
        [
          '/17/centers?q=jed',
          '/17/customers',
          '/17/customers/new',
          '/17/customers/5',
          '/17/reports/9',
          '/17/reports/acme-credit-exposure',
          '/17/approvals',
          '/17/credit-review',
          '/17/list/sales-invoice-lines#d=eyJm',
          '/17/settings',
          '/17/me/profile',
          '/17',
          '/17/nothing-here',
          '/tenants',
        ].map((url) => [url, navigation.resolve(url)]),
      );
      expect(states).toEqual({
        '/17/centers?q=jed': 'ok',
        '/17/customers': 'no-access',
        '/17/customers/new': 'no-access',
        '/17/customers/5': 'no-access',
        // A stored report's page reports a refusal itself.
        '/17/reports/9': 'ok',
        '/17/reports/acme-credit-exposure': 'no-access',
        '/17/approvals': 'no-access',
        '/17/credit-review': 'ok',
        '/17/list/sales-invoice-lines#d=eyJm': 'ok',
        '/17/settings': 'no-access',
        '/17/me/profile': 'ok',
        '/17': 'ok',
        '/17/nothing-here': 'unavailable',
        '/tenants': 'unavailable',
      });
    });
  });
});
