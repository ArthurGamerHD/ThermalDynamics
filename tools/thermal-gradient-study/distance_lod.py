"""Synthetic screen-error LOD and scene-budget allocation, not native model LOD access."""
import math
import itertools
import numpy as np


# pixels per metre operation.
def pixels_per_metre(distance,height=1080,fov_degrees=60):
    return height/(2*math.tan(math.radians(fov_degrees)/2)*max(distance,.1))


# coarse nodes operation.
def coarse_nodes(nodes,cell):
    """Stable grid-local cells. Clip outer cells to the original overall bounds."""
    occupied={}
    for n in nodes:
        lo=np.floor(np.asarray(n['min'])/cell).astype(int)
        hi=np.ceil(np.asarray(n['max'])/cell).astype(int)
        for key in itertools.product(*(range(a,b) for a,b in zip(lo,hi))):
            occupied[key]=True
    low=np.min([n['min'] for n in nodes],axis=0);high=np.max([n['max'] for n in nodes],axis=0)
    return [{'min':np.maximum(np.array(k)*cell,low).tolist(),
             'max':np.minimum((np.array(k)+1)*cell,high).tolist(),'kelvin':0.}
            for k in sorted(occupied)]


# desired level operation.
def desired_level(levels,distance,grid_metres=2.5,pixel_error=3.,height=1080,fov=60):
    ppm=pixels_per_metre(distance,height,fov)
    allowed=[i for i,l in enumerate(levels) if l['errorMetres']*ppm<=pixel_error]
    level=max(allowed,default=0)
    return 0 if levels[level]['errorMetres']==0 and grid_metres*ppm>=12 else level


# allocate operation.
def allocate(grids,budget=6000,pixel_error=3.):
    """Reserve visible-grid coverage first, then upgrade nearer grids first.

    Returns None when even minimum representations cannot fit; never silently
    drop a grid or pretend the budget was met. Inputs already viewport-culled.
    """
    selected=[len(g['levels'])-1 for g in grids]
    used=sum(g['levels'][i]['triangles'] for g,i in zip(grids,selected))
    if used>budget:return None
    for k in sorted(range(len(grids)),key=lambda k:grids[k]['distance']):
        g=grids[k];desired=desired_level(g['levels'],g['distance'],pixel_error=pixel_error)
        old=g['levels'][selected[k]]['triangles']
        for candidate in range(desired,len(g['levels'])):
            cost=g['levels'][candidate]['triangles']
            if used-old+cost<=budget:
                used+=cost-old;selected[k]=candidate;break
    return selected


# transition cost operation.
def transition_cost(grids,old,new):
    return sum(g['levels'][a]['triangles']+(g['levels'][b]['triangles'] if a!=b else 0)
               for g,a,b in zip(grids,old,new))


# smooth weight operation.
def smooth_weight(elapsed,duration=.3):
    t=max(0.,min(1.,elapsed/duration))
    return t*t*(3-2*t)
