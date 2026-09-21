"""Independent checks for proxy occlusion and corpus estimator ownership."""
import unittest
import numpy as np
from corpus_render import raster, estimates

class CorpusRenderTests(unittest.TestCase):
# test foreground hides aligned rear box in both orders operation.
    def test_foreground_hides_aligned_rear_box_in_both_orders(self):
        rear={'min':[0,0,0],'max':[2,2,2]}
        front={'min':[10,7,12],'max':[12,9,14]}
        for nodes, expected in (([rear,front],1),([front,rear],0)):
            owner,shade=raster(nodes)
            self.assertTrue(np.all(owner[owner>=0]==expected))
            self.assertGreaterEqual(shade.min(),.88)
            self.assertLessEqual(shade.max(),1)

# test estimates keep each distinct block and peak operation.
    def test_estimates_keep_each_distinct_block_and_peak(self):
        nodes=[{'position':[x,0,0],'kelvin':300+x*10} for x in range(16)]
        fields,stats=estimates(nodes)
        np.testing.assert_array_equal(fields[0],fields[2])
        self.assertLessEqual(stats['directedCells'],512)
        reverse,_=estimates(list(reversed(nodes)))
        np.testing.assert_array_equal(fields[2],reverse[2][::-1])

if __name__=='__main__':unittest.main()

class ThermalSoftnessReferenceTests(unittest.TestCase):
# test normalization keeps constant temperature at silhouette edges operation.
    def test_normalization_keeps_constant_temperature_at_silhouette_edges(self):
        from soft_reference import blur
        values=np.full((31,31),600.);mask=np.zeros((31,31),bool);mask[8:23,8:23]=True
        np.testing.assert_allclose(blur(values,mask,3)[mask],600,atol=1e-8)

# test isolated hotspot stays centered and cooling is not replaced by max operation.
    def test_isolated_hotspot_stays_centered_and_cooling_is_not_replaced_by_max(self):
        from soft_reference import blur
        values=np.full((41,41),300.);values[20,20]=900;mask=np.ones_like(values,dtype=bool)
        smoothed=blur(values,mask,3)
        self.assertEqual(np.unravel_index(np.argmax(smoothed),smoothed.shape),(20,20))
        self.assertLess(smoothed.max(),900)
        values[:]=900;values[20,20]=300
        cooled=blur(values,mask,3)
        self.assertEqual(np.unravel_index(np.argmin(cooled),cooled.shape),(20,20))
