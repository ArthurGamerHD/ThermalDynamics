"""Render exported production patch geometry; input JSON and output PNG paths are arguments."""
import json
import sys
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
examples=json.load(open(sys.argv[1]))
fig,axes=plt.subplots(len(examples),2,figsize=(10,len(examples)*3.6),layout='constrained',facecolor='#0c1219')
q=(np.arange(400)+.5)/100
x,y=np.meshgrid(q,q)
for row,example in enumerate(examples):
    outputs=[]
    for col,key in enumerate(('baseline','optimized')):
        values=np.full_like(x,np.nan)
        for p in example[key]:
            a,c=p['a'],p['c'];t=p['t']
            u=(x-a[0])/(c[0]-a[0]);v=(y-a[1])/(c[1]-a[1])
            inside=(u>=0)&(u<=1)&(v>=0)&(v<=1)
            estimate=np.where(u>=v,t[0]*(1-u)+t[1]*(u-v)+t[2]*v,t[0]*(1-v)+t[2]*u+t[3]*(v-u))
            if p.get('alternate',False):
                estimate=np.where(u+v<=1,t[0]*(1-u-v)+t[1]*u+t[3]*v,t[1]*(1-v)+t[2]*(u+v-1)+t[3]*(1-u))
            values[inside]=estimate[inside]
        assert np.isfinite(values).all()
        outputs.append(values)
        ax=axes[row,col]
        ax.imshow(values,cmap='cividis',vmin=300,vmax=800,origin='lower',extent=(0,4,0,4))
        ax.set_xticks([]);ax.set_yticks([])
        title=('Current splits' if col==0 else 'Selective minimum-patch splits')
        ax.set_title(f"{example['name'].replace('-',' ').title()} · {title}\n{len(example[key])*2} triangles",color='white',fontsize=11)
    print(example['name'],'maximum raster difference K:',float(np.max(np.abs(outputs[0]-outputs[1]))))
fig.suptitle('Same temperature tolerance · production patch geometry\nSynthetic surfaces, 300–800 K',color='white',fontsize=15)
fig.savefig(sys.argv[2],dpi=140,facecolor=fig.get_facecolor())
