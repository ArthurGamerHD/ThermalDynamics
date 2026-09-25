using System.Collections.Generic;

namespace Thermodynamics.Core
{
    public interface IBlockAdjacency
    {

        void GetNeighbours(BlockInstance block, List<BlockInstance> results);
    }
}
