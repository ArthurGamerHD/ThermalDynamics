using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Heat as something a player notices without looking at an instrument: a block that glows as
    /// it heats, and a cue in the cockpit before one of them fails.
    ///
    /// <para>
    /// The glow's brightness follows the last 100 K before the block's rating; its colour follows
    /// absolute temperature. Sound reaches the pilot when a hot block is hidden or off screen.
    /// See document-of-intent.md, Natural feedback.
    /// </para>
    ///
    /// <para>
    /// **Client side, and off costs nothing** (`C7`). A dedicated server returns before it looks at
    /// a temperature; a client with nothing hot on the grid compares one number — the hottest block
    /// against <see cref="ThermalSolver.CueFloorTemperature"/> — and returns.
    /// </para>
    ///
    /// <para>
    /// **The engine calls here have never run in a session.** Emissive parts and the sound emitter
    /// are the two places this mod reaches for something it cannot exercise offline; both are
    /// guarded so a block without an emissive material or an audio definition is skipped rather
    /// than throwing, and the whole pass is inside the tick's own handler. Recorded as a gap in
    /// known-issues.md.
    /// </para>
    /// </summary>
    public partial class ThermalGrid
    {
        /// <summary>
        /// Solver steps between cue scans. Four is one a second at the shipped clock, which is fast
        /// enough for a three-second lead and slow enough that the scan is not a per-step cost.
        ///
        /// **It must be at least one**: the scan reads the arrays a step writes, and asking twice
        /// inside a step reads a rate of zero and then a spike, from which no approach can be read.
        /// </summary>
        private const int CueInterval = 4;

        /// <summary>The sound a block makes as it comes up on its rating, and as it passes it.</summary>
        private static readonly MySoundPair ApproachingSound =
            new MySoundPair("ArcBlockDestroyedSmall");

        private static readonly MySoundPair CriticalSound =
            new MySoundPair("ArcBlockDestroyed");

        private readonly HeatCueState cueState = new HeatCueState();
        private readonly List<HeatCue> cues = new List<HeatCue>();

        /// <summary>Blocks this grid has written an emissive on, so it can put them back.</summary>
        private readonly Dictionary<Vector3I, float> glowing = new Dictionary<Vector3I, float>();

        /// <summary>
        /// The blocks currently glowing, for <see cref="ThermalGlow"/> to draw.
        ///
        /// Separate from <see cref="glowing"/>, which is a record of what has to be put back on the
        /// model: this is rebuilt whole each scan and is empty whenever nothing is hot, so the draw
        /// pass reads a count and returns.
        /// </summary>
        private readonly List<LitBlock> litBlocks = new List<LitBlock>();

        /// <summary>The blocks this grid is glowing, empty when none is.</summary>
        public IList<LitBlock> LitBlocks
        {
            get { return litBlocks; }
        }

        private readonly List<Vector3I> faded = new List<Vector3I>();

        private int stepsSinceCues;
        private float secondsSinceCues;

        private static MyEntity3DSoundEmitter cueEmitter;

        /// <summary>
        /// One cue pass. <paramref name="seconds"/> is play seconds since the last tick, which the
        /// forecast reads as its interval.
        /// </summary>
        private void UpdateCues(int steps, float seconds)
        {
            secondsSinceCues += seconds;
            stepsSinceCues += steps;
            if (stepsSinceCues < CueInterval) return;

            float interval = secondsSinceCues;
            stepsSinceCues = 0;
            secondsSinceCues = 0f;

            Settings settings = Settings.Instance;
            bool glow = settings != null && settings.HeatGlow;
            bool audible = settings != null && settings.HeatWarningSound;

            if (!glow && !audible)
            {
                ClearGlow();
                cueState.Clear();
                return;
            }

            litBlocks.Clear();

            // The one test a hull with nothing hot on it pays for. HottestNode is refreshed by the
            // observation pass and is an array read there, so this costs a comparison.
            ThermalNode hottest = HottestNode;
            if (hottest == null || hottest.Temperature < Simulation.Solver.CueFloorTemperature())
            {
                ClearGlow();
                cueState.Clear();
                return;
            }

            cues.Clear();
            Simulation.Solver.CollectHeatCues(cueState, interval, cues);

            for (int i = 0; i < cues.Count; i++)
            {
                HeatCue cue = cues[i];
                if (cue.Block == null) continue;

                if (glow) ApplyGlow(cue);
                if (audible && cue.Announce) Announce(cue);
            }

            if (glow) FadeBlocksNoLongerCued();
        }

        /// <summary>
        /// Writes one block's incandescence onto its model.
        ///
        /// The colour carries no brightness and the brightness carries no colour — the table is
        /// normalised so its brightest channel is full, and multiplying the two here is what keeps
        /// a dull red block dull rather than squaring its luminance.
        /// </summary>
        private void ApplyGlow(HeatCue cue)
        {
            float glow = cue.Glow;
            float written;

            if (glow <= 0f)
            {
                if (glowing.TryGetValue(cue.Block.Position, out written)) Fade(cue.Block.Position);
                return;
            }

            // Recorded before the emissive is attempted, because four fifths of block models have
            // nowhere to write one and those are exactly the blocks the drawn glow exists for.
            LitBlock lit = new LitBlock();
            lit.Position = cue.Block.Position;
            lit.Kelvin = cue.Kelvin;
            lit.Glow = glow;
            litBlocks.Add(lit);

            MyCubeBlock cube = FatBlockAt(cue.Block.Position);
            if (cube == null) return;

            Vector3 colour = Incandescence.Colour(cue.Kelvin);
            Color emissive = new Color(colour * glow);

            if (!Emit(cube, glow, emissive)) return;
            glowing[cue.Block.Position] = glow;
        }

        /// <summary>
        /// Plays the cue for one block, in the cockpit of the player flying this grid and nowhere
        /// else.
        ///
        /// <para>
        /// **Only the player at the controls hears it**, because they are the only one who can act
        /// on it, and because a hot ship in a hangar would otherwise chirp at everyone near it. The
        /// emitter is one shared object rather than one per block: a hull losing a dozen blocks at
        /// once should sound like an event, not like a dozen.
        /// </para>
        /// </summary>
        private void Announce(HeatCue cue)
        {
            if (!IsPilotedLocally()) return;

            if (cueEmitter == null) cueEmitter = new MyEntity3DSoundEmitter(null);
            if (cueEmitter.IsPlaying) return;

            cueEmitter.SetPosition(null);
            cueEmitter.Force2D = true;
            cueEmitter.PlaySound(
                cue.Stage == HeatCueStage.Critical ? CriticalSound : ApproachingSound, true);
        }

        /// <summary>True when the local player is at the controls of this grid.</summary>
        private bool IsPilotedLocally()
        {
            if (MyAPIGateway.Session == null) return false;

            IMyPlayer player = MyAPIGateway.Session.LocalHumanPlayer;
            if (player == null) return false;

            IMyCubeBlock seat = player.Controller == null
                ? null
                : player.Controller.ControlledEntity as IMyCubeBlock;

            return seat != null && seat.CubeGrid == Grid;
        }

        /// <summary>Puts back every block this grid is no longer cueing.</summary>
        private void FadeBlocksNoLongerCued()
        {
            if (glowing.Count == 0) return;

            faded.Clear();
            foreach (KeyValuePair<Vector3I, float> entry in glowing)
            {
                bool held = false;
                for (int i = 0; i < cues.Count; i++)
                {
                    if (cues[i].Block == null || cues[i].Block.Position != entry.Key) continue;
                    held = cues[i].Glow > 0f;
                    break;
                }

                if (!held) faded.Add(entry.Key);
            }

            for (int i = 0; i < faded.Count; i++) Fade(faded[i]);
            faded.Clear();
        }

        /// <summary>Puts every glowing block back and forgets them.</summary>
        private void ClearGlow()
        {
            litBlocks.Clear();

            if (glowing.Count == 0) return;

            faded.Clear();
            foreach (KeyValuePair<Vector3I, float> entry in glowing) faded.Add(entry.Key);
            for (int i = 0; i < faded.Count; i++) Fade(faded[i]);
            faded.Clear();
        }

        /// <summary>
        /// Returns one block to the emissive the game gave it.
        ///
        /// <c>SetEmissiveStateWorking</c> is the block's own answer to what it should look like, so
        /// asking for it is <c>C9</c> — the game's answer is read rather than overridden — and a
        /// block with no state of its own falls back to no glow at all.
        /// </summary>
        private void Fade(Vector3I position)
        {
            glowing.Remove(position);

            MyCubeBlock cube = FatBlockAt(position);
            if (cube == null) return;

            try
            {
                if (!cube.SetEmissiveStateWorking()) Emit(cube, 0f, Color.Black);
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.Fade", e);
            }
        }

        /// <summary>
        /// Writes an emissive on one block, and says whether the block had anywhere to write it.
        ///
        /// Only a model with an emissive material can carry this; armour has none, which is a real
        /// limit on the glow rather than a defect in it, and is one reason the audio cue exists.
        /// </summary>
        private static bool Emit(MyCubeBlock cube, float glow, Color emissive)
        {
            if (cube.Render == null || cube.Render.RenderObjectIDs == null
                || cube.Render.RenderObjectIDs.Length == 0)
            {
                return false;
            }

            uint id = cube.Render.RenderObjectIDs[0];
            if (id == uint.MaxValue) return false;

            try
            {
                cube.UpdateEmissiveParts(id, glow, emissive, emissive);
                return true;
            }
            catch (Exception e)
            {
                Telemetry.Exception("ThermalGrid.Emit", e);
                return false;
            }
        }

        /// <summary>The game block at a cell, or null where the cell holds none.</summary>
        private MyCubeBlock FatBlockAt(Vector3I position)
        {
            ThermalBlock bound = Get(position);
            if (bound == null || bound.Block == null) return null;

            return bound.Block.FatBlock as MyCubeBlock;
        }
    }
}
