using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public interface IBlockAdjacency
    {
/// <summary>Returns the neighbours.</summary>
        void GetNeighbours(BlockInstance block, List<BlockInstance> results);
    }
}
