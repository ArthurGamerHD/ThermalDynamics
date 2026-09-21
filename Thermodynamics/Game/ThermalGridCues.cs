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
    public partial class ThermalGrid
    {
        private const int CueInterval = 4;

        private static readonly MySoundPair ApproachingSound =
/// <summary>MySoundPair operation.</summary>
            new MySoundPair("ArcBlockDestroyedSmall");

        private static readonly MySoundPair CriticalSound =
/// <summary>MySoundPair operation.</summary>
            new MySoundPair("ArcBlockDestroyed");

/// <summary>HeatCueState operation.</summary>
        private readonly HeatCueState cueState = new HeatCueState();
/// <summary>List operation.</summary>
        private readonly List<HeatCue> cues = new List<HeatCue>();

        private readonly Dictionary<Vector3I, float> glowing = new Dictionary<Vector3I, float>();

/// <summary>List operation.</summary>
        private readonly List<LitBlock> litBlocks = new List<LitBlock>();

        public IList<LitBlock> LitBlocks
        {
            get { return litBlocks; }
        }

/// <summary>List operation.</summary>
        private readonly List<Vector3I> faded = new List<Vector3I>();

        private int stepsSinceCues;
        private float secondsSinceCues;

        private static MyEntity3DSoundEmitter cueEmitter;

/// <summary>UpdateCues operation.</summary>
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

/// <summary>Applies the glow.</summary>
        private void ApplyGlow(HeatCue cue)
        {
            float glow = cue.Glow;
            float written;

            if (glow <= 0f)
            {
                if (glowing.TryGetValue(cue.Block.Position, out written)) Fade(cue.Block.Position);
                return;
            }

/// <summary>LitBlock operation.</summary>
            LitBlock lit = new LitBlock();
            lit.Position = cue.Block.Position;
            lit.Kelvin = cue.Kelvin;
            lit.Glow = glow;
            litBlocks.Add(lit);

/// <summary>FatBlockAt operation.</summary>
            MyCubeBlock cube = FatBlockAt(cue.Block.Position);
            if (cube == null) return;

            Vector3 colour = Incandescence.Colour(cue.Kelvin);
/// <summary>Color operation.</summary>
            Color emissive = new Color(colour * glow);

            if (!Emit(cube, glow, emissive)) return;
            glowing[cue.Block.Position] = glow;
        }

/// <summary>Announce operation.</summary>
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

/// <summary>IsPilotedLocally operation.</summary>
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

/// <summary>FadeBlocksNoLongerCued operation.</summary>
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

/// <summary>ClearGlow operation.</summary>
        private void ClearGlow()
        {
            litBlocks.Clear();

            if (glowing.Count == 0) return;

            faded.Clear();
            foreach (KeyValuePair<Vector3I, float> entry in glowing) faded.Add(entry.Key);
            for (int i = 0; i < faded.Count; i++) Fade(faded[i]);
            faded.Clear();
        }

/// <summary>Fade operation.</summary>
        private void Fade(Vector3I position)
        {
            glowing.Remove(position);

/// <summary>FatBlockAt operation.</summary>
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

/// <summary>Emit operation.</summary>
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

/// <summary>FatBlockAt operation.</summary>
        private MyCubeBlock FatBlockAt(Vector3I position)
        {
/// <summary>Returns the .</summary>
            ThermalBlock bound = Get(position);
            if (bound == null || bound.Block == null) return null;

            return bound.Block.FatBlock as MyCubeBlock;
        }
    }
}
