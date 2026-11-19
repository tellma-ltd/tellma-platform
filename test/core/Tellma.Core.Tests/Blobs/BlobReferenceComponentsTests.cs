// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Blobs;
using Tellma.Core.Composition;
using Tellma.Core.Crud;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The blob pair's registration: the validator and the effect, closed over the leaf and
    ///     attached by <c>core</c>, at the head of the components of every stack whose aggregate
    ///     references a blob at any depth, ahead of a feature's own, and on no other stack.
    /// </summary>
    public sealed class BlobReferenceComponentsTests : IAsyncDisposable
    {
        /// <summary>The blob suite's model, with a validator of its own on the album, a stack referencing a blob only at depth two, and one referencing none.</summary>
        private readonly ServiceProvider _provider = TestComposition.Compose(static tellma => tellma.AddFeature(BlobModel.Feature(static contribution => contribution
                .Validator<BmAlbum, AlbumRule>()
                .Entity<BrBinder>()
                .Entity<BrPlain>())))
            .BuildServiceProvider(validateScopes: true);

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _provider.DisposeAsync();
        }

        [Fact]
        public void A_stack_referencing_blobs_runs_the_pair_first_attached_by_core()
        {
            Assert.Equal(
                [
                    $"Validator {typeof(BlobReferenceValidator<BmAlbum>)} {CoreFeature.FeatureName}",
                    $"PersistEffect {typeof(BlobReferenceEffect<BmAlbum>)} {CoreFeature.FeatureName}",
                    $"Validator {typeof(AlbumRule)} bm",
                ],
                Components<BmAlbum>());

            // --- The container resolves them in that order ---
            using IServiceScope scope = _provider.CreateScope();
            Assert.IsType<BlobReferenceValidator<BmAlbum>>(scope.ServiceProvider.GetServices<IEntityValidator<BmAlbum>>().First());
            Assert.IsType<BlobReferenceEffect<BmAlbum>>(Assert.Single(scope.ServiceProvider.GetServices<IPersistEffect<BmAlbum>>()));
        }

        [Fact]
        public void A_stack_referencing_a_blob_only_on_a_grandchild_runs_the_pair()
        {
            Assert.Equal(
                [
                    $"Validator {typeof(BlobReferenceValidator<BrBinder>)} {CoreFeature.FeatureName}",
                    $"PersistEffect {typeof(BlobReferenceEffect<BrBinder>)} {CoreFeature.FeatureName}",
                ],
                Components<BrBinder>());
        }

        [Fact]
        public void A_stack_referencing_no_blob_runs_neither()
        {
            Assert.Empty(Components<BrPlain>());

            using IServiceScope scope = _provider.CreateScope();
            Assert.Empty(scope.ServiceProvider.GetServices<IEntityValidator<BrPlain>>());
            Assert.Empty(scope.ServiceProvider.GetServices<IPersistEffect<BrPlain>>());
        }

        /// <summary>A stack's components, each as its kind, class and attaching feature, in order.</summary>
        /// <typeparam name="TLeaf">The stack's leaf.</typeparam>
        /// <returns>The lines.</returns>
        private string[] Components<TLeaf>()
        {
            ComposedStack stack = _provider.GetRequiredService<StackComposition>().Stacks.Single(static stack => stack.Leaf == typeof(TLeaf));
            return [.. stack.Components.Select(static component => $"{component.Kind} {component.ComponentType} {component.Owner.Name}")];
        }

        /// <summary>The album's own validator, which validates nothing.</summary>
        private sealed class AlbumRule : IEntityValidator<BmAlbum>
        {
            /// <inheritdoc />
            public Task ValidateAsync(SaveContext<BmAlbum> context)
            {
                return Task.CompletedTask;
            }
        }
    }

    /// <summary>A binder: an aggregate root with no blob reference of its own.</summary>
    [Table("Binders", Schema = "br")]
    [TableType]
    [Stack(Operations = StackOperations.All)]
    public class BrBinder : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The sheets.</summary>
        [NotMapped]
        public List<BrSheet>? Sheets { get; set; }
    }

    /// <summary>A sheet of a binder, with no blob reference of its own.</summary>
    [Table("Sheets", Schema = "br")]
    [TableType]
    public class BrSheet : ChildEntity
    {
        /// <summary>The binder.</summary>
        [ParentKey]
        public int BinderId { get; set; }

        /// <summary>The stamps.</summary>
        [NotMapped]
        public List<BrStamp>? Stamps { get; set; }
    }

    /// <summary>A stamp on a sheet: the binder's only blob reference, two levels down.</summary>
    [Table("Stamps", Schema = "br")]
    [TableType]
    public class BrStamp : ChildEntity
    {
        /// <summary>The sheet.</summary>
        [ParentKey]
        public int SheetId { get; set; }

        /// <summary>The stamp's image.</summary>
        [BlobReference("br-stamp", BlobPreset.Signature)]
        public int? ImageId { get; set; }
    }

    /// <summary>A plain aggregate root referencing no blob.</summary>
    [Table("Plains", Schema = "br")]
    [TableType]
    [Stack(Operations = StackOperations.All)]
    public class BrPlain : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }
}
