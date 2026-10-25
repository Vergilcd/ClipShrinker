from PIL import Image, ImageDraw
def draw(n):
    S=n*4                                   # supersample
    im=Image.new("RGBA",(S,S),(0,0,0,0)); d=ImageDraw.Draw(im)
    d.rounded_rectangle([S*0.03,S*0.03,S*0.97,S*0.97],radius=S*0.2,fill=(255,255,255,255),outline=(200,215,235,255),width=max(1,int(S*0.012)))
    blue=(47,128,237,255); t=S*0.095         # stroke thickness
    L,R,T,B=S*0.27,S*0.73,S*0.27,S*0.73     # crop frame
    ext=S*0.14
    # crop glyph: top-left bracket (vertical bar goes down from top-left, horizontal bar to right) + bottom-right bracket
    # vertical line at x=L from y=T-ext*... to B ; horizontal line at y=B from L to R+ext
    d.rectangle([L, S*0.13, L+t, B+t],fill=blue)           # left vertical (full)
    d.rectangle([L, B, R+ext*0.9, B+t],fill=blue)          # bottom horizontal (full)
    d.rectangle([R, T, R+t, B+t],fill=blue)                # right vertical (frame)
    d.rectangle([L, T, R+t, T+t],fill=blue)                # top horizontal (frame)
    # make it the classic crop: erase the parts that would make a closed frame, keep corner overlap look
    return im.resize((n,n),Image.LANCZOS)
