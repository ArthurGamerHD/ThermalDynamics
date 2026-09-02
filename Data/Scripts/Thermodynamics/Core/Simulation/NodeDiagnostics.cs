namespace Thermodynamics.Core
{
    /// <summary>
    /// What one node's last step is attributed to, per mechanism, in watts.
    ///
    /// <para>
    /// **Only ever written inside the solver's `CollectDiagnostics` branch**, which the crosshair
    /// readout, the block overlay and the telemetry report turn on and the shipped configuration
    /// leaves off. A node holds one of these once something has recorded a non-zero figure for it
    /// and holds null before that, which is what keeps the fields off the hundreds of thousands of
    /// nodes in a world that never asks for them. See <see cref="ThermalNode.Diagnostics"/>.
    /// </para>
    ///
    /// <para>
    /// Fields rather than properties, and one object rather than seven: the solver writes five of
    /// them in a row on the environment pass and accumulates two of them per link end, and this is
    /// the object it writes into.
    /// </para>
    /// </summary>
    public sealed class NodeDiagnostics
    {
        /// <summary>Watts this node exchanged with its neighbours by conduction.</summary>
        public float Conduction;

        /// <summary>Watts radiated to, or taken from, the sky.</summary>
        public float Radiation;

        /// <summary>Watts exchanged with the atmosphere around the grid.</summary>
        public float Convection;

        /// <summary>Watts of sunlight landing on this node's faces.</summary>
        public float Solar;

        /// <summary>Watts of atmospheric friction heating this node.</summary>
        public float Friction;

        /// <summary>Watts from mod-registered point heat sources.</summary>
        public float HeatSource;

        /// <summary>Watts exchanged with the air of the rooms this node faces.</summary>
        public float Room;
    }
}
