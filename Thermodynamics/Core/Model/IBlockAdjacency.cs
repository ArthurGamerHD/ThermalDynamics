using System.Collections.Generic;

namespace Thermodynamics.Core
{
    /// <summary>
    /// Answers "which blocks touch this one": the seam between the simulation and however the host
    /// stores its grid. Both games keep a spatial index of their own, so an adapter should forward to
    /// it rather than duplicate it; <see cref="GridModel"/> implements this over its cell map.
    /// See api.md, In-process extension.
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
