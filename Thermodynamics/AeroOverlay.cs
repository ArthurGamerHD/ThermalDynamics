using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Thermodynamics.Core;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace Thermodynamics
{
    /// <summary>
    /// The aerodynamics debug view: the centre of mass the force is applied at, the drag, lift and
    /// relative-wind vectors on the ship being flown or looked at, and — when no force is being
    /// applied — the reason, named.
    ///
    /// <para>
    /// **The reason line is the point.** The force chain has six gates in series (`EnableDrag`,
    /// the server, friction being live, the shape term, `EnableLift`, the coefficients), three of
    /// which ship off, and a player who turns on the one called *Lift* sees nothing and cannot
    /// tell which of the other five ate it. This view computes the same vectors the server
    /// applies, from the same published sums (<see cref="ThermalSimulation.FrictionWatts"/>,
    /// <see cref="ThermalSolver.LastPressureWatts"/>, <see cref="ThermalGrid.LastState"/>), and
    /// prints what is missing when a vector is zero.
    /// </para>
    ///
    /// <para>
    /// Summed over the physical group and drawn at the group's mass-weighted centre, because that
    /// is exactly what <see cref="ThermalGridDrag"/> applies — one force at one point, and
    /// therefore never a torque. Client only, behind
    /// <see cref="Settings.DebugAeroOverlay"/>; reads state the simulation already publishes and
    /// writes nothing.
    /// </para>
    /// </summary>
    public static class AeroOverlay
    {
        /// <summary>
        /// The x-ray band: geometry scaled about the eye to just in front of the near plane, the
        /// same trick <see cref="ThermalDebugView"/> uses, so the centre of mass reads through the
        /// hull it sits inside.
        /// </summary>
        private const double BandScale = 0.02d;

        /// <summary>How far the crosshair looks for a grid when nothing is being flown, m.</summary>
        private const double PickRange = 300d;

        private static readonly MyStringId LineMaterial = MyStringId.GetOrCompute("Square");

        /// <summary>Scratch for the group's grids, so a frame allocates nothing.</summary>
        private static readonly List<IMyCubeGrid> GroupGrids = new List<IMyCubeGrid>();

        public static void Draw()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            if (Settings.Instance == null || !Settings.Instance.DebugAeroOverlay) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;

            ThermalGrid thermals = Target();
            if (thermals == null || thermals.Grid == null || thermals.Simulation == null) return;

            // The arrows show the force the server applies (same as ThermalGridDrag summation),
            // including the anchored-group veto which zeroes them.
            Vector3D centre;
            float mass;
            Vector3 drag;
            Vector3 lift;
            Vector3 worldWind;
            bool anchored;
            SumGroup(thermals, out centre, out mass, out drag, out lift, out worldWind, out anchored);
            if (mass <= 0f) return;

            Vector3D eye = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            double radius = thermals.Grid.PositionComp.WorldVolume.Radius;

            DrawCentreOfMass(ref centre, ref eye, thermals.Grid);
            DrawArrows(ref centre, ref eye, radius, ref drag, ref lift, ref worldWind);

            Vector3D pressureCentre;
            bool hasPressureCentre = CentreOfPressure(out pressureCentre);
            if (hasPressureCentre) DrawCentreOfPressure(ref pressureCentre, ref centre, ref eye, thermals.Grid);

            Report(thermals, ref drag, ref lift, ref worldWind, mass,
                hasPressureCentre ? Vector3D.Distance(pressureCentre, centre) : -1d, anchored);
        }

        /// <summary>
        /// The friction-watts-weighted centre of the group's windward surface, in world space —
        /// the centre of lift the intent page promises beside the centre of mass. The offset
        /// between the two is the torque arm the applied-at-centre-of-mass simplification drops,
        /// which is why a lift test that watches the nose never sees anything.
        ///
        /// <para>
        /// Weighted by per-node friction watts, which exist only under diagnostics — this view
        /// arms them through `RefreshDiagnosticsFlag`, so the marker appears one step after the
        /// view is switched on. False while nothing windward has watts.
        /// </para>
        /// </summary>
        private static bool CentreOfPressure(out Vector3D centre)
        {
            centre = Vector3D.Zero;
            Vector3D weighted = Vector3D.Zero;
            double total = 0d;

            for (int g = 0; g < GroupGrids.Count; g++)
            {
                IMyCubeGrid grid = GroupGrids[g];
                if (grid == null || grid.GameLogic == null) continue;

                ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
                if (thermals == null || thermals.Simulation == null) continue;

                ThermalSolver solver = thermals.Simulation.Solver;
                float gridSize = grid.GridSize;
                MatrixD world = grid.WorldMatrix;

                for (int i = 0; i < solver.Nodes.Count; i++)
                {
                    ThermalNode node = solver.Nodes[i];
                    float watts = node.LastFrictionWatts;
                    if (watts <= 0f) continue;

                    // Cell (0,0,0)'s centre is the grid's local origin, so a block's geometric
                    // centre is its cell-space midpoint less half a cell, in metres.
                    Vector3 centreCells =
                        ((Vector3)node.Block.Min + (Vector3)node.Block.MaxExclusive) * 0.5f;
                    Vector3D local = (Vector3D)((centreCells - new Vector3(0.5f)) * gridSize);

                    weighted += Vector3D.Transform(local, world) * watts;
                    total += watts;
                }
            }

            if (total <= 0d) return false;
            centre = weighted / total;
            return true;
        }

        /// <summary>
        /// The centre of pressure as an orange cross, with a line back to the centre of mass —
        /// the offset drawn as the thing it is, an arm.
        /// </summary>
        private static void DrawCentreOfPressure(ref Vector3D pressure, ref Vector3D mass,
            ref Vector3D eye, IMyCubeGrid grid)
        {
            const double armMetres = 1.5d;
            Vector4 orange = new Color(255, 160, 40).ToVector4();

            MatrixD world = grid.WorldMatrix;
            XRayLine(pressure - (world.Right * armMetres), pressure + (world.Right * armMetres), ref eye, ref orange, 0.06d);
            XRayLine(pressure - (world.Up * armMetres), pressure + (world.Up * armMetres), ref eye, ref orange, 0.06d);
            XRayLine(pressure - (world.Forward * armMetres), pressure + (world.Forward * armMetres), ref eye, ref orange, 0.06d);

            if (Vector3D.DistanceSquared(pressure, mass) > 0.01d)
            {
                XRayLine(mass, pressure, ref eye, ref orange, 0.04d);
            }
        }

        /// <summary>The grid being flown, or the one under the crosshair within <see cref="PickRange"/>.</summary>
        private static ThermalGrid Target()
        {
            IMyCubeBlock seat = MyAPIGateway.Session.ControlledObject as IMyCubeBlock;
            if (seat != null && seat.CubeGrid != null)
            {
                ThermalGrid flown = seat.CubeGrid.GameLogic == null
                    ? null : seat.CubeGrid.GameLogic.GetAs<ThermalGrid>();
                if (flown != null) return flown;
            }

            MatrixD camera = MyAPIGateway.Session.Camera.WorldMatrix;
            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(
                camera.Translation, camera.Translation + (camera.Forward * PickRange), out hit);

            MyCubeGrid grid = hit == null ? null : hit.HitEntity as MyCubeGrid;
            if (grid == null || grid.GameLogic == null) return null;
            return grid.GameLogic.GetAs<ThermalGrid>();
        }

        /// <summary>
        /// The group's mass-weighted centre and its summed drag and lift, mirroring
        /// <c>ThermalGridDrag.ApplyToGroupOf</c> without applying anything. The wind out-param is
        /// the leader's relative wind in world space, which is what the arrows are drawn against.
        /// </summary>
        private static void SumGroup(ThermalGrid leader, out Vector3D centre, out float mass,
            out Vector3 drag, out Vector3 lift, out Vector3 worldWind, out bool anchored)
        {
            IMyGridGroupData group = leader.Grid.GetGridGroup(GridLinkTypeEnum.Physical);

            GroupGrids.Clear();
            if (group != null) group.GetGrids(GroupGrids);
            if (GroupGrids.Count == 0) GroupGrids.Add(leader.Grid);

            // The leader's own relative wind, which is what the arrows are drawn against.
            EnvironmentState state = leader.LastState;
            worldWind = Vector3.TransformNormal(
                state.WindDirectionLocal * state.WindSpeed, leader.Grid.WorldMatrix);

            AeroGroupForces.Sum(GroupGrids, out centre, out mass, out drag, out lift);
            if (mass > 0f) centre /= mass;

            // The drag pass does nothing for anchored groups; arrows show the server-applied force
            // (second opinion vetoed), so anchored groups show only centre of mass and wind.
            anchored = AeroGroupForces.Anchored(GroupGrids);
            if (anchored)
            {
                drag = Vector3.Zero;
                lift = Vector3.Zero;
            }
        }

        /// <summary>
        /// A cross of three lines along the grid's own axes at the group's centre of mass, on the
        /// x-ray band so it reads inside the hull. Grid axes rather than world axes, so the marker
        /// rolls with the ship and reads as a point *of the ship*.
        /// </summary>
        private static void DrawCentreOfMass(ref Vector3D centre, ref Vector3D eye, IMyCubeGrid grid)
        {
            const double armMetres = 2.5d;
            Vector4 colour = Color.Yellow.ToVector4();

            MatrixD world = grid.WorldMatrix;
            XRayLine(centre - (world.Right * armMetres), centre + (world.Right * armMetres), ref eye, ref colour, 0.08d);
            XRayLine(centre - (world.Up * armMetres), centre + (world.Up * armMetres), ref eye, ref colour, 0.08d);
            XRayLine(centre - (world.Forward * armMetres), centre + (world.Forward * armMetres), ref eye, ref colour, 0.08d);
        }

        /// <summary>
        /// The three vectors from the centre of mass: relative wind in blue at a fixed length,
        /// drag in red and lift in green scaled together so the larger force spans the hull's
        /// radius — the two force arrows are comparable with each other by eye, and the printed
        /// newtons carry the absolute scale.
        /// </summary>
        private static void DrawArrows(ref Vector3D centre, ref Vector3D eye, double radius,
            ref Vector3 drag, ref Vector3 lift, ref Vector3 worldWind)
        {
            double reach = Math.Max(4d, radius * 0.75d);

            if (worldWind.LengthSquared() > 1e-4f)
            {
                Vector4 blue = new Color(90, 160, 255).ToVector4();
                Arrow(ref centre, Vector3D.Normalize((Vector3D)worldWind), reach * 0.6d, ref eye, ref blue);
            }

            float largest = Math.Max(drag.Length(), lift.Length());
            if (largest <= 0f) return;

            if (drag.LengthSquared() > 0f)
            {
                Vector4 red = new Color(240, 80, 60).ToVector4();
                Arrow(ref centre, Vector3D.Normalize((Vector3D)drag),
                    reach * (drag.Length() / largest), ref eye, ref red);
            }

            if (lift.LengthSquared() > 0f)
            {
                Vector4 green = new Color(90, 230, 110).ToVector4();
                Arrow(ref centre, Vector3D.Normalize((Vector3D)lift),
                    reach * (lift.Length() / largest), ref eye, ref green);
            }
        }

        /// <summary>One arrow from a point, on the x-ray band: a shaft and two head strokes swept toward the eye's plane.</summary>
        private static void Arrow(ref Vector3D from, Vector3D direction, double length,
            ref Vector3D eye, ref Vector4 colour)
        {
            if (length < 1d) length = 1d;
            Vector3D tip = from + (direction * length);

            XRayLine(from, tip, ref eye, ref colour, 0.12d);

            Vector3D toEye = eye - from;
            Vector3D sweep = Vector3D.Cross(direction, toEye);
            if (sweep.LengthSquared() < 1e-12d) return;
            sweep = Vector3D.Normalize(sweep);

            double head = length * 0.25d;
            Vector3D back = tip - (direction * head);
            XRayLine(tip, back + (sweep * head * 0.5d), ref eye, ref colour, 0.12d);
            XRayLine(tip, back - (sweep * head * 0.5d), ref eye, ref colour, 0.12d);
        }

        /// <summary>A world-space line scaled about the eye onto the band, so nothing occludes it.</summary>
        private static void XRayLine(Vector3D from, Vector3D to, ref Vector3D eye,
            ref Vector4 colour, double thickness)
        {
            Vector3D a = eye + ((from - eye) * BandScale);
            Vector3D b = eye + ((to - eye) * BandScale);

            Vector3D delta = b - a;
            double length = delta.Length();
            if (length <= 0d) return;

            MyTransparentGeometry.AddLineBillboard(
                LineMaterial, colour, a, (Vector3)(delta / length), (float)length,
                (float)(thickness * BandScale), BlendTypeEnum.PostPP);
        }

        /// <summary>
        /// The numbers, and the gates. One-frame notifications, reissued while the view is on, the
        /// way the crosshair readout works.
        /// </summary>
        private static void Report(ThermalGrid thermals, ref Vector3 drag, ref Vector3 lift,
            ref Vector3 worldWind, float mass, double pressureArm, bool anchored)
        {
            EnvironmentState state = thermals.LastState;
            float friction = thermals.Simulation.FrictionWatts;

            MyAPIGateway.Utilities.ShowNotification(
                "[Aero] wind: " + worldWind.Length().ToString("n1") + " m/s"
                + "  air: " + state.AirDensity.ToString("n3")
                + "  friction: " + (friction / 1000f).ToString("n1") + " kW"
                + "  drag: " + drag.Length().ToString("n0") + " N"
                + "  lift: " + lift.Length().ToString("n0") + " N"
                + "  mass: " + mass.ToString("n0") + " kg"
                + (pressureArm >= 0d
                    ? "  CoP offset: " + pressureArm.ToString("n1") + " m (no torque is applied from it)"
                    : ""), 1, "White");

            string blocked = Gates(thermals, ref worldWind, anchored);
            if (blocked.Length > 0)
            {
                MyAPIGateway.Utilities.ShowNotification("[Aero gates] " + blocked, 1, "Red");
            }

            ReportCrossover(ref worldWind, friction);
        }

        /// <summary>
        /// What the air is doing to the hull's temperature on net, and the speed where that flips.
        ///
        /// <para>
        /// **The crossover is emergent and belongs in a readout, not in a setting.** Friction adds
        /// `∝ v³`; convection removes `h·A·(T−T_ambient)` growing with `√v` — so where the two
        /// balance depends on how hot the hull already is, and no constant a world could author
        /// names it. This solves it for the hull as it stands: both terms are read off the current
        /// state (`k = friction/v³`; the convection sum from the per-node diagnostics this view
        /// arms), the √v growth is applied to the cooling side, and the flip speed is found by
        /// bisection. A hull at ambient has nothing to cool and reads "heats at any speed".
        /// </para>
        /// </summary>
        private static void ReportCrossover(ref Vector3 worldWind, float friction)
        {
            float speed = worldWind.Length();
            if (speed <= 0.5f || friction <= 0f)
            {
                return; // Nothing to extrapolate from while the air is still.
            }

            // Convective watts removed by wind (summed over nodes; negative while shedding). Friction re-summed over same group.
            // Balance covers same hull; solar/radiation excluded. Question: what does airspeed do?
            double removing = 0d;
            double adding = 0d;
            for (int g = 0; g < GroupGrids.Count; g++)
            {
                IMyCubeGrid grid = GroupGrids[g];
                if (grid == null || grid.GameLogic == null) continue;

                ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
                if (thermals == null || thermals.Simulation == null) continue;

                adding += thermals.Simulation.FrictionWatts;
                IList<ThermalNode> nodes = thermals.Simulation.Solver.Nodes;
                for (int i = 0; i < nodes.Count; i++) removing -= nodes[i].LastConvectionWatts;
            }

            friction = (float)adding;
            double net = friction - removing;
            string line = "[Aero net] air is "
                + (net >= 0d ? "heating" : "cooling")
                + " the hull by " + (Math.Abs(net) / 1000d).ToString("n1") + " kW";

            if (removing > 0d)
            {
                double crossover = CrossoverSpeed(friction, speed, (float)removing);
                line += crossover > 0d
                    ? "; flips at ~" + crossover.ToString("n0") + " m/s at this hull temperature"
                    : "; net heating at every speed at this hull temperature";
            }
            else
            {
                line += "; at ambient, so any airspeed heats";
            }

            MyAPIGateway.Utilities.ShowNotification(line, 1, "White");
        }

        /// <summary>
        /// The airspeed where `k·v³` heating meets `c·(1+s·√v)` cooling, both fitted to the
        /// current reading, or 0 when heating already exceeds cooling at every lower speed.
        /// Bisection over 0..2000 m/s; the answer is a debug figure, not a solver input.
        /// </summary>
        private static double CrossoverSpeed(float friction, float speed, float removing)
        {
            double k = friction / ((double)speed * speed * speed);

            // The √v bonus convection carries, undone from the current reading so the base rate
            // can be re-grown at any candidate speed.
            double s = EnvironmentSolver.WindConvectionScale;
            double baseRate = removing / (1d + (s * Math.Sqrt(speed)));

            Func<double, double> netAt = v => (k * v * v * v) - (baseRate * (1d + (s * Math.Sqrt(v))));

            if (netAt(0.1d) >= 0d) return 0d;

            double low = 0.1d, high = 2000d;
            if (netAt(high) < 0d) return 0d;

            for (int i = 0; i < 40; i++)
            {
                double mid = (low + high) * 0.5d;
                if (netAt(mid) < 0d) low = mid; else high = mid;
            }

            return (low + high) * 0.5d;
        }

        /// <summary>
        /// Every gate between the model and the ship's motion that is currently shut, in the order
        /// the chain tests them. Empty when force is flowing.
        /// </summary>
        private static string Gates(ThermalGrid thermals, ref Vector3 worldWind, bool anchored)
        {
            Settings settings = Settings.Instance;
            string blocked = "";

            if (anchored) blocked += "anchored: a static grid is in the group, so no force is applied; ";

            if (!settings.EnableFriction) blocked += "EnableFriction off; ";
            if (thermals.LastState.AirDensity <= 0.01f) blocked += "no atmosphere; ";

            float speed = worldWind.Length();
            if (speed <= 0f)
            {
                blocked += "no relative airspeed; ";
            }
            else if (settings.FrictionAtSpeedsAbove > 0f && speed <= settings.FrictionAtSpeedsAbove)
            {
                blocked += "airspeed " + speed.ToString("n0") + " <= FrictionAtSpeedsAbove floor "
                    + settings.FrictionAtSpeedsAbove.ToString("n0") + "; ";
            }

            if (!settings.EnableDrag) blocked += "EnableDrag off (this gate also stops lift); ";
            if (!settings.EnableShapeDrag) blocked += "EnableShapeDrag off (no pressure vector, so no lift); ";
            if (!settings.EnableLift) blocked += "EnableLift off; ";
            if (settings.EnableLift && settings.LiftCoefficient <= 0f) blocked += "LiftCoefficient 0; ";
            if (settings.DragCoefficient <= 0f) blocked += "DragCoefficient 0; ";
            if (settings.FrictionScale <= 0f) blocked += "FrictionScale 0; ";

            if (MyAPIGateway.Session != null && !MyAPIGateway.Session.IsServer)
            {
                blocked += "client of a server (forces apply server-side); ";
            }

            return blocked.TrimEnd(' ', ';');
        }
    }
}
