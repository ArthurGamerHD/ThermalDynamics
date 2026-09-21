namespace Thermodynamics.Presentation
{
    public sealed class ThermalVisionState
    {
        public enum Mode { Off, Cividis, WhiteHot }

        public Mode Current { get; private set; }


/// <summary>Enable operation.</summary>
        public bool Enable(Mode mode, long eligibleViewpoint)
        {
            Disable();
            if (eligibleViewpoint == 0 || (mode != Mode.Cividis && mode != Mode.WhiteHot)) return false;
            Current = mode;
            return true;
        }

/// <summary>Validate operation.</summary>
        public bool Validate(long eligibleViewpoint)
        {
            return eligibleViewpoint != 0 && Current != Mode.Off;
        }

/// <summary>Disable operation.</summary>
        public void Disable()
        {
            Current = Mode.Off;
        }
    }
}
