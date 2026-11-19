// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3: an enlisted stack's participants see a host of their own, which shares the
    // frame they were enlisted with (its kind, its operation, its confirmations and its rounds), and
    // holds the groups they enlist in turn, which nest around their group. It admits enlistment while
    // the host's rounds run, from the group's id assignment on (never from its preprocessing), and
    // never a self-pair: only an open frame its own API action opened does.
    /// <summary>The frame an enlisted group's participants see as their host.</summary>
    /// <param name="host">The frame the group was enlisted with.</param>
    /// <param name="errors">The group's re-homing view, which the groups it holds re-home theirs into.</param>
    internal sealed class GroupFrame(EnlistmentFrame host, ValidationErrors errors) : EnlistmentFrame
    {
        /// <summary>Whether the group reached its id assignment, so its participants validate.</summary>
        private volatile bool _composing;

        /// <summary>The frame the group was enlisted with.</summary>
        public EnlistmentFrame Host { get; } = host;

        /// <inheritdoc />
        public override WriteHostKind Kind => Host.Kind;

        /// <inheritdoc />
        public override string Operation => Host.Operation;

        /// <inheritdoc />
        public override IConfirmations Confirmations => Host.Confirmations;

        /// <inheritdoc />
        public override EnlistmentRounds Rounds => Host.Rounds;

        /// <inheritdoc />
        public override ValidationErrors Errors { get; } = errors;

        /// <inheritdoc />
        public override SaveSource Source => Host.Source;

        /// <inheritdoc />
        public override bool IsComposing => _composing && !Rounds.IsClosed;

        /// <summary>Starts composing: the group's ids are assigned, or ride its host's next round.</summary>
        public void BeginComposing()
        {
            _composing = true;
        }

        /// <inheritdoc />
        protected override void EnsureAdmits(Type writer)
        {
            if (!_composing || Rounds.IsClosed)
            {
                throw new InvalidOperationException(
                    $"The class {writer.Name} enlisted a write with {Operation} while its stack was not validating. An enlisted stack's participants enlist from their validation hooks and validators, never from preprocessing, a persist effect or post-commit work.");
            }
        }
    }
}
