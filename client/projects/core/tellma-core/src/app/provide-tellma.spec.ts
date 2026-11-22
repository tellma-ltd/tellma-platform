// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { provideLocationMocks } from '@angular/common/testing';
import { type ApplicationConfig, ApplicationInitStatus, type Provider, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTellmaStateTesting, provideTellmaTesting, TmFakeTelemetry, tmTestEntitiesChunk, tmTestMetadata } from '@tellma/core-testing';

import { TmApi } from '../api/tm-api';
import { TmHttpApi } from '../api/tm-http-api';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import type { TmBootResult } from '../boot/tm-boot-types';
import { tmFeature } from '../declarations/tm-feature';
import { tmEntity } from '../entity/tm-entity-declaration';
import { TM_CLIENT_METADATA, TmClientMetadataStore } from '../metadata/tm-client-metadata';
import type { TmMetadataLoaders } from '../metadata/tm-metadata-loaders';
import { provideTellma, tmAppConfigOf } from './provide-tellma';
import type { TmAppConfig } from './tm-app-config';
import { TM_APP_DECLARATIONS } from './tm-app-declarations';

/** Loaders over the test metadata, which the startup initializer calls. */
const loaders: TmMetadataLoaders = {
  core: () => Promise.resolve(tmTestMetadata()),
  entities: () => Promise.resolve(tmTestEntitiesChunk()),
  contracts: () => Promise.resolve({ fingerprints: {}, long: [], idempotent: [] }),
  strings: {},
};

/** An app configuration with one feature. */
const config: TmAppConfig = {
  metadata: loaders,
  languages: [{ code: 'en', dir: 'ltr', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) }],
  features: [tmFeature({ key: 'acme', entities: [tmEntity('acme.Customer', { order: 1 })] })],
};

describe('provideTellma', () => {
  it('provides the merged declarations, built from the loaded metadata', () => {
    TestBed.configureTestingModule({ providers: [provideTellma(config), provideTellmaTesting(), provideTellmaStateTesting()] });
    TestBed.inject(TmClientMetadataStore).set(tmTestMetadata());

    const declarations = TestBed.inject(TM_APP_DECLARATIONS);
    expect(declarations.features).toEqual(['core', 'acme']);
    expect(declarations.entities.get('acme.Customer')?.['order']).toBe(1);
    expect(declarations.reports.map((report) => report.key)).toEqual(['acme-credit-exposure']);
  });
});

describe('TM_CLIENT_METADATA', () => {
  it('reads the Core chunk the boot sequence keeps', () => {
    const metadata = tmTestMetadata();
    TestBed.inject(TmClientMetadataStore).set(metadata);
    expect(TestBed.inject(TM_CLIENT_METADATA)).toBe(metadata);
  });

  it('throws when injected before the Core chunk is loaded', () => {
    expect(() => TestBed.inject(TM_CLIENT_METADATA)).toThrow(/The client metadata is not loaded yet/);
  });
});

describe('tmAppConfigOf', () => {
  it('finds the config provideTellma received, however deep its providers nest', () => {
    const appConfig: ApplicationConfig = {
      providers: [provideZonelessChangeDetection(), [[provideTellma(config)]]],
    };
    expect(tmAppConfigOf(appConfig)).toBe(config);
  });

  it('answers null when no provider came from provideTellma', () => {
    expect(tmAppConfigOf({ providers: [provideZonelessChangeDetection()] })).toBeNull();
    expect(tmAppConfigOf({ providers: [] })).toBeNull();
  });
});

describe('the startup sequence', () => {
  /** The test page's own address, restored after each case. */
  const address = location.pathname + location.search + location.hash;

  afterEach(() => {
    history.replaceState(history.state, '', address);
    TestBed.resetTestingModule();
  });

  /** A boot result whose boot page the spec watches. */
  function bootResult(): TmBootResult {
    return { state: 'run', profile: null, record: null, cold: false, auto: null, mark: null, showPage: vi.fn() };
  }

  /** Configures the app over the given loaders and providers, and starts its initializers. */
  function start(metadata: TmMetadataLoaders, providers: readonly Provider[] = []): Promise<unknown> {
    TestBed.configureTestingModule({
      providers: [
        provideTellma({ ...config, metadata }),
        provideTellmaTesting(),
        provideTellmaStateTesting(),
        provideLocationMocks(),
        ...providers,
      ],
    });
    return TestBed.inject(ApplicationInitStatus).donePromise;
  }

  it.each(['core', 'strings'])('shows the boot page load-failed when the %s load rejects', async (chunk) => {
    const boot = bootResult();
    const failure = (): Promise<never> => Promise.reject(new TypeError('Failed to fetch'));
    const metadata: TmMetadataLoaders = chunk === 'core' ? { ...loaders, core: failure } : { ...loaders, strings: { en: failure } };
    let started = false;
    void start(metadata, [{ provide: TM_BOOT_RESULT, useValue: boot }]).then(() => (started = true));

    await vi.waitFor(() => expect(boot.showPage).toHaveBeenCalledWith('load-failed', expect.any(Function)));
    const events = TestBed.inject(TmFakeTelemetry).recent().filter((item) => item.name === 'tm.boot.load-failed');
    expect(events.map((item) => item.props)).toEqual([{ chunk }]);
    // The app never starts under the boot page.
    await new Promise((resolve) => setTimeout(resolve));
    expect(started).toBe(false);
  });

  it('drops a trailing slash from the document address before the first navigation', async () => {
    history.replaceState(history.state, '', '/17/centers/?q=x#top');
    await start(loaders);
    expect(location.pathname + location.search + location.hash).toBe('/17/centers?q=x#top');
  });

  it('loads the Contracts chunk after the first render, at idle', async () => {
    const contracts = vi.fn(loaders.contracts);
    await start({ ...loaders, contracts }, [{ provide: TmApi, useExisting: TmHttpApi }]);
    expect(contracts).not.toHaveBeenCalled();

    TestBed.tick();
    await vi.waitFor(() => expect(contracts).toHaveBeenCalledTimes(1));
  });
});
