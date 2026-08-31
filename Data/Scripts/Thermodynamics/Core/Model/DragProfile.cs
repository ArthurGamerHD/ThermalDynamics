using System;

namespace Thermodynamics.Core
{
    /// <summary>
    /// A block's directional drag, as a multiplier on each of its six exposed faces.
    ///
    /// <para>
    /// **The model has one shape term and it is a projected area, which cannot tell a jet engine
    /// from a box.** The solver keeps six per-face exposure fractions a block and weights them by
    /// their incidence against the wind — a Newtonian flat-plate projection, right in
    /// free-molecular hypersonic flow and crude everywhere a ship actually flies (backlog.md `K6`).
    /// A nacelle is slippery nose-on and blunt side-on, and nothing in an axis-aligned face count
    /// says so.
    /// </para>
    ///
    /// <para>
    /// **So a mod that knows the shape of its own block can say so, and the mechanism is a
    /// multiplier rather than a parallel model** (`K17`). Six numbers ride the exposure the solver
    /// already computes, which is why this costs a multiply on a row that is already being summed
    /// and why a registration cannot invent geometry the hull does not have.
    /// </para>
    ///
    /// <para>
    /// **A profile may only reduce, never add**, and that is the bound on what a registration may
    /// claim. A block's exposed faces are what its geometry gives it; a profile says *the air slips
    /// past this face more easily than its area suggests*, which is a statement about shape. Letting
    /// it exceed one would let a mod give a block more drag than it has surface, and a handling bug
    /// in somebody else's mod would look like one in this one.
    /// </para>
    /// </summary>
    public struct DragProfile
    {
        /// <summary>Per-face multipliers in <see cref="Face"/> order, each 0..1.</summary>
        private readonly float f0, f1, f2, f3, f4, f5;

        /// <summary>True when this profile is anything other than *no change*.</summary>
        public readonly bool IsSet;

        private DragProfile(float a, float b, float c, float d, float e, float f)
        {
            f0 = a; f1 = b; f2 = c; f3 = d; f4 = e; f5 = f;
            IsSet = true;
        }

        /// <summary>
        /// A profile from six multipliers, each clamped to 0..1.
        ///
        /// **Clamped rather than refused**, because `W4` says a registration must not throw into
        /// somebody else's update loop: a caller that passes 3 gets 1 and a caller that passes a
        /// NaN gets 1, which is *no change* — the direction that cannot break a ship.
        /// </summary>
        public static DragProfile Of(float a, float b, float c, float d, float e, float f)
        {
            return new DragProfile(Clamp(a), Clamp(b), Clamp(c), Clamp(d), Clamp(e), Clamp(f));
        }

        /// <summary>The multiplier for one face, or 1 where no profile is registered.</summary>
        public float this[int face]
        {
            get
            {
                if (!IsSet) return 1f;

                switch (face)
                {
                    case 0: return f0;
                    case 1: return f1;
                    case 2: return f2;
                    case 3: return f3;
                    case 4: return f4;
                    default: return f5;
                }
            }
        }

        /// <summary>
        /// 0..1, with anything unusable reading as 1.
        ///
        /// A NaN fails every comparison, so the order matters: the `>= 0` test rejects it and the
        /// fallback is *no change* rather than *no drag*. A profile that silently zeroed a face
        /// because a caller passed a NaN would remove drag rather than fail to add a claim.
        /// </summary>
        private static float Clamp(float value)
        {
            if (!(value >= 0f)) return 1f;
            return value > 1f ? 1f : value;
        }
    }
}
