// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Tellma.Identity.Data
{
    /// <summary>
    ///     The identity store: ASP.NET Core Identity tables (schema version 3, including
    ///     passkeys), OpenIddict's four tables (applications, authorizations, scopes, tokens),
    ///     and the engine's own tables — all under the dedicated
    ///     <see cref="TellmaIdentityConstants.Schema" /> schema so an in-proc deployment can share
    ///     a distribution's database.
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

            base.OnModelCreating(builder);

            // OpenIddict's application/authorization/scope/token entities.
            builder.UseOpenIddict();

            // The engine's own entities (sessions, codes, passes, audit).
            builder.ApplyConfigurationsFromAssembly(typeof(TellmaIdentityDbContext).Assembly);
        }
    }
}
