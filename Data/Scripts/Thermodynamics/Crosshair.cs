using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    /// <summary>
    /// The thermal block under the player's crosshair, resolved once for every readout that asks.
    ///
    /// The crosshair text and the extinguisher HUD each carried their own copy of this walk, and
    /// its two constants are a contract between them: the same reach and the same into-the-face
    /// nudge, or the two readouts describe different blocks while the player looks at one. Client
    /// side only — it reads the session camera, so a caller has already ruled out a dedicated
    /// server.
    /// </summary>
    public static class Crosshair
    {
        /// <summary>How far the readouts look, metres. Interaction range, not render range.</summary>
        public const double ReachMetres = 15;

        /// <summary>
        /// Metres past the hit surface the sampled point is nudged, along the view ray. A raycast
        /// hit sits exactly on the face, where rounding can land the cell on either side of it;
        /// the nudge puts the sample just inside the block that was hit.
        /// </summary>
        public const float IntoFaceMetres = 0.005f;

        /// <summary>What one resolution found, valid only when Resolve returned true.</summary>
        public struct Target
        {
            /// <summary>The grid's thermal adapter, with a live simulation.</summary>
            public ThermalGrid Thermals;

            /// <summary>The bound block under the crosshair, with a live node.</summary>
            public ThermalBlock Block;

            /// <summary>The grid cell the ray hit, for the per-cell readouts.</summary>
            public Vector3I Cell;

            /// <summary>The camera at the moment of the cast, for anything drawn relative to it.</summary>
            public MatrixD Camera;
        }

        /// <summary>
        /// False when the crosshair is not on a simulated block: no hit, a grid this mod is not
        /// simulating, or a cell bound to no node.
        /// </summary>
        public static bool Resolve(out Target target)
        {
            string reason;
            return Resolve(ReachMetres, out target, out reason);
        }

        /// <summary>Explicit diagnostic reach; normal readouts retain their interaction range.</summary>
        public static bool Resolve(double reachMetres, out Target target, out string reason)
        {
            reason = "no-hit";
            target = new Target();
            target.Camera = MyAPIGateway.Session.Camera.WorldMatrix;

            Vector3D start = target.Camera.Translation;
            Vector3D end = start + (target.Camera.Forward * reachMetres);

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(start, end, out hit);
            MyCubeGrid grid = hit == null ? null : hit.HitEntity as MyCubeGrid;
            if (grid == null) { reason = hit == null ? "no-hit" : "hit-not-grid"; return false; }

            ThermalGrid thermals = grid.GameLogic.GetAs<ThermalGrid>();
            if (thermals == null || thermals.Simulation == null) { reason = "grid-not-simulated"; return false; }

            Vector3I cell = grid.WorldToGridInteger(hit.Position + (target.Camera.Forward * IntoFaceMetres));
            ThermalBlock bound = thermals.GetAtCell(cell);
            if (bound == null || bound.Node == null) { reason = "hit-cell-no-temperature"; return false; }

            target.Thermals = thermals;
            target.Block = bound;
            target.Cell = cell;
            reason = "target-found";
            return true;
        }
    }
}
