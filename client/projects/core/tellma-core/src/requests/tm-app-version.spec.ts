// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { tmTestMetadata } from '@tellma/core-testing';

import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmAppVersion, tmCompareBuilds } from './tm-app-version';

// The UI version's identity (spec 0026 §7): its parts, the server build the
// request log reports, and the SemVer order of two builds.

/**
 * The version service over a Core chunk with the given UI version.
 *
 * @param version - The distribution's informational version.
 * @returns The service.
 */
function versionOf(version: string): TmAppVersion {
  const metadata = tmTestMetadata();
  TestBed.inject(TmClientMetadataStore).set({ ...metadata, distribution: { ...metadata.distribution, version, platformVersion: '1.2.0' } });
  return TestBed.inject(TmAppVersion);
}

describe('TmAppVersion', () => {
  const sha = '3f9c2ab1e5d40c9a7b2e6f1d8c3a5b7e9f0a1c2d';

  it('splits the UI version into its display, its commit and its label', () => {
    const version = versionOf(`1.4.0+${sha.toUpperCase()}`);
    expect(version.ui).toBe(`1.4.0+${sha.toUpperCase()}`);
    expect(version.display).toBe('1.4.0');
    expect(version.commit).toBe(sha);
    expect(version.label).toBe('1.4.0 (3f9c2ab)');
    expect(version.platform).toBe('1.2.0');
    expect(version.features).toEqual([
      { key: 'core', version: '1.0.0' },
      { key: 'gl', version: '1.0.0' },
      { key: 'acme', version: '1.0.0' },
    ]);
  });

  it('shows the version alone when it carries no commit', () => {
    const version = versionOf('1.4.0-rc.2+build.7');
    expect(version.display).toBe('1.4.0-rc.2');
    expect(version.commit).toBeNull();
    expect(version.label).toBe('1.4.0-rc.2');
  });

  it('is empty, and orders nothing, before the client metadata loads', () => {
    const version = TestBed.inject(TmAppVersion);
    expect([version.ui, version.display, version.label, version.platform]).toEqual(['', '', '', '']);
    expect(version.commit).toBeNull();
    expect(version.features).toEqual([]);
    expect(version.compare('1.5.0')).toBeNull();
  });

  it('orders a newer, an older, another commit and the identical build', () => {
    const version = versionOf('1.4.0+3f9c2ab');
    expect(version.compare('1.5.0')).toBe(1);
    expect(version.compare('1.4.0-rc.2')).toBe(-1);
    expect(version.compare('1.4.0+9e8d7c6')).toBeNull();
    expect(version.compare('1.4.0+3f9c2ab')).toBe(0);
  });

  it('flags a newer server build only, as the request log reports it', () => {
    const version = versionOf('1.4.0+3f9c2ab');
    expect(version.server()).toBeNull();
    expect(version.serverNewer()).toBe(false);
    version.ɵserver('1.4.0+9e8d7c6');
    expect(version.server()).toBe('1.4.0+9e8d7c6');
    expect(version.serverNewer()).toBe(false);
    version.ɵserver('1.4.1+0000000');
    expect(version.serverNewer()).toBe(true);
    version.ɵserver('1.3.9');
    expect(version.serverNewer()).toBe(false);
  });
});

describe('tmCompareBuilds', () => {
  const ui = '1.4.0+3f9c2ab';

  it('orders by SemVer precedence, ignoring the commit', () => {
    expect(tmCompareBuilds(ui, '1.4.1+0000000')).toBe(1);
    expect(tmCompareBuilds(ui, '1.10.0')).toBe(1);
    expect(tmCompareBuilds(ui, '1.3.9+3f9c2ab')).toBe(-1);
    expect(tmCompareBuilds(ui, '1.4.0-rc.2')).toBe(-1);
    expect(tmCompareBuilds('1.4.0-rc.2', '1.4.0-rc.10')).toBe(1);
    expect(tmCompareBuilds('1.4.0-rc.2', '1.4.0-rc')).toBe(-1);
    expect(tmCompareBuilds('1.4.0-2', '1.4.0-beta')).toBe(1);
    expect(tmCompareBuilds('1.4.0-beta', '1.4.0-alpha')).toBe(-1);
  });

  it('is 0 only for the identical build', () => {
    expect(tmCompareBuilds(ui, ui)).toBe(0);
  });

  it('is unordered for the same version with another commit, or for no version', () => {
    expect(tmCompareBuilds(ui, '1.4.0+9e8d7c6')).toBeNull();
    expect(tmCompareBuilds(ui, '1.4.0')).toBeNull();
    expect(tmCompareBuilds(ui, null)).toBeNull();
    expect(tmCompareBuilds(ui, 'nightly')).toBeNull();
    expect(tmCompareBuilds('dev', '1.4.0')).toBeNull();
  });
});
