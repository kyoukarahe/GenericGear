using System;
using System.Collections.Generic;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    public sealed class GearInvestResolvedPlaybackController : MonoBehaviour
    {
        private readonly Dictionary<string, PlaybackChannel> _channels =
            new Dictionary<string, PlaybackChannel>(StringComparer.Ordinal);

        private readonly Dictionary<string, PlaybackBodyBinding> _bindings =
            new Dictionary<string, PlaybackBodyBinding>(StringComparer.Ordinal);

        private GearInvestArtifactSession _session;
        private GearInvestMechanismView _view;

        public bool IsInitialized { get; private set; }

        public bool IsPlaying { get; set; }

        public double RootTurns { get; private set; }

        public double TurnsPerSecond { get; set; } = 0.1;

        public void Initialize(GearInvestArtifactSession session, GearInvestMechanismView view)
        {
            if (session == null)
            {
                throw new ArgumentNullException("session");
            }

            if (view == null)
            {
                throw new ArgumentNullException("view");
            }

            _session = session;
            _view = view;
            _channels.Clear();
            _bindings.Clear();

            foreach (var channel in session.Artifact.Candidate.ResolvedPlayback.Channels)
            {
                _channels.Add(channel.DofId, channel);
            }

            foreach (var binding in session.Artifact.Candidate.ResolvedPlayback.BodyBindings)
            {
                _bindings.Add(binding.BodyId, binding);
            }

            IsInitialized = true;
            Apply();
        }

        public void SetRootTurns(double rootTurns)
        {
            RootTurns = rootTurns;
            Apply();
        }

        public KinematicEvaluation EvaluateExact(Rational rootTurns)
        {
            EnsureInitialized();
            return _session.EvaluateExact(rootTurns);
        }

        private void Update()
        {
            if (!IsInitialized || !IsPlaying)
            {
                return;
            }

            RootTurns += Time.deltaTime * TurnsPerSecond;
            Apply();
        }

        private void Apply()
        {
            if (!IsInitialized)
            {
                return;
            }

            foreach (var pair in _bindings)
            {
                GearInvestBodyView bodyView;
                PlaybackChannel channel;
                if (!_view.TryGetBody(pair.Key, out bodyView) ||
                    !_channels.TryGetValue(pair.Value.DofId, out channel))
                {
                    throw new InvalidOperationException("ResolvedPlayback references an unknown body or DOF.");
                }

                var dofTurns =
                    (RootTurns * ToDouble(channel.ExactCoefficient)) +
                    ToDouble(channel.ExactPhaseOffset);
                var bodyTurns = dofTurns + ToDouble(pair.Value.ExactMountingPhase);
                bodyView.ApplyTurns(bodyTurns);
            }
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Playback must be initialized before evaluation.");
            }
        }

        private static double ToDouble(Rational value)
        {
            return (double)value.Numerator / (double)value.Denominator;
        }
    }
}
