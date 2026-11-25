// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Injectable } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { TmAppVersion, type TmDistributionInfo } from '@tellma/core';
import { TmAlertHarness, TmCopyButtonHarness } from '@tellma/core-ui-testing';

import { TM_TEST_SUB, tmAppRig, tmSettle, type TmAppRig, type TmAppRigOptions } from '../../../src/routes/tm-app.testing.util';
import { TM_ABOUT_ERROR_VIEWS, TmAboutDialog } from './tm-about-dialog';

// The About dialog in the test app: the wordmark, the distribution's own
// description only when its server text has one, the versions, the alert
// and Reload while the server runs a newer build and none while it runs an
// older one, Reload's failure and Reload anyway (which tries the error views
// again when they cannot load), and the legal lines.

/** The error views' update service, answering each update with a scripted result. */
@Injectable()
class TmFakeUpdates {
  /** What each update answers, in order; the last one repeats. */
  results: string[] = ['failed'];

  /** How many times the page was reloaded past the worker. */
  reloads = 0;

  /** Answers the next scripted result. */
  update(): Promise<string> {
    return Promise.resolve(this.results.length > 1 ? this.results.shift()! : this.results[0]);
  }

  /** Counts a reload. */
  reload(): void {
    this.reloads++;
  }
}

describe('the About dialog', () => {
  let rig: TmAppRig;
  let fixture: ComponentFixture<TmAboutDialog>;

  afterEach(() => TestBed.resetTestingModule());

  /** Starts the app and renders the dialog's content; a test's own providers come last, so they win. */
  async function open(options: TmAppRigOptions = {}): Promise<HTMLElement> {
    rig = await tmAppRig({
      ...options,
      providers: [
        TmFakeUpdates,
        { provide: TM_ABOUT_ERROR_VIEWS, useValue: () => Promise.resolve({ ɵTmErrorUpdates: TmFakeUpdates }) },
        ...(options.providers ?? []),
      ],
    });
    fixture = TestBed.createComponent(TmAboutDialog);
    await tmSettle();
    return fixture.nativeElement as HTMLElement;
  }

  /** The versions' terms and values, in order. */
  const versions = (element: HTMLElement): string[][] =>
    Array.from(element.querySelectorAll('dl > div')).map((row) => [
      row.querySelector('dt')?.textContent?.trim() ?? '',
      row.querySelector('dd span')?.textContent?.trim() ?? '',
    ]);

  it('shows the wordmark and the versions, with a copy button for the UI version, and no description without its server text', async () => {
    const element = await open();
    expect(element.querySelector('img')?.getAttribute('src')).toBe('brand/wordmark-on-light.svg');
    expect(element.querySelector('.tm-about-dialog__description')).toBeNull();
    const version = TestBed.inject(TmAppVersion);
    expect(versions(element)).toEqual([
      ['Acme', version.label],
      ['Server', '—'],
      ['Platform', version.platform],
      ['gl', '1.0.0'],
      ['acme', '1.0.0'],
    ]);
    expect(element.querySelector('.tm-about-dialog__mono')?.getAttribute('dir')).toBe('ltr');
    const copy = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmCopyButtonHarness);
    expect(await copy.getLabel()).toBe('Copy version');
    expect(element.querySelector('tm-alert')).toBeNull();
  });

  it("shows the distribution's own description when its server text has one", async () => {
    const element = await open({ strings: { en: { Distribution_About: 'Acme ERP, by Acme Ltd.' } } });
    expect(element.querySelector('.tm-about-dialog__description')?.textContent).toBe('Acme ERP, by Acme Ltd.');
  });

  it("shows the cached distribution's version and its legal links", async () => {
    rig = await tmAppRig();
    const distribution = { distributionVersion: '1.0.7', links: { terms: 'https://acme.example/terms', privacy: null } } as unknown as TmDistributionInfo;
    await rig.store.put({ kind: 'user', sub: TM_TEST_SUB }, 'distribution', { tag: null, savedAt: Date.now(), data: distribution });
    fixture = TestBed.createComponent(TmAboutDialog);
    await tmSettle();
    const element = fixture.nativeElement as HTMLElement;
    expect(versions(element)[1]).toEqual(['Server', '1.0.7']);
    const links = Array.from(element.querySelectorAll('a')).map((link) => [link.textContent, link.getAttribute('href'), link.getAttribute('target')]);
    expect(links).toEqual([['Terms of service', 'https://acme.example/terms', '_blank']]);
  });

  it('offers Reload while the server runs a newer build, says when the update fails, and offers Reload anyway from the second failure', async () => {
    const element = await open();
    TestBed.inject(TmAppVersion).ɵserver('1.1.0');
    await tmSettle();
    const alert = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmAlertHarness);
    expect(await alert.getKind()).toBe('info');
    expect(element.querySelector('.tm-about-dialog__alert-text')?.textContent).toBe('The server runs 1.1.0. Reload to update.');
    const reload = (): void => element.querySelector<HTMLButtonElement>('tm-alert button')!.click();
    reload();
    await tmSettle();
    expect(element.querySelector('.tm-about-dialog__alert-text')?.textContent).toBe("Couldn't download the update. Check your connection and try again.");
    expect(element.querySelectorAll('tm-alert button')).toHaveLength(1);
    reload();
    await tmSettle();
    const buttons = element.querySelectorAll<HTMLButtonElement>('tm-alert button');
    expect(Array.from(buttons).map((button) => button.textContent?.trim())).toEqual(['Reload', 'Reload anyway']);
    buttons[1].click();
    await tmSettle();
    expect(TestBed.inject(TmFakeUpdates).reloads).toBe(1);
  });

  it('tries the error views again on Reload anyway when they cannot load, and says it failed', async () => {
    let loads = 0;
    let offline = true;
    const element = await open({
      providers: [
        {
          provide: TM_ABOUT_ERROR_VIEWS,
          useValue: () => (loads++, offline ? Promise.reject(new TypeError('Failed to fetch')) : Promise.resolve({ ɵTmErrorUpdates: TmFakeUpdates })),
        },
      ],
    });
    TestBed.inject(TmAppVersion).ɵserver('1.1.0');
    await tmSettle();
    const buttons = (): HTMLButtonElement[] => Array.from(element.querySelectorAll<HTMLButtonElement>('tm-alert button'));
    buttons()[0].click();
    await tmSettle();
    buttons()[0].click();
    await tmSettle();
    expect(buttons().map((button) => button.textContent?.trim())).toEqual(['Reload', 'Reload anyway']);
    // Reload anyway asks for the views again; their failure is caught and shown.
    buttons()[1].click();
    await tmSettle();
    expect(loads).toBe(3);
    expect(element.querySelector('.tm-about-dialog__alert-text')?.textContent).toBe("Couldn't download the update. Check your connection and try again.");
    expect(buttons()[1].getAttribute('aria-label')).toBeNull();
    expect(TestBed.inject(TmFakeUpdates).reloads).toBe(0);
    // Once the views load, the next press reloads.
    offline = false;
    buttons()[1].click();
    await tmSettle();
    expect(loads).toBe(4);
    expect(TestBed.inject(TmFakeUpdates).reloads).toBe(1);
  });

  it('shows no alert while the server runs an older build', async () => {
    const element = await open();
    TestBed.inject(TmAppVersion).ɵserver('0.9.0');
    await tmSettle();
    expect(versions(element)[1]).toEqual(['Server', '0.9.0']);
    expect(element.querySelector('tm-alert')).toBeNull();
  });
});
