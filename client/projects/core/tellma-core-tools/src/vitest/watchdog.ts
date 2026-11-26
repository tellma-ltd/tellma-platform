// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ReportedHookContext, Reporter, TestCase, TestModule, TestProject, TestRunEndReason, Vitest } from 'vitest/node';

// Vitest's own limits leave silent phases in a browser-mode run. A test and
// a hook time out inside the page, and opening a session times out after the
// connect limit, but nothing bounds the browser server's start, the import of
// a test file into the tester frame, the clean-up between files, or the close
// after the run. A run stuck in one of them prints nothing until the CI job
// is cancelled, and a cancelled step leaves no trace of what it was waiting
// for. This reporter watches the run's progress instead: when no test file,
// test or hook starts or finishes for too long, it writes what was still
// pending (the files, tests and hooks; each browser session's page, frames
// and open requests; the dependency optimizer; the Node handles) and ends
// the process with exit code 1.

/** How long a run may go without progress before the watchdog ends it: 5 minutes. */
export const TM_VITEST_STALL_MS = 300_000;

/** How long a process may live after its run ended, outside watch mode, before the watchdog ends it: 2 minutes. */
export const TM_VITEST_EXIT_MS = 120_000;

/** How long the report waits for a browser frame to answer before it calls the frame unresponsive. */
const FRAME_ANSWER_MS = 5_000;

/** How often the watchdog checks its deadline and looks for new browser pages. */
const POLL_MS = 500;

/** How many of the latest progress events the report lists. */
const EVENTS_KEPT = 25;

/** How many of a page's latest errors the report lists. */
const PAGE_ERRORS_KEPT = 5;

/** How the watchdog judges and reports a stalled run. */
export interface TmVitestWatchdogOptions {
  /** How long the run may go without progress, in milliseconds, before the watchdog ends it. Defaults to {@link TM_VITEST_STALL_MS}. */
  readonly stallMs?: number;
  /**
   * How long the process may live after its run ended, in milliseconds, before the watchdog ends
   * it. Watch mode keeps the process on purpose, so it is not limited there. Defaults to
   * {@link TM_VITEST_EXIT_MS}.
   */
  readonly exitMs?: number;
  /** Writes the report, and resolves once it is written. Defaults to standard error, resolved once the stream has flushed it. */
  readonly write?: (text: string) => Promise<void>;
  /** Ends the process with an exit code. Defaults to `process.exit`. */
  readonly exit?: (code: number) => void;
}

/** One progress event: when it happened and what it was. */
interface WatchdogEvent {
  /** When it happened, in milliseconds since the epoch. */
  readonly at: number;
  /** What happened, such as `test file started src/app.spec.ts [app (chromium)]`. */
  readonly text: string;
}

/** Something that started and has not finished: a test file, a test or a hook. */
interface PendingEntry {
  /** What it is, as the report names it. */
  label: string;
  /** Where it stands, such as `queued` or `running`. */
  state: string;
  /** When it reached that state, in milliseconds since the epoch. */
  since: number;
}

// The shapes the report reads from the Playwright provider and its pages. The
// provider's class is not part of Vitest's typed API, so each is checked before
// use, and a shape that changes in a later release only drops the details it
// carried from the report.

/** A request a page made, as Playwright reports it. */
interface PageRequestLike {
  /** The request's URL. */
  url(): string;
  /** The request's method. */
  method(): string;
  /** Why the request failed, once it has. */
  failure(): { readonly errorText: string } | null;
}

/** A frame of a page, as Playwright exposes it. */
interface FrameLike {
  /** The frame's name. */
  name(): string;
  /** The frame's URL. */
  url(): string;
  /** Evaluates an expression in the frame. */
  evaluate(expression: string): Promise<unknown>;
}

/** A browser page, as Playwright exposes it. */
interface PageLike {
  /** Adds an event listener. */
  on(event: string, listener: (value: unknown) => void): unknown;
  /** The page's URL. */
  url(): string;
  /** Whether the page is closed. */
  isClosed(): boolean;
  /** The page's frames, the main frame first. */
  frames(): readonly FrameLike[];
}

