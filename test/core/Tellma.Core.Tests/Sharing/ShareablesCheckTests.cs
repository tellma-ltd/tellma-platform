// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Sharing
{
    /// <summary>
    ///     <c>core.shareables</c> and the realiser of a shareable declaration: a pair is whole when its
    ///     share entity references the row once, is neither a stack nor a child collection, and the
    ///     row's stack is served by the shareable service over the pair.
    /// </summary>
    public sealed class ShareablesCheckTests
    {
        /// <summary>The check's name.</summary>
        private const string Check = "core.shareables";

        /// <summary>The fixture's notification type.</summary>
        private const string Notice = "fixture.note.shared";

        [Fact]
        public async Task The_core_composition_passes()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));
        }

        [Fact]
        public async Task A_sound_pair_passes()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static contribution => contribution
                .Entity<ShNote, ShNoteService>()
                .Shareable<ShNote, ShNoteShare>(Notice));

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public async Task A_stack_whose_service_does_not_derive_from_the_shareable_service_is_refused()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static contribution => contribution
                .Entity<ShNote>()
                .Shareable<ShNote, ShNoteShare>(Notice));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal(
                "The shareable Tellma.Core.Tests.Sharing.ShNote of the feature 'fixture' is served by the platform's default service, which does not derive from ShareableService<ShNote, ShNoteShare>, so its owners and standard rows go unguarded.",
                problem.Problem);
        }

        [Fact]
        public async Task A_shareable_whose_owner_is_not_server_owned_is_refused()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static contribution => contribution
                .Entity<ShEditableNote, ShEditableNoteService>()
                .Shareable<ShEditableNote, ShEditableNoteShare>(Notice));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal(
                "The shareable Tellma.Core.Tests.Sharing.ShEditableNote of the feature 'fixture' declares its OwnerId as Editable, not server-owned, so a save could change its owner or a transfer could not assign one.",
                problem.Problem);
        }

        [Fact]
        public async Task A_shareable_with_no_stack_is_refused()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static contribution => contribution
                .Model<ShNoteModel>()
                .Shareable<ShNote, ShNoteShare>(Notice));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal("The shareable Tellma.Core.Tests.Sharing.ShNote of the feature 'fixture' has no stack.", problem.Problem);
        }

        [Fact]
        public async Task A_share_entity_that_is_also_a_stack_is_refused()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static contribution => contribution
                .Entity<ShNote, ShNoteService>()
                .Entity<ShNoteShare>()
                .Shareable<ShNote, ShNoteShare>(Notice));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.StartsWith("The shareable Tellma.Core.Tests.Sharing.ShNote of the feature 'fixture' is shared through fx.ShNoteShare, which a feature also contributes as a stack", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_share_entity_with_no_reference_to_the_row_is_refused_at_composition()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                .Entity<ShNote, ShNoteService>()
                .Shareable<ShNote, ShLooseShare>(Notice))));

            CompositionProblem problem = Assert.Single(problems, static candidate => candidate.Problem.Contains("ShLooseShare", StringComparison.Ordinal));
            Assert.Equal(
                "Feature 'fixture' shares Tellma.Core.Tests.Sharing.ShNote through Tellma.Core.Tests.Sharing.ShLooseShare, which has no single property marked [References(typeof(ShNote))].",
                problem.Problem);
        }

        [Fact]
        public void An_entity_declared_shareable_twice_is_refused_at_composition()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                .Entity<ShNote, ShNoteService>()
                .Shareable<ShNote, ShNoteShare>(Notice)
                .Shareable<ShNote, ShNoteShare>(Notice))));

            CompositionProblem problem = Assert.Single(problems, static candidate => candidate.Problem.Contains("already made shareable", StringComparison.Ordinal));
            Assert.Equal(
                "Feature 'fixture' makes Tellma.Core.Tests.Sharing.ShNote shareable, which feature 'fixture' already made shareable.",
                problem.Problem);
        }

        [Fact]
        public void A_blank_notification_type_is_refused_where_it_is_declared()
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => TestComposition.Compose(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                .Entity<ShNote, ShNoteService>()
                .Shareable<ShNote, ShNoteShare>(" ")))));

            Assert.Equal("notificationType", exception.ParamName);
        }

        /// <summary>Composes a fixture feature, builds the provider and runs every startup check.</summary>
        /// <param name="contribute">The fixture's contribution.</param>
        /// <returns>The problems.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(Action<FeatureContribution> contribute)
        {
            return TestComposition.CheckAsync(tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribute)));
        }
    }

    /// <summary>A shareable note.</summary>
    [Table("ShNotes", Schema = "fx")]
    [TableType]
    public class ShNote : TopLevelEntity, IShareable
    {
        /// <summary>The title.</summary>
        [MaxLength(255)]
        public string Title { get; set; } = null!;

        /// <inheritdoc />
        [ServerOwned]
        [References(typeof(User))]
        public int? OwnerId { get; set; }

        /// <inheritdoc />
        [WriteOnce]
        public int? BasedOnId { get; set; }
    }

    /// <summary>A grant of a note.</summary>
    [Table("ShNoteShares", Schema = "fx")]
    [TableType]
    public class ShNoteShare : Entity<int>, IShareRow
    {
        /// <summary>The note.</summary>
        [References(typeof(ShNote))]
        public int NoteId { get; set; }

        /// <inheritdoc />
        [References(typeof(User))]
        public int? UserId { get; set; }

        /// <inheritdoc />
        [References(typeof(Role))]
        public int? RoleId { get; set; }

        /// <inheritdoc />
        public ShareAccess Access { get; set; }

        /// <inheritdoc />
        [References(typeof(User))]
        public int SharedById { get; set; }

        /// <inheritdoc />
        public DateTimeOffset SharedAt { get; set; }
    }

    /// <summary>A grant class that names no note.</summary>
    [Table("ShLooseShares", Schema = "fx")]
    public class ShLooseShare : Entity<int>, IShareRow
    {
        /// <summary>A note's id, with no reference.</summary>
        public int NoteId { get; set; }

        /// <inheritdoc />
        public int? UserId { get; set; }

        /// <inheritdoc />
        public int? RoleId { get; set; }

        /// <inheritdoc />
        public ShareAccess Access { get; set; }

        /// <inheritdoc />
        public int SharedById { get; set; }

        /// <inheritdoc />
        public DateTimeOffset SharedAt { get; set; }
    }

    /// <summary>The notes' service: the shareable service with its three actions.</summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="commands">The sharing commands.</param>
    public sealed class ShNoteService(IEntityPipelineFactory factory, IShareCommands<ShNote> commands) : ShareableService<ShNote, ShNoteShare>(factory, commands)
    {
        /// <summary>Shares notes with users.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The grants.</param>
        /// <returns>A task.</returns>
        [EntityAction(ShareableNames.ShareAction, Action = "Share")]
        public Task ShareAsync(ActionContext<ShNote, int> context, ShareArguments arguments)
        {
            return Commands.ShareAsync(context, arguments);
        }
    }

    /// <summary>A shareable note whose owner the client may edit.</summary>
    [Table("ShEditableNotes", Schema = "fx")]
    [TableType]
    public class ShEditableNote : TopLevelEntity, IShareable
    {
        /// <summary>The title.</summary>
        [MaxLength(255)]
        public string Title { get; set; } = null!;

        /// <inheritdoc />
        [References(typeof(User))]
        public int? OwnerId { get; set; }

        /// <inheritdoc />
        [WriteOnce]
        public int? BasedOnId { get; set; }
    }

    /// <summary>A grant of an editable-owner note.</summary>
    [Table("ShEditableNoteShares", Schema = "fx")]
    [TableType]
    public class ShEditableNoteShare : Entity<int>, IShareRow
    {
        /// <summary>The note.</summary>
        [References(typeof(ShEditableNote))]
        public int NoteId { get; set; }

        /// <inheritdoc />
        [References(typeof(User))]
        public int? UserId { get; set; }

        /// <inheritdoc />
        [References(typeof(Role))]
        public int? RoleId { get; set; }

        /// <inheritdoc />
        public ShareAccess Access { get; set; }

        /// <inheritdoc />
        [References(typeof(User))]
        public int SharedById { get; set; }

        /// <inheritdoc />
        public DateTimeOffset SharedAt { get; set; }
    }

    /// <summary>The editable-owner notes' service.</summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="commands">The sharing commands.</param>
    public sealed class ShEditableNoteService(IEntityPipelineFactory factory, IShareCommands<ShEditableNote> commands)
        : ShareableService<ShEditableNote, ShEditableNoteShare>(factory, commands);

    /// <summary>Maps the notes' table with no stack.</summary>
    internal sealed class ShNoteModel : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<ShNote>
    {
        /// <inheritdoc />
        public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ShNote> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
        }
    }
}
