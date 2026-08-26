using Thermodynamics.Core;

namespace Thermodynamics.Harness
{
    /// <summary>
    /// **The transport package, as a candidate rather than as a setting.**
    ///
    /// <para>
    /// Three measurements say the same thing: a radiator's emission is never what limits a fitted
    /// hull, and the joint between the heat and the panel is. A bolt joint carries about 174 kW in
    /// the reference rig and no surface dial moves it by a tenth; a coolant sink is the only path
    /// that does. So the dials worth turning are the loop's, and this is what turning them means,
    /// held in one place so a lab and a hull are measured against the same proposal (`P6`).
    /// </para>
    ///
    /// <para>
    /// **Two thirds of it shipped at `C42` and this is the third that did not.** The pumped
    /// coefficient and the stopped share are now the mod's own defaults, because the pickup is the
    /// only thing that decides whether a big block has an answer and at 160 it had none. What
    /// remains a proposal is the coolant's mass, which is a transport term rather than a pickup one:
    /// it changes how much of the ring's length is usable, not whether the heat gets in.
    /// </para>
    ///
    /// <para>
    /// **It is not what ships.** Nothing here is applied to `Cubes.xml`, `Loops.xml` or the shipped
    /// defaults; a lab asks for it explicitly. See balance.md, *What the loop can be worth, and what
    /// it costs*.
    /// </para>
    /// </summary>
    public static class LoopCandidate
    {
        /// <summary>
        /// Coolant per cubic metre of the cell a pipe occupies, kg/m³.
        ///
        /// <para>
        /// **The shipped figure is a flat 50 kg with no grid size in it** — 3.2 kg/m³ in a 2.5 m
        /// cube, which is a gas, against 400 kg/m³ in a 0.5 m one, which is a liquid. That is the
        /// same defect <see cref="LoopThermalProperties.HeatTransferCoefficient"/> was already
        /// corrected for, and it is authored for the grid the mod is least often plumbed on.
        /// </para>
        ///
        /// <para>
        /// 33 kg/m³ is a bore one fifth of the cell across running down the middle of it, filled
        /// with water-glycol at 1,050 kg/m³: `π/4 × (0.2g)² × g × 1050 / g³`. On a large grid that
        /// is 515 kg a pipe and on a small one 4.1 kg. It is also, independently, where the curve
        /// flattens — past about 500 kg on a large grid the extra coolant buys nothing.
        /// </para>
        /// </summary>
        public const float KilogramsPerCubicMetre = 33f;

        /// <summary>
        /// The package as one grid of the given cell size would carry it. **Grid size is the whole
        /// point**: a package that hands a small-grid pipe a large grid's coolant reproduces the
        /// defect it is meant to correct, in the opposite direction.
        /// </summary>
        public static LoopThermalProperties For(float gridSize)
        {
            LoopThermalProperties properties = LoopThermalProperties.Default();

            properties.CoolantMassPerPipe = KilogramsPerCubicMetre * gridSize * gridSize * gridSize;

            return properties.Clamp();
        }

        /// <summary>
        /// Applies it to every grid of a ship, each at its own cell size, and rebuilds so the loops
        /// are found again against the new numbers.
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