/** The fields of Vitest's Playwright provider the report reads. */
interface PlaywrightProviderLike {
  /** The provider's name. */
  readonly name: string;
  /** The open page of each browser session, by session id. */
  readonly pages: ReadonlyMap<string, PageLike>;
  /** The launched browser, once it is launched. */
  readonly browser: { isConnected(): boolean } | null;
  /** The browser's launch, while it runs. */
  readonly browserPromise?: Promise<unknown> | null;
}

/** What the watchdog learned about one browser page from its events. */
interface PageWatch {
  /** The requests the page made that have not finished, with when each started. */
  readonly open: Map<PageRequestLike, number>;
  /** The page's latest errors and failed requests. */
  readonly errors: string[];
  /** Whether the page crashed. */
  crashed: boolean;
}

/** The marker {@link isPending} races a promise against. */
const PENDING: unique symbol = Symbol('pending');

/** The marker {@link within} resolves with when the limit passes first. */
const TIMED_OUT: unique symbol = Symbol('timed out');

/**
 * Whether a promise is still pending. A settled promise's reaction is queued before the marker's,
 * so it wins the race; a pending one never does.
 */
async function isPending(promise: Promise<unknown>): Promise<boolean> {
  try {
    return (await Promise.race([promise, PENDING])) === PENDING;
  } catch {
    return false;
  }
}

/** A promise's value, or {@link TIMED_OUT} when it takes longer than `ms`. */
async function within<T>(promise: Promise<T>, ms: number): Promise<T | typeof TIMED_OUT> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const limit = new Promise<typeof TIMED_OUT>((resolve) => {
    timer = setTimeout(() => resolve(TIMED_OUT), ms);
  });
  try {
    return await Promise.race([promise, limit]);
  } finally {
    clearTimeout(timer);
  }
}

/** A value as a record, when it is an object. */
function asRecord(value: unknown): Readonly<Record<string, unknown>> | undefined {
  return typeof value === 'object' && value !== null ? (value as Readonly<Record<string, unknown>>) : undefined;
}

/** Whether a browser provider is Vitest's Playwright provider, by the fields the report reads. */
function isPlaywrightProvider(provider: unknown): provider is PlaywrightProviderLike {
  const record = asRecord(provider);
  return record !== undefined && record['pages'] instanceof Map && 'browser' in record;
}

/** An error's message, or the value as text. */
function describeError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** A duration in seconds, to one decimal. */
function seconds(ms: number): string {
  return `${(ms / 1000).toFixed(1)} s`;
}

/** The label of a test file: its path from the root, and its project. */
function moduleLabel(testModule: TestModule): string {
  return `${testModule.relativeModuleId} [${testModule.project.name}]`;
}

/** The label of a hook: its kind, and what it runs for. */
function hookLabel(hook: ReportedHookContext): string {
  const entity = hook.entity;
  const owner = entity.type === 'module' ? moduleLabel(entity) : `${entity.fullName} in ${moduleLabel(entity.module)}`;
  return `${hook.name} of ${owner}`;
}

/** The Node resources keeping the event loop alive, counted by kind. */
function activeResources(): string {
  const counts = new Map<string, number>();
  for (const kind of process.getActiveResourcesInfo()) {
    counts.set(kind, (counts.get(kind) ?? 0) + 1);
  }
  return [...counts].map(([kind, count]) => `${kind} x${count}`).join(', ') || 'none';
}

/** Writes to standard error, and resolves once the stream has taken the text, which a pipe on Windows does later. */
function writeToStandardError(text: string): Promise<void> {
  return new Promise((resolve) => {
    process.stderr.write(text, () => resolve());
  });
}

