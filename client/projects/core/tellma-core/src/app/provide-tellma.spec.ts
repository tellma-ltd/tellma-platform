// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { provideLocationMocks } from '@angular/common/testing';
import { type ApplicationConfig, ApplicationInitStatus, Injectable, type Provider, provideZonelessChangeDetection } from '@angular/core';
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
import { TM_STATE_PARTS } from '../state/tm-state-dump';
import { provideTellma, tmAppConfigOf } from './provide-tellma';
import type { TmAppConfig } from './tm-app-config';
import { TM_APP_DECLARATIONS } from './tm-app-declarations';
import { TM_RECENTS_LOAD, type TmRecentsModule } from './tm-app-init';

/** Loaders over the test metadata, which the startup initializer calls. */
const loaders: TmMetadataLoaders = {
  core: () => Promise.resolve(tmTestMetadata()),
  entities: () => Promise.resolve(tmTestEntitiesChunk()),
  contracts: () => Promise.resolve({ fingerprints: {}, long: [], idempotent: [] }),
  strings: {},
};

/** How many times the stand-in for the recent pages' service was created. */
let recentsCreated = 0;

/** A stand-in for the recent pages' service, which counts its creations. */
@Injectable({ providedIn: 'root' })
class TmCountedRecents {
  /** Counts the creation. */
  constructor() {
    recentsCreated++;
  }
}

/** A stand-in for the recent pages' service that fails to start. */
@Injectable({ providedIn: 'root' })
class TmFailingRecents {
  /** Fails. */
  constructor() {
    throw new Error('The recent pages failed to start.');
  }
}

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

  it('creates the preferences at startup, so they hear the other tabs from the first frame', async () => {
    await start(loaders);
    // The preferences report their part of the dump once created.
    expect(TestBed.inject(TM_STATE_PARTS).preferences).toBeDefined();
  });

  it('creates the recent pages after the first render, from their entry point', async () => {
    recentsCreated = 0;
    const load = vi.fn(() => Promise.resolve<TmRecentsModule>({ TmRecents: TmCountedRecents }));
    await start(loaders, [{ provide: TM_RECENTS_LOAD, useValue: load }]);
    expect(load).not.toHaveBeenCalled();

    TestBed.tick();
    await vi.waitFor(() => expect(recentsCreated).toBe(1));
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('creates no recent pages when the app is torn down before their entry point loads', async () => {
    recentsCreated = 0;
    let loaded!: (module: TmRecentsModule) => void;
    const load = vi.fn(() => new Promise<TmRecentsModule>((resolve) => (loaded = resolve)));
    await start(loaders, [{ provide: TM_RECENTS_LOAD, useValue: load }]);
    TestBed.tick();
    await vi.waitFor(() => expect(load).toHaveBeenCalledTimes(1));
    const telemetry = TestBed.inject(TmFakeTelemetry);

    TestBed.resetTestingModule();
    loaded({ TmRecents: TmCountedRecents });
    await new Promise((resolve) => setTimeout(resolve));
    expect(recentsCreated).toBe(0);
    expect(telemetry.errors()).toEqual([]);
  });

  it.each([
    ['a failed load of the recent pages', () => Promise.reject(new TypeError('Failed to fetch dynamically imported module'))],
    ['a recent pages service that fails to start', () => Promise.resolve<TmRecentsModule>({ TmRecents: TmFailingRecents })],
  ])('reports %s as an error, and starts all the same', async (_, load) => {
    await start(loaders, [{ provide: TM_RECENTS_LOAD, useValue: load }]);
    TestBed.tick();
    const telemetry = TestBed.inject(TmFakeTelemetry);
    await vi.waitFor(() =>
      expect(telemetry.recent().filter((item) => item.name === 'tm.error.captured').map((item) => item.props['source'])).toEqual([
        'tm-recents',
      ]),
    );
  });

  it('loads the recent pages from their entry point by default', async () => {
    const module = await TestBed.inject(TM_RECENTS_LOAD)();
    // The name the startup reads is the one the entry point exports.
    expect(module.TmRecents).toBe((await import('@tellma/core/recents')).TmRecents);
  });

  it('loads the Contracts chunk after the first render, at idle', async () => {
    const contracts = vi.fn(loaders.contracts);
    await start({ ...loaders, contracts }, [{ provide: TmApi, useExisting: TmHttpApi }]);
    expect(contracts).not.toHaveBeenCalled();

    TestBed.tick();
    await vi.waitFor(() => expect(contracts).toHaveBeenCalledTimes(1));
  });
});
