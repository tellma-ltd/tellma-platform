// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmProcessRunner } from '../common/process.js';
import { tmFormatProblem, type TmBuildProblem } from '../common/problems.js';

// What every command runs with: the project folder, the process seam and the
// two output streams, so the specs run a command against a fixture without
// touching the real process.

/** Where the command writes: its standard output and standard error. */
export interface TmCliStreams {
  /** Writes one chunk of normal output. */
  readonly out: (text: string) => void;
  /** Writes one chunk of diagnostic output. */
  readonly err: (text: string) => void;
}

/** What a command runs with. */
export interface TmCommandContext {
  /** The client project's folder: the working directory. */
  readonly cwd: string;
  /** Starts the programs the command runs. */
  readonly runner: TmProcessRunner;
  /** Where the command writes. */
  readonly streams: TmCliStreams;
}

/** Writes one progress line to the standard output. */
export function tmLogStep(context: TmCommandContext, text: string): void {
  context.streams.out(`[tellma-client] ${text}\n`);
}

/** Writes each problem as its one line to the standard error. */
export function tmReportProblems(context: TmCommandContext, problems: readonly TmBuildProblem[]): void {
  for (const problem of problems) {
    context.streams.err(`${tmFormatProblem(problem)}\n`);
  }
}
