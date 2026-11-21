// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Identity.Data
{
    /// <summary>
    ///     The identity store: ASP.NET Core Identity tables (schema version 3, including
    ///     passkeys), OpenIddict's four tables (applications, authorizations, scopes, tokens),
    ///     and the engine's own tables — all under the dedicated
    ///     <see cref="TellmaIdentityConstants.Schema" /> schema so an in-proc deployment can share
    ///     a distribution's database. The model carries the platform collation, so a database
    ///     created from it is created in that collation.
    /// </summary>
    /// <param name="options">The context options supplied by the host registration.</param>
    public sealed class TellmaIdentityDbContext(DbContextOptions<TellmaIdentityDbContext> options)
        : IdentityDbContext<TellmaIdentityUser>(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder builder)
        {
            // Everything — Identity, OpenIddict, and engine tables — lives under one schema.
            builder.HasDefaultSchema(TellmaIdentityConstants.Schema);

            // The platform collation (spec 0011 §6.7). EF writes it into the CREATE DATABASE it
            // issues for a missing development or test store; an initial migration carries no
            // collation operation, and no later one may change it, because Azure SQL Database
            // refuses the ALTER DATABASE … COLLATE that a change would scaffold.
            builder.UseCollation(DatabaseCollations.Platform);

            base.OnModelCreating(builder);

            // OpenIddict's application/authorization/scope/token entities.
            builder.UseOpenIddict();

            // The engine's own entities (sessions, codes, passes, audit).
            builder.ApplyConfigurationsFromAssembly(typeof(TellmaIdentityDbContext).Assembly);
        }
    }
}
