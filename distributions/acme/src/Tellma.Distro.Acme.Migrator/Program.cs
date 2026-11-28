// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The Acme migrator: one call, which runs the command on the command line over the same
// composition the web host runs. Its tenant migrations are this assembly's.
using Tellma.Core.Migrator;
using Tellma.Distro.Acme;
using Tellma.Identity.Migrations;

return await TellmaMigrator.RunAsync(args, AcmeComposition.Slug, AcmeComposition.Compose, TellmaIdentityStore.MigrateAsync).ConfigureAwait(false);
