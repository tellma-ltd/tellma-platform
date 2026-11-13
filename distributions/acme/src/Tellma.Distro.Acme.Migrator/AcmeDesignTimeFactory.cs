// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Migrator;

namespace Tellma.Distro.Acme.Migrator
{
    /// <summary>
    ///     Acme's design-time factory: <c>dotnet ef</c> builds the tenant context from the
    ///     distribution's composition method, so the migrations it scaffolds are those of the model
    ///     both hosts build.
    /// </summary>
    public sealed class AcmeDesignTimeFactory : TellmaDesignTimeDbContextFactory<TellmaDbContext>
    {
        /// <inheritdoc />
        protected override string Slug => AcmeComposition.Slug;

        /// <inheritdoc />
        public override void Compose(TellmaBuilder builder)
        {
            AcmeComposition.Compose(builder);
        }
    }
}
