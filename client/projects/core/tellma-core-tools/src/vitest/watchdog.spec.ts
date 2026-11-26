// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { EventEmitter } from 'node:events';

import { afterEach, beforeEach, describe, expect, it, type Mock, vi } from 'vitest';
import type { ReportedHookContext, TestCase, TestModule, TestProject, Vitest } from 'vitest/node';

import { tmVitestConfig } from './index.js';
import { TmVitestWatchdog } from './watchdog.js';

// The watchdog is driven here through its reporter hooks with fake timers,
// and fake Vitest objects carrying only the fields it reads.

/** The stall limit the tests use. */
const STALL_MS = 60_000;

/** The exit limit the tests use. */
const EXIT_MS = 20_000;

/** A fake test file of the project `app (chromium)`. */
function fakeModule(path: string): TestModule {
  return { id: `module:${path}`, type: 'module', relativeModuleId: path, project: { name: 'app (chromium)' } } as unknown as TestModule;
}

/** A fake test of a test file. */
function fakeTest(testModule: TestModule, fullName: string): TestCase {
  return { id: `test:${fullName}`, type: 'test', fullName, module: testModule } as unknown as TestCase;
}

/** A fake Vitest instance, in watch mode or not, with the inspector on or off, over its projects. */
function fakeVitest(watch: boolean, inspector = false, projects: readonly TestProject[] = []): Vitest {
  return { config: { watch, inspector: { enabled: inspector } }, projects } as unknown as Vitest;
}

/** A fake request a page made. */
function fakeRequest(url: string, errorText?: string): object {
  return { url: () => url, method: () => 'GET', failure: () => (errorText === undefined ? null : { errorText }) };
}

/** A fake frame, which answers with a ready state or never answers. */
function fakeFrame(name: string, url: string, readyState: string | undefined): object {
  return {
    name: () => name,
    url: () => url,
    evaluate: () => (readyState === undefined ? new Promise(() => undefined) : Promise.resolve(readyState)),
  };
}

/** A fake Playwright page: an event emitter with a URL and frames. */
class FakePage extends EventEmitter {
  /**
   * Creates the page.
   *
   * @param pageUrl - Its URL.
   * @param pageFrames - Its frames, the main frame first.
   */
  constructor(
    private readonly pageUrl: string,
    private readonly pageFrames: readonly object[],
  ) {
    super();
  }

  /** The page's URL. */
  url(): string {
    return this.pageUrl;
  }

  /** Whether the page is closed: never. */
  isClosed(): boolean {
    return false;
  }

  /** The page's frames. */
  frames(): readonly object[] {
    return this.pageFrames;
  }
}

/** A fake browser project over a provider, with no session connected. */
function fakeBrowserProject(provider: object, vite: object = {}): TestProject {
  return {
    name: 'app (chromium)',
    browser: { provider, vite, state: { orchestrators: new Map() } },
  } as unknown as TestProject;
}

