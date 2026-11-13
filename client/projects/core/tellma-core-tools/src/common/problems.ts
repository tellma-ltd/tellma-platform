// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The two ways a `tellma-client` command ends badly (spec 0024 §2.6): a build
// problem, which the checks find in the sources or the output and print one
// per line, and a build failure, a step that could not run at all.

/** How serious a build problem is: an error fails the build, a warning prints and passes. */
export type TmBuildProblemSeverity = 'error' | 'warning';

/** One problem a check found in the sources or the output. */
export interface TmBuildProblem {
  /** The problem's code, such as `manifest-invalid`. */
  readonly code: string;
  /** Whether the problem fails the build. */
  readonly severity: TmBuildProblemSeverity;
  /** The declaration or the file concerned, such as `page:centers/map` or `public/manifest.webmanifest`. */
  readonly key: string;
  /** The feature the problem belongs to: a feature key, or the distribution's slug for its own files. */
  readonly feature: string;
  /** What is wrong, for the developer who fixes it. */
  readonly message: string;
}

/**
 * A step that could not run: a missing tool or file, a failed child process,
 * or emitted metadata the build cannot read. The command exits with code 2.
 */
export class TmBuildFailure extends Error {
  /**
   * Creates the failure.
   *
   * @param message - What failed, for the developer.
   * @param options - The underlying error, when there is one.
   */
  constructor(message: string, options?: ErrorOptions) {
    super(message, options);
    this.name = 'TmBuildFailure';
  }
}

/** Replaces the characters that would split a problem line's space-separated fields. */
function field(value: string): string {
  const text = value.trim().replace(/\s+/g, '_');
  return text === '' ? '-' : text;
}

/**
 * Formats a problem as its one printed line: the code, the key and the
 * feature, separated by spaces, then the message.
 *
 * @param problem - The problem.
 * @returns The line, without a line break.
 */
export function tmFormatProblem(problem: TmBuildProblem): string {
  return `${field(problem.code)} ${field(problem.key)} ${field(problem.feature)} - ${problem.message}`;
}

/** Whether any of the problems is an error, which fails the build. */
export function tmHasErrors(problems: readonly TmBuildProblem[]): boolean {
  return problems.some((problem) => problem.severity === 'error');
}
