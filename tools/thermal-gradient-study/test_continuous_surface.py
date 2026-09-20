"""Independent checks for synthetic temperature fidelity, visibility and geometry limits."""
import unittest
import numpy as np
from continuous_surface import Field, fixture, mesh, faces, render


def block(x=0, kelvin=400):
    return {'min':[x,0,0], 'max':[x+1,1,1], 'kelvin':kelvin}


class ContinuousSurfaceTests(unittest.TestCase):
    """Guard thermal extrema, grid isolation, interpolation and depth ownership."""
    def test_constant_and_independent_grid_fields(self):
        points=np.array([[0,0,0],[100,0,0]])
        np.testing.assert_allclose(Field([block()]).sample(points,2),400)
        np.testing.assert_allclose(Field([block(kelvin=900)]).sample(points,2),900)

    def test_hotspot_and_cooled_patch_stay_at_source(self):
        points=np.array([[12,8,0],[15,8,0],[0,0,0]])
        hot=Field(fixture(False)['nodes']).sample(points,.45)
        cooled=Field(fixture(True)['nodes']).sample(points,.45)
        self.assertGreater(hot[0],hot[1])
        self.assertLess(cooled[0],cooled[1])
        self.assertLess(cooled[0],hot[0]-300)
        self.assertGreater(cooled[1],cooled[2]+200)

    def test_shared_face_removed_and_budget_overrun_reported(self):
        self.assertEqual(len(faces([block(),block(1)])),5)
        triangles,stats=mesh([block(),block(1,900)],.6,budget=10)
        self.assertEqual(len(triangles),10)
        self.assertFalse(stats['overBudget'])
        _,stats=mesh([block(),block(1,900)],.6,budget=8)
        self.assertTrue(stats['overBudget'])
        self.assertEqual(stats['baseTriangles'],10)

    def test_scalar_interpolation_and_depth_order(self):
        # Translate along the camera axis: same projected triangle, different depth.
        xyz=np.array([[0.,0.,0.],[1.,0.,0.],[0.,1.,0.]])
        near=(xyz+np.array([1.,.7,1.2]),np.array([300.,600.,900.]),600.)
        far=(xyz,np.array([100.,100.,100.]),100.)
        a=render([near,far],[block()])
        b=render([far,near],[block()])
        np.testing.assert_allclose(a,b,equal_nan=True)
        visible=a[np.isfinite(a)]
        self.assertGreater(len(visible),100)
        self.assertGreater(visible.min(),299)
        self.assertGreater(len(np.unique(visible.astype(int))),100)


if __name__=='__main__':
    unittest.main()