describe('the Vitest watchdog', () => {
  let reports: string[];
  let exit: Mock<(code: number) => void>;
  let watchdog: TmVitestWatchdog;

  beforeEach(() => {
    vi.useFakeTimers({ now: 0, toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] });
    reports = [];
    exit = vi.fn<(code: number) => void>();
    watchdog = new TmVitestWatchdog({
      stallMs: STALL_MS,
      exitMs: EXIT_MS,
      write: (text) => {
        reports.push(text);
        return Promise.resolve();
      },
      exit,
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('end a run with no progress for its limit, listing the file, test and hook still running', async () => {
    const testModule = fakeModule('src/app.spec.ts');
    const test = fakeTest(testModule, 'app > loads');
    watchdog.onInit(fakeVitest(false));
    watchdog.onTestRunStart([{}]);
    watchdog.onTestModuleQueued(testModule);
    watchdog.onTestModuleStart(testModule);
    watchdog.onHookStart({ name: 'beforeAll', entity: testModule } as ReportedHookContext);
    watchdog.onHookEnd({ name: 'beforeAll', entity: testModule } as ReportedHookContext);
    watchdog.onTestCaseReady(test);

    await vi.advanceTimersByTimeAsync(STALL_MS - 1_000);
    expect(exit).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1_000);

    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
    expect(reports).toHaveLength(1);
    const report = reports[0]!;
    expect(report).toContain('No test file, test or hook started or finished for 60.0 s');
    expect(report).toContain('+0.0 s  test file started src/app.spec.ts [app (chromium)]');
    expect(report).toContain('Test files not finished:\n  src/app.spec.ts [app (chromium)]: running for 60.0 s');
    expect(report).toContain('Tests not finished:\n  app > loads in src/app.spec.ts [app (chromium)]: running for 60.0 s');
    expect(report).toContain('Hooks not finished:\n  none');
    expect(report).toContain('Node resources keeping the process alive:');
  });

  it('move its deadline on every progress event, and never fire twice', async () => {
    const testModule = fakeModule('src/app.spec.ts');
    watchdog.onInit(fakeVitest(false));
    await vi.advanceTimersByTimeAsync(STALL_MS - 2_000);
    watchdog.onTestModuleQueued(testModule);
    await vi.advanceTimersByTimeAsync(STALL_MS - 2_000);
    watchdog.onTestModuleCollected(testModule);
    await vi.advanceTimersByTimeAsync(STALL_MS - 2_000);
    expect(exit).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(10 * STALL_MS);
    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
    expect(reports[0]).toContain('src/app.spec.ts [app (chromium)]: collected for 60.0 s');
  });

  it('leave a finished run alone until its exit limit, then end a process that did not exit', async () => {
    const testModule = fakeModule('src/app.spec.ts');
    watchdog.onInit(fakeVitest(false));
    watchdog.onTestModuleQueued(testModule);
    watchdog.onTestModuleEnd(testModule);
    watchdog.onTestRunEnd([testModule], [], 'passed');

    await vi.advanceTimersByTimeAsync(EXIT_MS - 1_000);
    expect(exit).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1_000);

    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
    expect(reports[0]).toContain('The test run ended 20.0 s ago, but the process has not exited');
    expect(reports[0]).toContain('run ended (passed)');
    expect(reports[0]).toContain('Test files not finished:\n  none');
  });

  it('wait for changes without a limit in watch mode, and arm again for a rerun', async () => {
    watchdog.onInit(fakeVitest(true));
    watchdog.onTestRunEnd([], [], 'passed');
    await vi.advanceTimersByTimeAsync(10 * STALL_MS);
    expect(exit).not.toHaveBeenCalled();

    watchdog.onWatcherRerun(['src/app.spec.ts']);
    await vi.advanceTimersByTimeAsync(STALL_MS);
    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
    expect(reports[0]).toContain('rerun for 1 changed file(s)');
  });

  it("describe a Playwright session: the launch, each frame's answer, open and failed requests and the optimizer", async () => {
    const page = new FakePage('http://localhost:63315/__vitest_test__/?sessionId=s1', [
      fakeFrame('', 'http://localhost:63315/__vitest_test__/?sessionId=s1', 'complete'),
      fakeFrame('vitest-iframe', 'http://localhost:63315/__vitest_test__/tester', undefined),
    ]);
    const provider = { name: 'playwright', pages: new Map([['s1', page]]), browser: null, browserPromise: new Promise(() => undefined) };
    const vite = {
      environments: {
        client: {
          depsOptimizer: {
            scanProcessing: new Promise(() => undefined),
            metadata: {
              optimized: { rxjs: {} },
              discovered: { lucide: { processing: new Promise(() => undefined) }, tslib: { processing: Promise.resolve() } },
            },
          },
        },
      },
    };
    watchdog.onInit(fakeVitest(false));
    watchdog.onBrowserInit(fakeBrowserProject(provider, vite));
    await vi.advanceTimersByTimeAsync(1_000);
    const open = fakeRequest('http://localhost:63315/@fs/app/spec-app.js');
    const finished = fakeRequest('http://localhost:63315/@fs/app/chunk-A.js');
    page.emit('request', open);
    page.emit('request', finished);
    page.emit('requestfinished', finished);
    page.emit('requestfailed', fakeRequest('http://localhost:63315/@fs/app/chunk-B.js', 'net::ERR_ABORTED'));
    page.emit('pageerror', new Error('boom'));

    // The stall, then the five seconds the report gives the frame that never answers.
    await vi.advanceTimersByTimeAsync(STALL_MS);
    await vi.advanceTimersByTimeAsync(5_000);

    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
    const report = reports[0]!;
    expect(report).toContain('Browser [app (chromium)]: provider playwright, 0 session(s) connected to the server');
    expect(report).toContain('  browser: still launching');
    expect(report).toContain('  session s1: page open at http://localhost:63315/__vitest_test__/?sessionId=s1');
    expect(report).toContain('    frame (main) at http://localhost:63315/__vitest_test__/?sessionId=s1: document complete');
    expect(report).toContain('    frame "vitest-iframe" at http://localhost:63315/__vitest_test__/tester: did not answer within 5.0 s');
    expect(report).toContain('    open requests: 1\n      GET http://localhost:63315/@fs/app/spec-app.js: open for 59.0 s');
    expect(report).not.toContain('chunk-A.js');
    expect(report).toContain('    request failed: GET http://localhost:63315/@fs/app/chunk-B.js (net::ERR_ABORTED)');
    expect(report).toContain('    page error: boom');
    expect(report).toContain('  dependency optimizer: 1 optimized, 2 discovered, scan still running\n    still bundling: lucide');
  });

  it('name a launched browser, and a provider that is not Playwright by its name alone', async () => {
    watchdog.onInit(fakeVitest(false));
    watchdog.onBrowserInit(fakeBrowserProject({ name: 'playwright', pages: new Map(), browser: { isConnected: () => true } }));
    await vi.advanceTimersByTimeAsync(STALL_MS);
    expect(reports[0]).toContain('  browser: launched and connected\n  no page open\n  dependency optimizer: none');

    const other = new TmVitestWatchdog({ stallMs: STALL_MS, write: (text) => Promise.resolve(void reports.push(text)), exit });
    other.onInit(fakeVitest(false));
    other.onBrowserInit(fakeBrowserProject({ name: 'webdriverio' }));
    await vi.advanceTimersByTimeAsync(STALL_MS);
    expect(reports[1]).toContain('Browser [app (chromium)]: provider webdriverio, 0 session(s) connected to the server\n  dependency optimizer: none');
  });

  it('name a browser project whose server never became ready', async () => {
    const starting = { name: 'app (chromium)', config: { browser: { enabled: true } } } as unknown as TestProject;
    const node = { name: 'node', config: { browser: { enabled: false } } } as unknown as TestProject;
    watchdog.onInit(fakeVitest(false, false, [starting, node]));
    watchdog.onTestRunStart([{}]);
    await vi.advanceTimersByTimeAsync(STALL_MS);
    expect(reports[0]).toContain('Browser [app (chromium)]: its server has not become ready');
    expect(reports[0]).not.toContain('Browser [node]');
  });

  it('stay off while the inspector is on, since a debugger paused at a breakpoint makes no progress either', async () => {
    watchdog.onInit(fakeVitest(false, true));
    watchdog.onTestModuleQueued(fakeModule('src/app.spec.ts'));
    await vi.advanceTimersByTimeAsync(10 * STALL_MS);
    expect(exit).not.toHaveBeenCalled();
  });

  it('end the run even when its report cannot be written', async () => {
    const failing = new TmVitestWatchdog({ stallMs: STALL_MS, write: () => Promise.reject(new Error('closed')), exit });
    failing.onInit(fakeVitest(false));
    await vi.advanceTimersByTimeAsync(STALL_MS);
    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
  });
});

describe('the Vitest preset', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("name Vitest's default reporters before the watchdog", () => {
    vi.stubEnv('GITHUB_ACTIONS', '');
    expect(tmVitestConfig().test?.reporters).toEqual(['default', expect.any(TmVitestWatchdog)]);
    vi.stubEnv('GITHUB_ACTIONS', 'true');
    expect(tmVitestConfig().test?.reporters).toEqual(['default', 'github-actions', expect.any(TmVitestWatchdog)]);
  });
});
