"""Checks geometric coverage and temperature fidelity of coplanar merging."""
import unittest
import numpy as np
from continuous_surface import mesh, fixture, render
from optimize_surface import optimize, optimize_best_order, remove_buried_faces, cull_occluded_triangles


class SurfaceOptimizationTests(unittest.TestCase):
    """Prevent saving geometry by removing silhouettes or flattening extrema."""
# check images operation.
    def check_images(self, nodes):
        source,_=mesh(nodes,.45)
        merged=optimize(source)
        a=render(source,nodes);b=render(merged,nodes)
        mask=np.isfinite(a)
        np.testing.assert_array_equal(mask,np.isfinite(b))
        self.assertLessEqual(np.max(np.abs(a[mask]-b[mask])),2.00001)
        self.assertLess(len(merged),len(source))
        return source,merged

# test constant plate collapses without changing heat operation.
    def test_constant_plate_collapses_without_changing_heat(self):
        nodes=fixture(False)['nodes']
        for node in nodes:node['kelvin']=400
        source,merged=self.check_images(nodes)
        self.assertEqual(len(merged),6) # three visible rectangular sides

# test hot and cooled extrema preserved operation.
    def test_hot_and_cooled_extrema_preserved(self):
        for cooled in (False,True):
            nodes=fixture(cooled)['nodes'];source,merged=self.check_images(nodes)
            a=render(source,nodes);b=render(merged,nodes)
            self.assertLess(abs(np.nanmax(a)-np.nanmax(b)),2.00001)

# test gaps are not bridged operation.
    def test_gaps_are_not_bridged(self):
        nodes=[{'min':[x,y,0],'max':[x+1,y+1,1],'kelvin':400} for x in (0,1,5,6) for y in range(4)]
        self.check_images(nodes)

# test best order keeps level four error bound operation.
    def test_best_order_keeps_level_four_error_bound(self):
        nodes=fixture(True)['nodes']
        original,_=mesh(nodes,.45)
        basic=optimize(original,20)
        best=optimize_best_order(original,20)
        self.assertLessEqual(len(best),len(basic))
        a=render(original,nodes);b=render(best,nodes)
        np.testing.assert_array_equal(np.isfinite(a),np.isfinite(b))
        self.assertLessEqual(np.nanmax(np.abs(a-b)),20.00001)

# test multiple small blocks cover large internal face operation.
    def test_multiple_small_blocks_cover_large_internal_face(self):
        nodes=[{'min':[0,0,0],'max':[1,2,2],'kelvin':500}]
        nodes += [{'min':[1,y,z],'max':[2,y+1,z+1],'kelvin':300} for y in range(2) for z in range(2)]
        original,_=mesh(nodes,.45)
        culled=remove_buried_faces(original,nodes)
        self.assertLess(len(culled),len(original))
        np.testing.assert_allclose(render(original,nodes),render(culled,nodes),equal_nan=True)

# test uncovered gap keeps face operation.
    def test_uncovered_gap_keeps_face(self):
        nodes=[{'min':[0,0,0],'max':[1,2,2],'kelvin':500},
               {'min':[1,0,0],'max':[2,1,1],'kelvin':300}]
        original,_=mesh(nodes,.45)
        culled=remove_buried_faces(original,nodes)
        np.testing.assert_allclose(render(original,nodes),render(culled,nodes),equal_nan=True)
        self.assertTrue(any(np.all(t[0][:,0]==.5) for t in culled))

# test occlusion preserves partial and coplanar surfaces operation.
    def test_occlusion_preserves_partial_and_coplanar_surfaces(self):
        xyz=np.array([[0.,0.,0.],[2.,0.,0.],[0.,2.,0.]])
        triangle=lambda p:(p,np.array([300.,400.,500.]),400.)
        back=triangle(xyz);front=triangle(xyz+[0,0,1])
        self.assertEqual(len(cull_occluded_triangles([back,front],(0,0,1))),1)
        self.assertEqual(len(cull_occluded_triangles([front,back],(0,0,1))),1)
        self.assertEqual(len(cull_occluded_triangles([back,triangle(xyz+[1,0,1])],(0,0,1))),2)
        self.assertEqual(len(cull_occluded_triangles([back,back],(0,0,1))),2)
        self.assertEqual(len(cull_occluded_triangles([back,front],(1,0,0))),2)

# test linear field can merge without error operation.
    def test_linear_field_can_merge_without_error(self):
        triangles=[]
        for x in range(10):
            q=np.array([[x,0,0],[x+1,0,0],[x+1,1,0],[x,1,0]],float)
            t=300+q[:,0]*10+q[:,1]*20
            for ids in ((0,1,2),(0,2,3)):triangles.append((q[list(ids)],t[list(ids)],0))
        result=optimize(triangles,tolerance=1e-8)
        self.assertEqual(len(result),2)


if __name__=='__main__':unittest.main()
