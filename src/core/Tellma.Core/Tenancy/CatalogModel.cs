// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The catalog's runtime model, built once from a context with no connection, and the
    ///     table-type binder over it: the membership rows bind to the deployed type's physical name and
    ///     column order as the model defines them, never by hard-coded ordinal.
    /// </summary>
    internal sealed class CatalogModel
    {
        private readonly Lazy<(IModel Model, TableTypeBinder Binder)> _built = new(static () =>
        {
            using var context = CatalogDbContext.Create(connectionString: null);
            IModel model = context.Model;
            return (model, new TableTypeBinder(model));
        });

        /// <summary>The catalog's runtime model.</summary>
        public IModel Model => _built.Value.Model;

        /// <summary>The binder of the catalog's table type.</summary>
        public TableTypeBinder Binder => _built.Value.Binder;
    }
}
