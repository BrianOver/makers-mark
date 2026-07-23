import sys, trimesh, numpy as np
s = trimesh.load(sys.argv[1])
sc = s if isinstance(s, trimesh.Scene) else trimesh.Scene(s)
imgs=[]
for az in (0.0, 2.094, 4.188):
    R = trimesh.transformations.rotation_matrix(az, [0,1,0], sc.centroid)
    sc.camera_transform = sc.camera.look_at(sc.to_geometry().vertices, rotation=R)
    try:
        imgs.append(sc.save_image(resolution=(400,400)))
    except Exception as e:
        print("fail",e); sys.exit(1)
# stitch horizontally
from PIL import Image
import io
ims=[Image.open(io.BytesIO(b)).convert("RGBA") for b in imgs]
w=sum(i.width for i in ims); h=max(i.height for i in ims)
canvas=Image.new("RGBA",(w,h),(255,255,255,255)); x=0
for i in ims: canvas.paste(i,(x,0),i); x+=i.width
canvas.convert("RGB").save(sys.argv[2]); print("tex:OK")
