// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { RuleTester, type Linter } from 'eslint';
import { describe, it } from 'vitest';

// The lint rule specs' RuleTester. Left to itself, RuleTester finds no global
// describe or it (Vitest exports them instead) and runs every case of a `run`
// synchronously inside the test that calls it: one test then carries all its
// cases, and also the first parse, lint and option-schema compile of the
// worker process, so a machine busy with the other spec files could push it
// past the default 5 s test timeout. Handed Vitest's own functions, `run`
// registers a describe block per rule and a test per case instead, which
// keeps every test to one case's work and names the failing case in the
// report.
//
// The functions sit on a subclass. RuleTester's describe, it and itOnly are
// static accessors that store on the class they are called on, and `run`
// reads them through its instance's constructor, so ESLint's own class is
// never changed: a spec that uses RuleTester directly behaves the same
// whichever spec files ran before it in the same process.

/** ESLint's RuleTester, registering its cases through Vitest's describe and it. */
class VitestRuleTester extends RuleTester {}

VitestRuleTester.describe = describe;
VitestRuleTester.it = it;
VitestRuleTester.itOnly = it.only;

/**
 * A RuleTester whose `run` registers each valid and invalid case as a Vitest
 * test of its own. Call `run` while a describe block collects its tests,
 * never from inside a test: Vitest refuses a test declared in another test.
 *
 * @param config The configuration every case is linted with, before the case's own.
 * @returns The rule tester.
 */
export function createRuleTester(config: Linter.Config): RuleTester {
  return new VitestRuleTester(config);
}
