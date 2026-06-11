// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Migrator-shaped console host: the dotnet-ef design-time target for the end-to-end CI leg.
// `dotnet ef migrations add/script/bundle` runs against this project; running it applies the
// committed migration chain (the integration test suite does the same in-process against a
// containerized SQL Server).
using Microsoft.EntityFrameworkCore;
using Tellma.Core.EntityFrameworkCore.MigrationsHost;

if (args.Contains("--migrate"))
{
    using MigrationsHostContext context = new MigrationsHostContextFactory().CreateDbContext(args);
    context.Database.Migrate();
    Console.WriteLine("Migrations applied.");
}
else
{
    Console.WriteLine("Tellma.Core.EntityFrameworkCore migrations host. Use `dotnet ef` against this project, or run with --migrate.");
}
