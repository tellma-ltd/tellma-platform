// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { tmTestMetadata } from '@tellma/core-testing';

import { tmFeature } from '../declarations/tm-feature';
import { tmEntity } from '../entity/tm-entity-declaration';
import { TM_CLIENT_METADATA, TmClientMetadataStore } from '../metadata/tm-client-metadata';
import type { TmMetadataLoaders } from '../metadata/tm-metadata-loaders';
import { provideTellma, tmAppConfigOf } from './provide-tellma';
import type { TmAppConfig } from './tm-app-config';
import { TM_APP_DECLARATIONS } from './tm-app-declarations';

/** Loaders a test never calls. */
const unusedLoaders: TmMetadataLoaders = {
  core: () => Promise.reject(new Error('not loaded in tests')),
  entities: () => Promise.reject(new Error('not loaded in tests')),
  contracts: () => Promise.reject(new Error('not loaded in tests')),
  strings: {},
};

/** An app configuration with one feature. */
const config: TmAppConfig = {
  metadata: unusedLoaders,
  languages: [{ code: 'en', dir: 'ltr', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) }],
  features: [tmFeature({ key: 'acme', entities: [tmEntity('acme.Customer', { order: 1 })] })],
};

describe('provideTellma', () => {
  it('provides the merged declarations, built from the loaded metadata', () => {
    TestBed.configureTestingModule({ providers: [provideTellma(config)] });
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
