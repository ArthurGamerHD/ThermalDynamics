using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// Heat conduction across a rotor or a piston, where the two blocks belong to different grids and
    /// so to different solvers — a link that can live in neither. Exchanged once per tick under the
    /// same clamped, energy-conserving rule. See thermal-model.md, Conduction.
    /// </summary>
    public static class ThermalBridges
    {
        private class Bridge
        {
            public ThermalBlock A;
            public ThermalBlock B;
            public float Conductance;
        }

        private static readonly List<Bridge> Bridges = new List<Bridge>();

        public static int Count
        {
            get { return Bridges.Count; }
        }

        /// <summary>
        /// Rebuilds the bridge for one mechanical block: dropped when it detaches, created
        /// against the head's grid when it attaches.
        /// </summary>
        public static void Rebuild(ThermalBlock baseBlock, IMyMechanicalConnectionBlock mechanical)
        {
            RemoveAllFor(baseBlock);

            if (mechanical == null || mechanical.Top == null) return;

            ThermalGrid topGrid = mechanical.Top.CubeGrid.GameLogic.GetAs<ThermalGrid>();
            if (topGrid == null) return;

            ThermalBlock top = topGrid.Get(mechanical.Top.SlimBlock.Min);
            if (top == null || top == baseBlock) return;

            float lattice = Math.Min(
                baseBlock.Grid.Model.GridSize,
                topGrid.Model.GridSize);

            // One cell face of contact, along the axis the head sits on: a rotor joint is a single
            // mounting plate whatever the size of the blocks either side.
            float conductance = ConductionBuilder.Conductance(
                lattice, baseBlock.Instance, top.Instance, 1, Face.Axis(Face.Up));

            if (conductance <= 0f) return;

            Bridges.Add(new Bridge { A = baseBlock, B = top, Conductance = conductance });
        }

        /// <summary>Drops every bridge touching a block. Called when the block goes away.</summary>
        public static void RemoveAllFor(ThermalBlock block)
        {
            for (int i = Bridges.Count - 1; i >= 0; i--)
            {
                if (Bridges[i].A == block || Bridges[i].B == block) Bridges.RemoveAt(i);
            }
        }

        public static void Clear()
        {
            Bridges.Clear();
        }

        /// <summary>
        /// Exchanges heat across every bridge. Called once per tick from the session, after the
        /// grids have stepped.
        /// </summary>
        public static void Update(float deltaSeconds)
        {
            if (Bridges.Count == 0 || deltaSeconds <= 0f) return;

            for (int i = Bridges.Count - 1; i >= 0; i--)
            {
                Bridge bridge = Bridges[i];

                ThermalNode a = bridge.A != null ? bridge.A.Node : null;
                ThermalNode b = bridge.B != null ? bridge.B.Node : null;
                if (a == null || b == null)
                {
                    Bridges.RemoveAt(i);
                    continue;
                }

                float difference = b.Temperature - a.Temperature;
                if (difference == 0f) continue;

                float watts = ThermalSolver.ClampExchange(
                    bridge.Conductance * difference, deltaSeconds, difference, a.ThermalMass, b.ThermalMass);

                float energy = watts * deltaSeconds;
                a.Temperature = Math.Max(ThermalConstants.MinimumTemperature, a.Temperature + (energy / a.ThermalMass));
                b.Temperature = Math.Max(ThermalConstants.MinimumTemperature, b.Temperature - (energy / b.ThermalMass));
            }
        }
    }
}
