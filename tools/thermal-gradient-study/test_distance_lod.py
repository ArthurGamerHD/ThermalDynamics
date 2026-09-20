"""Scene-wide geometry accounting and projected-size LOD policies."""
import unittest
from distance_lod import pixels_per_metre,desired_level,allocate,transition_cost,smooth_weight,coarse_nodes


class DistanceLodTests(unittest.TestCase):
    """Guard coverage, near priority, far reduction and transition accounting."""
    def levels(self):
        return [dict(triangles=1000,errorMetres=0),dict(triangles=400,errorMetres=0),dict(triangles=60,errorMetres=10),dict(triangles=6,errorMetres=100)]

    def test_projected_size_halves_at_double_distance(self):
        self.assertAlmostEqual(pixels_per_metre(100),2*pixels_per_metre(200))
        self.assertGreater(pixels_per_metre(100,height=2160),pixels_per_metre(100))

    def test_near_full_far_minimum(self):
        self.assertEqual(desired_level(self.levels(),50),0)
        self.assertEqual(desired_level(self.levels(),100000),3)

    def test_budget_reserves_far_grid_and_prefers_nearest(self):
        grids=[dict(distance=50,levels=self.levels()),dict(distance=70,levels=self.levels())]
        chosen=allocate(grids,budget=1100)
        self.assertEqual(chosen,[0,2])
        self.assertLessEqual(sum(g['levels'][i]['triangles'] for g,i in zip(grids,chosen)),1100)
        self.assertIsNone(allocate(grids,budget=11))

    def test_transition_counts_both_meshes_and_smooth_endpoints(self):
        grids=[dict(levels=self.levels())]
        self.assertEqual(transition_cost(grids,[0],[1]),1400)
        self.assertEqual(transition_cost(grids,[0],[0]),1000)
        self.assertEqual(smooth_weight(0),0)
        self.assertEqual(smooth_weight(.3),1)
        self.assertAlmostEqual(smooth_weight(.15),.5)

    def test_coarse_cells_keep_occupied_blocks_and_stable_bounds(self):
        nodes=[dict(min=[-3,0,0],max=[-2,1,1]),dict(min=[2,0,0],max=[3,1,1])]
        coarse=coarse_nodes(nodes,4)
        self.assertEqual(len(coarse),2)
        self.assertEqual(min(n['min'][0] for n in coarse),-3)
        self.assertEqual(max(n['max'][0] for n in coarse),3)


if __name__=='__main__':unittest.main()