/**
 * A Vitest reporter that ends a run which makes no progress, after writing what was pending.
 *
 * Progress is a test file, a test or a hook starting or finishing, or the browser server becoming
 * ready. Vitest's own limits fail a test or a hook that runs too long; this one covers the phases
 * they do not: the browser's start, the import of a test file, the clean-up between files and a
 * process that does not exit after its run. The report lists the latest events, what started and
 * has not finished, each browser session's page with its frames and open requests, the dependency
 * optimizer's pending work and the handles keeping Node alive. The process then ends with exit
 * code 1, so the run fails with evidence rather than waiting for its CI job to be cancelled. It
 * stays off while the inspector is on, since a debugger paused at a breakpoint makes no progress
 * either.
 *
 * @example
 * ```ts
 * export default { test: { reporters: ['default', new TmVitestWatchdog({ stallMs: 120_000 })] } };
 * ```
 */
export class TmVitestWatchdog implements Reporter {
  /** How long the run may go without progress. */
  private readonly stallMs: number;
  /** How long the process may live after its run ended. */
  private readonly exitMs: number;
  /** Writes the report. */
  private readonly write: (text: string) => Promise<void>;
  /** Ends the process. */
  private readonly exit: (code: number) => void;
  /** The latest progress events, oldest first. */
  private readonly events: WatchdogEvent[] = [];
  /** The test files that were queued and have not finished, by id. */
  private readonly modules = new Map<string, PendingEntry>();
  /** The tests that started and have not finished, by id. */
  private readonly tests = new Map<string, PendingEntry>();
  /** The hooks that started and have not finished, by kind and owner. */
  private readonly hooks = new Map<string, PendingEntry>();
  /** The projects whose browser server is ready. */
  private readonly browserProjects = new Set<TestProject>();
  /** What each browser page's events told the watchdog. */
  private readonly pages = new Map<PageLike, PageWatch>();
  /** The Vitest instance, once the run initialises. */
  private vitest: Vitest | undefined;
  /** When the run initialised, in milliseconds since the epoch. */
  private startedAt = 0;
  /** When the run last made progress, or ended, in milliseconds since the epoch. */
  private lastProgressAt = 0;
  /** When the watchdog fires unless progress moves it, or undefined while it is disarmed. */
  private deadline: number | undefined;
  /** Whether the run has ended, so the deadline is the exit limit rather than the stall limit. */
  private ended = false;
  /** The timer that checks the deadline and looks for new pages. */
  private poller: ReturnType<typeof setInterval> | undefined;
  /** Whether the watchdog has fired; it fires once. */
  private fired = false;
  /** Whether the watchdog stays off, as it does while a debugger may pause the run. */
  private off = false;

  /**
   * Creates the watchdog.
   *
   * @param options - Its limits, and where its report goes.
   */
  constructor(options: TmVitestWatchdogOptions = {}) {
    this.stallMs = options.stallMs ?? TM_VITEST_STALL_MS;
    this.exitMs = options.exitMs ?? TM_VITEST_EXIT_MS;
    this.write = options.write ?? writeToStandardError;
    this.exit = options.exit ?? ((code) => process.exit(code));
  }

  /**
   * Arms the watchdog as the run initialises, unless the inspector is on: a debugger paused at a
   * breakpoint makes no progress either.
   *
   * @param vitest - The Vitest instance.
   */
  onInit(vitest: Vitest): void {
    this.vitest = vitest;
    this.startedAt = Date.now();
    if (vitest.config.inspector.enabled === true) {
      this.off = true;
      return;
    }
    this.progress('initialised');
    // One unreferenced timer, so the watchdog never keeps a finished process alive.
    this.poller ??= setInterval(() => this.poll(), POLL_MS);
    this.poller.unref();
  }

  /**
   * Notes a project's browser server as ready, so the report can describe its sessions.
   *
   * @param project - The project.
   */
  onBrowserInit(project: TestProject): void {
    this.browserProjects.add(project);
    this.progress(`browser server ready [${project.name}]`);
  }

  /**
   * Notes the start of a run.
   *
   * @param specifications - The test files it runs.
   */
  onTestRunStart(specifications: readonly unknown[]): void {
    this.ended = false;
    this.progress(`run started with ${specifications.length} test file(s)`);
  }

  /**
   * Notes a test file queued for a worker or a browser session.
   *
   * @param testModule - The test file.
   */
  onTestModuleQueued(testModule: TestModule): void {
    this.track(this.modules, testModule.id, moduleLabel(testModule), 'queued');
    this.progress(`test file queued ${moduleLabel(testModule)}`);
  }

