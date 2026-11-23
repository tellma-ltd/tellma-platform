// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.1: the exports' stack service. A row exists only once its run has a file, so the
    // export's job inserts it as an enlisted save at the end of the run and nothing here enqueues;
    // a delete releases the file through the blob capture and needs no guard, since a row's job
    // has always ended by the time the row exists.
    /// <summary>
    ///     The exports' stack service: the reads, the delete, and the enlisted save the export's job
    ///     makes when its workbook is ready.
    /// </summary>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    public sealed class ExportService(IEntityPipelineFactory factory) : EntityService<Export>(factory);
}
