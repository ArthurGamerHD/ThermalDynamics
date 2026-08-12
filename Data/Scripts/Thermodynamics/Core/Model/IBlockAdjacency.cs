using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Answers "which blocks touch this one".
    ///
    /// This is the seam between the simulation and however the host stores its grid. The
    /// simulation needs the adjacency relation and nothing else — not a cell index, not a
    /// spatial tree, not the host's block type.
    ///
    /// <para>
    /// Both games already maintain a better spatial index than this mod would build.
    /// Space Engineers 1 stores a block per grid cell; Space Engineers 2 keeps a block octree
    /// that maintains its own face-connectivity graph and exposes it as
    /// <c>GetConnectedCubeBlocks</c> and <c>HasBlockConnection</c>. An adapter should forward to
    /// whichever exists rather than duplicating it.
    /// </para>
    ///
    /// <para>
    /// <see cref="GridModel"/> implements this over its own cell map, which is what the tests
    /// and the SE1 adapter use today.
    /// </para>
    /// </summary>
    public interface IBlockAdjacency
    {
        /// <summary>
        /// Appends every distinct block sharing at least some face area with
        /// <paramref name="block"/> to <paramref name="results"/>.
        ///
        /// The caller owns and clears the list, so an implementation can be allocation-free.
        /// A block is never its own neighbour, and no block appears twice however much area it
        /// shares.
        /// </summary>
        void GetNeighbours(BlockInstance block, List<BlockInstance> results);
    }
}
