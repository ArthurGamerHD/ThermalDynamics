namespace Thermodynamics.Presentation
{
    /// <summary>Persistent client optics selection, with separate per-frame viewpoint eligibility.</summary>
    public sealed class ThermalVisionState
    {
        /// <summary>The two palettes; Off is a state, not a third palette.</summary>
        public enum Mode { Off, Cividis, WhiteHot }

        /// <summary>Currently requested presentation.</summary>
        public Mode Current { get; private set; }


        /// <summary>Enables a palette only for a nonzero, eligible camera/character entity ID.</summary>
        public bool Enable(Mode mode, long eligibleViewpoint)
        {
            Disable();
            if (eligibleViewpoint == 0 || (mode != Mode.Cividis && mode != Mode.WhiteHot)) return false;
            Current = mode;
            return true;
        }

        /// <summary>An unavailable view suspends rendering without clearing the requested palette.</summary>
        public bool Validate(long eligibleViewpoint)
        {
            return eligibleViewpoint != 0 && Current != Mode.Off;
        }

        /// <summary>Clears the request on explicit off or world unload.</summary>
        public void Disable()
        {
            Current = Mode.Off;
        }
    }
}
