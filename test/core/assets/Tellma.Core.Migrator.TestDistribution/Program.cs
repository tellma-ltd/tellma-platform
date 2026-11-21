// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The test distribution's migrator, as a distribution's Program.cs is: one call. The suites drive
// the same commands in process; this entry point is what dotnet-ef and a manual run start.
using Tellma.Core.Migrator;
using Tellma.Core.Migrator.TestDistribution;

return await TellmaMigrator.RunAsync(args, TestDistribution.Slug, TestDistribution.Compose).ConfigureAwait(false);