  /**
   * Notes a test file whose tests are collected.
   *
   * @param testModule - The test file.
   */
  onTestModuleCollected(testModule: TestModule): void {
    this.track(this.modules, testModule.id, moduleLabel(testModule), 'collected');
    this.progress(`test file collected ${moduleLabel(testModule)}`);
  }

  /**
   * Notes a test file whose tests start running.
   *
   * @param testModule - The test file.
   */
  onTestModuleStart(testModule: TestModule): void {
    this.track(this.modules, testModule.id, moduleLabel(testModule), 'running');
    this.progress(`test file started ${moduleLabel(testModule)}`);
  }

  /**
   * Notes a finished test file.
   *
   * @param testModule - The test file.
   */
  onTestModuleEnd(testModule: TestModule): void {
    this.modules.delete(testModule.id);
    this.progress(`test file finished ${moduleLabel(testModule)}`);
  }

  /**
   * Notes a test that starts.
   *
   * @param testCase - The test.
   */
  onTestCaseReady(testCase: TestCase): void {
    this.track(this.tests, testCase.id, `${testCase.fullName} in ${moduleLabel(testCase.module)}`, 'running');
    this.progress(`test started ${testCase.fullName}`);
  }

  /**
   * Notes a finished test.
   *
   * @param testCase - The test.
   */
  onTestCaseResult(testCase: TestCase): void {
    this.tests.delete(testCase.id);
    this.progress(`test finished ${testCase.fullName}`);
  }

  /**
   * Notes a hook that starts.
   *
   * @param hook - The hook and what it runs for.
   */
  onHookStart(hook: ReportedHookContext): void {
    this.track(this.hooks, `${hook.name}:${hook.entity.id}`, hookLabel(hook), 'running');
    this.progress(`hook started ${hookLabel(hook)}`);
  }

  /**
   * Notes a finished hook.
   *
   * @param hook - The hook and what it ran for.
   */
  onHookEnd(hook: ReportedHookContext): void {
    this.hooks.delete(`${hook.name}:${hook.entity.id}`);
    this.progress(`hook finished ${hookLabel(hook)}`);
  }

  /**
   * Moves the deadline to the exit limit once the run ends, or disarms it in watch mode, which
   * keeps the process on purpose; the next run arms it again.
   *
   * @param _testModules - The run's test files.
   * @param _unhandledErrors - The run's errors outside tests.
   * @param reason - How the run ended.
   */
  onTestRunEnd(_testModules: readonly TestModule[], _unhandledErrors: readonly unknown[], reason: TestRunEndReason): void {
    this.record(`run ended (${reason})`);
    this.ended = true;
    this.lastProgressAt = Date.now();
    this.deadline = this.vitest?.config.watch === true ? undefined : Date.now() + this.exitMs;
  }

  /**
   * Arms the watchdog again for a rerun in watch mode.
   *
   * @param files - The files that triggered it.
   */
  onWatcherRerun(files: readonly string[]): void {
    this.ended = false;
    this.progress(`rerun for ${files.length} changed file(s)`);
  }

  /** Records an event and moves the stall deadline, unless the run has ended. */
  private progress(text: string): void {
    this.record(text);
    if (!this.ended) {
      this.lastProgressAt = Date.now();
      this.deadline = this.lastProgressAt + this.stallMs;
    }
  }

  /** Records an event, keeping only the latest ones. */
  private record(text: string): void {
    this.events.push({ at: Date.now(), text });
    if (this.events.length > EVENTS_KEPT) {
      this.events.shift();
    }
  }

  /** Records what an entry is and where it stands. */
  private track(entries: Map<string, PendingEntry>, id: string, label: string, state: string): void {
    entries.set(id, { label, state, since: Date.now() });
  }

