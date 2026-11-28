// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Tests.Infrastructure
{
    /// <summary>A service the test features register.</summary>
    internal interface ISampleService
    {
        /// <summary>Which implementation answered.</summary>
        string Who { get; }
    }

    /// <summary>A pack's implementation.</summary>
    internal sealed class SampleService : ISampleService
    {
        /// <inheritdoc />
        public string Who => "pack";
    }

    /// <summary>A distribution's replacement.</summary>
    internal sealed class ReplacementService : ISampleService
    {
        /// <inheritdoc />
        public string Who => "distribution";
    }

    /// <summary>An extension point resolved as a collection.</summary>
    [CollectionService]
    internal interface ISampleExtension;

    /// <summary>One extension.</summary>
    internal sealed class FirstExtension : ISampleExtension;

    /// <summary>Another extension.</summary>
    internal sealed class SecondExtension : ISampleExtension;

    /// <summary>An open generic service.</summary>
    /// <typeparam name="T">The argument.</typeparam>
    internal interface IRepository<T>;

    /// <summary>Its open generic implementation.</summary>
    /// <typeparam name="T">The argument.</typeparam>
    internal sealed class Repository<T> : IRepository<T>;

    /// <summary>Options a test feature binds.</summary>
    internal sealed class SampleOptions
    {
        /// <summary>A required name.</summary>
        [Required]
        public string? Name { get; set; }

        /// <summary>A count between 1 and 10.</summary>
        [Range(1, 10)]
        public int Count { get; set; } = 1;
    }

    /// <summary>A pack's default entity.</summary>
    internal class SampleEntity
    {
        /// <summary>The id.</summary>
        public int Id { get; set; }
    }

    /// <summary>A distribution's leaf for it.</summary>
    internal sealed class SampleLeaf : SampleEntity;

    /// <summary>Another leaf for it.</summary>
    internal sealed class OtherLeaf : SampleEntity;

    /// <summary>A shape a compliance pack requires of <see cref="SampleEntity" />.</summary>
    internal interface IEInvoiceTracked
    {
        /// <summary>The submission status.</summary>
        string? EInvoiceStatus { get; set; }

        /// <summary>The authority's id for the invoice.</summary>
        Guid? EInvoiceUuid { get; init; }

        /// <summary>Whether the invoice was accepted.</summary>
        bool IsAccepted => EInvoiceStatus == "accepted";
    }

    /// <summary>A leaf carrying the shape.</summary>
    internal sealed class TrackedLeaf : SampleEntity, IEInvoiceTracked
    {
        /// <inheritdoc />
        public string? EInvoiceStatus { get; set; }

        /// <inheritdoc />
        public Guid? EInvoiceUuid { get; init; }
    }

    /// <summary>A table-type row class.</summary>
    internal sealed class SampleRow
    {
        /// <summary>The id.</summary>
        public int Id { get; set; }
    }
}
