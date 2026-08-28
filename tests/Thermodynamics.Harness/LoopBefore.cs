using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **The loop's transport package as it stood before the transport line closed, kept so that
    /// what closing it bought can still be measured (`D8`).**
    ///
    /// <para>
    /// This was <c>LoopCandidate</c>: a proposal, three dials the labs applied to ask what a loop
    /// that could carry what a panel sheds would be worth. All three have shipped — the pumped
    /// coefficient and the stopped share at `C42`, the coolant's density at `C43` — so a class that
    /// went on calling itself a candidate would be a lab arm identical to the arm it is compared
    /// against, reporting *the package is worthless* in exactly the voice it would use if the
    /// package were worthless. That is this repository's recurring defect, so the class was
    /// inverted rather than deleted: the arms it feeds still exist and now hold the **before**.
    /// </para>
    ///
    /// <para>
    /// **Nothing here is what ships**, and the sense is the opposite of the one this file used to
    /// carry. See balance.md, *What the loop can be worth, and what it costs*.
    /// </para>
    /// </summary>
    public static class LoopBefore
    {
        /// <summary>
        /// Coolant a pipe block carried at any cell size, kg. **A flat charge with no grid size in
        /// it**: 3.2 kg/m³ in a 2.5 m cube, which is a gas, against 400 kg/m³ in a 0.5 m one, which
        /// is a liquid and outweighs the 32 kg pipe carrying it. `C43` replaced it with a density.
        /// </summary>
        public const float FlatKilogramsPerPipe = 50f;

        /// <summary>
        /// The fluid-to-wall coefficient before `C42`, W/(m²·K), applied whether or not anything
        /// circulated. At this value one sink face forced 6,400 K on a 6.4 MW block.
        /// </summary>
        public const float Coefficient = 160f;

        /// <summary>
        /// The stopped share before `C42`. One: the coefficient applied whole to a ring nothing was
        /// pushing, so a pump earned its power by mixing the fluid and not by making it conduct.
        /// </summary>
        public const float StagnantFraction = 1f;

        /// <summary>
        /// The package as one grid of the given cell size carried it. **The cell size is taken and
        /// ignored**, which is the defect being recorded rather than an oversight: a flat charge is
        /// the same on both grids, and a signature that hid that would hide the point.
        /// </summary>
        public static LoopThermalProperties For(float gridSize)
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();

            properties.CoolantMassPerPipe = FlatKilogramsPerPipe;
            properties.HeatTransferCoefficient = Coefficient;
            properties.StagnantTransferFraction = StagnantFraction;

            return properties.Clamp();
        }

        /// <summary>
        /// Applies it to every grid of a ship and rebuilds, so the loops are found again against the
        /// old numbers.
        /// </summary>
        public static void Apply(ShipAssembly assembly)
        {
            if (assembly == null) return;

            for (int i = 0; i < assembly.Simulations.Count; i++)
            {
                ThermalSimulation simulation = assembly.Simulations[i];
                simulation.LoopProperties = For(simulation.Grid.GridSize);
                simulation.RebuildAll();
            }
        }
    }
}