  /** The poller's tick: fires past the deadline, else starts watching any new browser page. */
  private poll(): void {
    if (this.fired || this.off) {
      return;
    }
    if (this.deadline !== undefined && Date.now() >= this.deadline) {
      void this.fire();
      return;
    }
    for (const project of this.browserProjects) {
      try {
        const provider = project.browser?.provider;
        if (isPlaywrightProvider(provider)) {
          for (const page of provider.pages.values()) {
            this.watchPage(page);
          }
        }
      } catch (error) {
        // A provider whose shape changed loses its page details, never the run.
        this.record(`could not read the browser pages of [${project.name}]: ${describeError(error)}`);
        this.browserProjects.delete(project);
      }
    }
  }

  /** Starts following a page's requests, errors and crash, once per page. */
  private watchPage(page: PageLike): void {
    if (this.pages.has(page)) {
      return;
    }
    const watch: PageWatch = { open: new Map(), errors: [], crashed: false };
    this.pages.set(page, watch);
    const note = (text: string): void => {
      watch.errors.push(text);
      if (watch.errors.length > PAGE_ERRORS_KEPT) {
        watch.errors.shift();
      }
    };
    page.on('request', (request) => watch.open.set(request as PageRequestLike, Date.now()));
    page.on('requestfinished', (request) => watch.open.delete(request as PageRequestLike));
    page.on('requestfailed', (value) => {
      const request = value as PageRequestLike;
      watch.open.delete(request);
      note(`request failed: ${request.method()} ${request.url()} (${request.failure()?.errorText ?? 'no reason given'})`);
    });
    page.on('pageerror', (error) => note(`page error: ${describeError(error)}`));
    page.on('crash', () => {
      watch.crashed = true;
    });
  }

  /** Writes the report and ends the process with exit code 1. */
  private async fire(): Promise<void> {
    this.fired = true;
    clearInterval(this.poller);
    let report: string;
    try {
      report = await this.report();
    } catch (error) {
      report = `${this.headline()}\nThe report could not be completed: ${describeError(error)}\n`;
    }
    // A report that cannot be written changes nothing: the run still ends.
    await this.write(report).catch(() => undefined);
    this.exit(1);
  }

  /** The report's first line: what the watchdog saw, and what it does. */
  private headline(): string {
    const quiet = seconds(Date.now() - this.lastProgressAt);
    return this.ended
      ? `[vitest watchdog] The test run ended ${quiet} ago, but the process has not exited. Ending it with exit code 1; what was still open follows.`
      : `[vitest watchdog] No test file, test or hook started or finished for ${quiet}. Ending the run with exit code 1; what was pending follows.`;
  }

  /** The whole report. */
  private async report(): Promise<string> {
    const now = Date.now();
    const lines: string[] = [this.headline(), '', 'Latest events, oldest first:'];
    for (const event of this.events) {
      lines.push(`  +${seconds(event.at - this.startedAt)}  ${event.text}`);
    }
    // What started and has not finished.
    const sections: [string, Map<string, PendingEntry>][] = [
      ['Test files not finished', this.modules],
      ['Tests not finished', this.tests],
      ['Hooks not finished', this.hooks],
    ];
    for (const [title, entries] of sections) {
      lines.push('', `${title}:`);
      if (entries.size === 0) {
        lines.push('  none');
      }
      for (const entry of entries.values()) {
        lines.push(`  ${entry.label}: ${entry.state} for ${seconds(now - entry.since)}`);
      }
    }
    // Each browser project's sessions, pages and dependency optimizer; a browser project whose
    // server never became ready is still starting, its optimizer's first run included.
    for (const project of this.browserProjects) {
      lines.push('', ...(await this.describeBrowser(project, now)));
    }
    for (const project of this.vitest?.projects ?? []) {
      if (project.config.browser.enabled && !this.browserProjects.has(project)) {
        lines.push('', `Browser [${project.name}]: its server has not become ready; it is still starting, the dependency optimizer's first run included`);
      }
    }
    lines.push('', `Node resources keeping the process alive: ${activeResources()}`, '');
    return lines.join('\n');
  }

