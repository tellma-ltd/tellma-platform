// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { EventEmitter } from 'node:events';

import { afterEach, beforeEach, describe, expect, it, type Mock, vi } from 'vitest';
import type { ReportedHookContext, TestCase, TestModule, TestProject, Vitest } from 'vitest/node';

import { tmVitestConfig } from './index.js';
import { TmVitestWatchdog } from './watchdog.js';

// The coding-agent detection the preset reads, switchable per test.
const agent = vi.hoisted(() => ({ detected: false }));
vi.mock('std-env', () => ({
  get isAgent(): boolean {
    return agent.detected;
  },
}));

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

/** A fake Vitest instance and the controls of its close. */
interface FakeVitest {
  /** The instance. */
  readonly vitest: Vitest;
  /** Begins the close: calls the close handlers, as Vitest does once its global teardown is done. */
  beginClose(): void;
  /** Finishes the close: settles the promise `close()` returns. */
  finishClose(): void;
}

/** A fake Vitest instance, in watch mode or not, with the inspector on or off, over its projects. */
function fakeVitestWithClose(watch: boolean, inspector = false, projects: readonly TestProject[] = []): FakeVitest {
  const handlers: (() => unknown)[] = [];
  let finish: () => void = () => undefined;
  const closing = new Promise<void>((resolve) => {
    finish = resolve;
  });
  const vitest = {
    config: { watch, inspector: { enabled: inspector } },
    projects,
    onClose: (handler: () => unknown) => handlers.push(handler),
    close: () => closing,
  } as unknown as Vitest;
  return {
    vitest,
    beginClose: () => {
      for (const handler of handlers) {
        handler();
      }
    },
    finishClose: () => finish(),
  };
}

/** A fake Vitest instance whose close is never begun. */
function fakeVitest(watch: boolean, inspector = false, projects: readonly TestProject[] = []): Vitest {
  return fakeVitestWithClose(watch, inspector, projects).vitest;
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
    const done = fakeTest(testModule, 'app > starts');
    const test = fakeTest(testModule, 'app > loads');
    watchdog.onInit(fakeVitest(false));
    watchdog.onTestRunStart([{}]);
    watchdog.onTestModuleQueued(testModule);
    watchdog.onTestModuleStart(testModule);
    watchdog.onHookStart({ name: 'beforeAll', entity: testModule } as ReportedHookContext);
    watchdog.onHookEnd({ name: 'beforeAll', entity: testModule } as ReportedHookContext);
    watchdog.onTestCaseReady(done);
    watchdog.onTestCaseResult(done);
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
    expect(report).toContain('Tests not finished:\n  app > loads in src/app.spec.ts [app (chromium)]: running for 60.0 s\n\n');
    expect(report).not.toContain('app > starts in');
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

  it('list the hooks still running, of a test file and of a test', async () => {
    const testModule = fakeModule('src/app.spec.ts');
    const test = fakeTest(testModule, 'app > loads');
    watchdog.onInit(fakeVitest(false));
    watchdog.onTestModuleStart(testModule);
    watchdog.onHookStart({ name: 'beforeAll', entity: testModule } as ReportedHookContext);
    watchdog.onTestCaseReady(test);
    await vi.advanceTimersByTimeAsync(STALL_MS - 20_000);
    watchdog.onHookStart({ name: 'beforeEach', entity: test } as ReportedHookContext);
    await vi.advanceTimersByTimeAsync(STALL_MS);

    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
    expect(reports[0]).toContain(
      'Hooks not finished:\n' +
        '  beforeAll of src/app.spec.ts [app (chromium)]: running for 100.0 s\n' +
        '  beforeEach of app > loads in src/app.spec.ts [app (chromium)]: running for 60.0 s\n',
    );
  });

  it('leave a finished run alone until its exit limit, then end a process whose Vitest has not finished closing', async () => {
    const testModule = fakeModule('src/app.spec.ts');
    const { vitest, beginClose } = fakeVitestWithClose(false);
    watchdog.onInit(vitest);
    watchdog.onTestModuleQueued(testModule);
    watchdog.onTestModuleEnd(testModule);
    watchdog.onTestRunEnd([testModule], [], 'passed');
    beginClose();

    await vi.advanceTimersByTimeAsync(EXIT_MS - 1_000);
    expect(exit).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1_000);

    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
    expect(reports[0]).toContain('The test run ended 20.0 s ago, but Vitest has not finished closing');
    expect(reports[0]).toContain('run ended (passed)');
    expect(reports[0]).toContain('Test files not finished:\n  none');
  });

  it('disarm once Vitest has closed, so a process that goes on to build the next project is left alone', async () => {
    const { vitest, beginClose, finishClose } = fakeVitestWithClose(false);
    watchdog.onInit(vitest);
    watchdog.onTestRunStart([{}]);
    watchdog.onTestRunEnd([], [], 'passed');
    beginClose();
    finishClose();
    await vi.advanceTimersByTimeAsync(0);

    // No timer is left, and an idle process far past both limits is not ended.
    expect(vi.getTimerCount()).toBe(0);
    await vi.advanceTimersByTimeAsync(10 * STALL_MS);
    expect(exit).not.toHaveBeenCalled();
  });

  it('start over for a later Vitest instance in the same process, and still catch its stall', async () => {
    const first = fakeVitestWithClose(false);
    const testModule = fakeModule('src/core.spec.ts');
    watchdog.onInit(first.vitest);
    watchdog.onTestModuleQueued(testModule);
    watchdog.onTestRunEnd([], [], 'passed');
    first.beginClose();

    // The next instance starts before the first's close finishes: its exit limit no longer applies,
    // and the first's close finishing later does not disarm the second.
    const second = fakeVitestWithClose(false);
    watchdog.onInit(second.vitest);
    watchdog.onTestRunStart([{}]);
    watchdog.onTestModuleQueued(fakeModule('src/ui.spec.ts'));
    first.finishClose();
    await vi.advanceTimersByTimeAsync(EXIT_MS);
    expect(exit).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(STALL_MS - EXIT_MS);

    expect(exit).toHaveBeenCalledExactlyOnceWith(1);
    const report = reports[0]!;
    expect(report).toContain('No test file, test or hook started or finished for 60.0 s');
    expect(report).toContain('Test files not finished:\n  src/ui.spec.ts [app (chromium)]: queued for 60.0 s\n\n');
    expect(report).not.toContain('src/core.spec.ts');
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
    expect(report).toContain('Tests not finished:\n  none\n  (a frame did not answer: its page may be running a test it could not report)');
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

  it('name the reporters Vitest would pick itself before the watchdog', () => {
    agent.detected = false;
    vi.stubEnv('GITHUB_ACTIONS', '');
    expect(tmVitestConfig().test?.reporters).toEqual(['default', expect.any(TmVitestWatchdog)]);
    vi.stubEnv('GITHUB_ACTIONS', 'true');
    expect(tmVitestConfig().test?.reporters).toEqual(['default', 'github-actions', expect.any(TmVitestWatchdog)]);
    agent.detected = true;
    expect(tmVitestConfig().test?.reporters).toEqual(['agent', 'github-actions', expect.any(TmVitestWatchdog)]);
    vi.stubEnv('GITHUB_ACTIONS', '');
    expect(tmVitestConfig().test?.reporters).toEqual(['agent', expect.any(TmVitestWatchdog)]);
  });

  it('restore every spy, global stub and environment stub before each test, since the files of a page share its globals', () => {
    expect(tmVitestConfig().test).toMatchObject({ restoreMocks: true, unstubGlobals: true, unstubEnvs: true });
  });
});
