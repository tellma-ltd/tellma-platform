#!/usr/bin/env node
// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The `tellma-client` executable. It stays a plain script outside dist/ so
// the package manager can link it before the package is built; the command
// itself is the compiled tmRunCli.
import { tmRunCli } from '../dist/cli/tellma-client.js';

process.exitCode = await tmRunCli(process.argv.slice(2));
