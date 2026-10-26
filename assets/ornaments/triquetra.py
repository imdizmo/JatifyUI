import math
def circle3(p1,p2,p3):
    (ax,ay),(bx,by),(cx,cy)=p1,p2,p3
    d=2*(ax*(by-cy)+bx*(cy-ay)+cx*(ay-by))
    ux=((ax*ax+ay*ay)*(by-cy)+(bx*bx+by*by)*(cy-ay)+(cx*cx+cy*cy)*(ay-by))/d
    uy=((ax*ax+ay*ay)*(cx-bx)+(bx*bx+by*by)*(ax-cx)+(cx*cx+cy*cy)*(bx-ax))/d
    return (ux,uy), math.dist((ux,uy),p1)
def rot(p,a,o=(0,0)):
    c,s=math.cos(a),math.sin(a); x,y=p[0]-o[0],p[1]-o[1]; return (o[0]+x*c-y*s, o[1]+x*s+y*c)
# the reference, in its own pixels (y down): one arc from the top tip, round the left side and under the centre, to the bottom-right tip
O=(600,(20+1020+1020)/3)   # the centre of the three tips: the knot turns about this
A=[(600,20),(390,480),(530,850),(1180,1020)]
(cen,R)=circle3(A[0],A[1],A[3])
def arc(c,R,p_from,p_to,via,n=200):
    a0=math.atan2(p_from[1]-c[1],p_from[0]-c[0]); a1=math.atan2(p_to[1]-c[1],p_to[0]-c[0]); av=math.atan2(via[1]-c[1],via[0]-c[0])
    def norm(a): return (a-a0)%(2*math.pi)
    span=norm(a1); go=1 if norm(av)<span else -1
    if go<0: span=2*math.pi-span
    return [(c[0]+R*math.cos(a0+go*span*i/n), c[1]+R*math.sin(a0+go*span*i/n)) for i in range(n+1)]
arc0=arc(cen,R,A[0],A[3],A[1])
arcs=[arc0]+[[rot(p,k*2*math.pi/3,O) for p in arc0] for k in (1,2)]
# chain into one closed curve: each arc ends at the tip where the next (rotated by 120 deg) starts
curve=arcs[0][:-1]+arcs[1][:-1]+arcs[2][:-1]
def seg_x(p1,p2,p3,p4):
    (x1,y1),(x2,y2),(x3,y3),(x4,y4)=p1,p2,p3,p4
    d=(x2-x1)*(y4-y3)-(y2-y1)*(x4-x3)
    if abs(d)<1e-9: return None
    ua=((x3-x1)*(y4-y3)-(y3-y1)*(x4-x3))/d; ub=((x3-x1)*(y2-y1)-(y3-y1)*(x2-x1))/d
    return (ua,ub) if 0<=ua<1 and 0<=ub<1 else None
n=len(curve); xs=[]
for i in range(n):
    for j in range(i+3,n):
        if i==0 and j>=n-2: continue
        q=seg_x(curve[i],curve[(i+1)%n],curve[j],curve[(j+1)%n])
        if q: xs.append((i+q[0], j+q[1]))
# alternate along the traversal
events=sorted([(t,k,0) for k,(t,_) in enumerate(xs)]+[(t,k,1) for k,(_,t) in enumerate(xs)])
over={}
for idx,(t,k,side) in enumerate(events): over[(k,side)]=(idx%2==0)