  /** The report's lines on one project's browser: the provider, its sessions and the dependency optimizer. */
  private async describeBrowser(project: TestProject, now: number): Promise<string[]> {
    const browser = project.browser;
    if (browser === undefined) {
      return [`Browser [${project.name}]: not started`];
    }
    const lines = [
      `Browser [${project.name}]: provider ${browser.provider.name}, ${browser.state.orchestrators.size} session(s) connected to the server`,
    ];
    try {
      const provider: unknown = browser.provider;
      if (isPlaywrightProvider(provider)) {
        lines.push(`  browser: ${await this.describeLaunch(provider)}`);
        if (provider.pages.size === 0) {
          lines.push('  no page open');
        }
        for (const [sessionId, page] of provider.pages) {
          lines.push(...(await this.describePage(sessionId, page, now)));
        }
      }
    } catch (error) {
      lines.push(`  the provider's pages could not be read: ${describeError(error)}`);
    }
    lines.push(...(await this.describeOptimizer(browser.vite)));
    return lines;
  }

  /** Whether the Playwright provider's browser is launching, launched or gone. */
  private async describeLaunch(provider: PlaywrightProviderLike): Promise<string> {
    if (provider.browser !== null) {
      return provider.browser.isConnected() ? 'launched and connected' : 'launched, connection lost';
    }
    const launch = provider.browserPromise;
    return launch !== null && launch !== undefined && (await isPending(launch)) ? 'still launching' : 'not launched';
  }

  /** The report's lines on one session's page: its state, frames, open requests and errors. */
  private async describePage(sessionId: string, page: PageLike, now: number): Promise<string[]> {
    const watch = this.pages.get(page);
    const lines = [`  session ${sessionId}: page ${page.isClosed() ? 'closed' : 'open'}${watch?.crashed === true ? ', crashed' : ''} at ${page.url()}`];
    if (!page.isClosed()) {
      for (const frame of page.frames()) {
        // A frame whose main thread is blocked never answers; one waiting on the server does.
        const state = await within(
          frame.evaluate('document.readyState').then(
            (value) => `document ${String(value)}`,
            (error: unknown) => `could not evaluate: ${describeError(error)}`,
          ),
          FRAME_ANSWER_MS,
        );
        const answer = state === TIMED_OUT ? `did not answer within ${seconds(FRAME_ANSWER_MS)}` : state;
        lines.push(`    frame ${frame.name() === '' ? '(main)' : `"${frame.name()}"`} at ${frame.url()}: ${answer}`);
      }
    }
    if (watch === undefined) {
      lines.push('    requests: not followed (the page opened after the last check)');
      return lines;
    }
    lines.push(`    open requests: ${watch.open.size === 0 ? 'none' : watch.open.size}`);
    for (const [request, since] of watch.open) {
      lines.push(`      ${request.method()} ${request.url()}: open for ${seconds(now - since)}`);
    }
    for (const error of watch.errors) {
      lines.push(`    ${error}`);
    }
    return lines;
  }

  /** The report's lines on the browser server's dependency optimizer: its scan and the dependencies it is still bundling. */
  private async describeOptimizer(vite: unknown): Promise<string[]> {
    const environments = asRecord(asRecord(vite)?.['environments']);
    const optimizer = asRecord(asRecord(environments?.['client'])?.['depsOptimizer']);
    if (optimizer === undefined) {
      return ['  dependency optimizer: none'];
    }
    const metadata = asRecord(optimizer['metadata']);
    const optimized = Object.keys(asRecord(metadata?.['optimized']) ?? {}).length;
    const discovered = Object.entries(asRecord(metadata?.['discovered']) ?? {});
    const scan = optimizer['scanProcessing'];
    const scanning = scan instanceof Promise && (await isPending(scan));
    const bundling: string[] = [];
    for (const [name, info] of discovered) {
      const processing = asRecord(info)?.['processing'];
      if (processing instanceof Promise && (await isPending(processing))) {
        bundling.push(name);
      }
    }
    return [
      `  dependency optimizer: ${optimized} optimized, ${discovered.length} discovered, scan ${scanning ? 'still running' : 'done'}`,
      `    still bundling: ${bundling.length === 0 ? 'none' : bundling.join(', ')}`,
    ];
  }
}
