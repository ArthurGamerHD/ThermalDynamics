using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Answers "which blocks touch this one".
    ///
    /// The seam between the simulation and however the host stores its grid. The simulation needs
    /// the adjacency relation only — not a cell index, a spatial tree, or the host's block type.
    ///
    /// <para>
    /// Both games maintain a spatial index of their own. Space Engineers 1 stores a block per grid
    /// cell; Space Engineers 2 keeps a block octree with a face-connectivity graph exposed as
    /// <c>GetConnectedCubeBlocks</c> and <c>HasBlockConnection</c>. An adapter should forward to
    /// whichever exists rather than duplicating it.
    /// </para>
    ///
    /// <para>
    /// <see cref="GridModel"/> implements this over its own cell map, which the tests and the SE1
    /// adapter use.
    /// </para>
    /// </summary>
    public interface IBlockAdjacency
    {
        /// <summary>
        /// Appends every distinct block sharing at least some face area with
        /// <paramref name="block"/> to <paramref name="results"/>.
        ///
        /// The caller owns and clears the list, so an implementation can be allocation-free. A block
        /// is never its own neighbour, and no block appears twice however much area it shares.
        /// </summary>
        void GetNeighbours(BlockInstance block, List<BlockInstance> results);
    }
}
