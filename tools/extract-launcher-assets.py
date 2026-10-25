#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Export the launcher's local interface assets from the user's Bloodborne dump.
Requires Pillow and fonttools. Does not modify or redistribute the game files.
"""
from pathlib import Path
import struct,io,zlib,argparse,json,hashlib
from PIL import Image
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
class Bits:
 def __init__(self,b,off=0):self.b=b;self.p=off*8
 def u(self,n):
  v=0
  for _ in range(n):v=(v<<1)|((self.b[self.p//8]>>(7-self.p%8))&1);self.p+=1
  return v
 def s(self,n):
  v=self.u(n);return v-(1<<n) if v&(1<<(n-1)) else v
 def align(self):self.p=(self.p+7)//8*8
 def rect(self):
  n=self.u(5);v=[self.s(n) for _ in range(4)];self.align();return v

def export_font(source,destination):
 b=source.read_bytes();off=8+(5+4*(b[8]>>3)+7)//8+4
 while off<len(b):
  t=struct.unpack_from('<H',b,off)[0];off+=2;size=t&63;code=t>>6
  if size==63:size=struct.unpack_from('<I',b,off)[0];off+=4
  if code==75:font=b[off:off+size];break
  off+=size
 flags=font[2];name_len=font[4];original=font[5:5+name_len].decode('utf8');count=struct.unpack_from('<H',font,5+name_len)[0];base=7+name_len;wide=bool(flags&8);fmt='<I' if wide else '<H';stride=4 if wide else 2
 offsets=[struct.unpack_from(fmt,font,base+i*stride)[0] for i in range(count)];codesoff=struct.unpack_from(fmt,font,base+count*stride)[0]+base;codefmt='<H' if flags&4 else '<B';codesize=2 if flags&4 else 1;codes=[struct.unpack_from(codefmt,font,codesoff+i*codesize)[0] for i in range(count)]
 print('Font',original,'glyphs',count,'layout',bool(flags&128))
 def glyph(data):
  bits=Bits(data);fill=bits.u(4);line=bits.u(4);pen=TTGlyphPen(None);x=y=0;opened=False
  def ensure():
   nonlocal opened
   if not opened:pen.moveTo((round(x/20),round(-y/20)));opened=True
  while True:
   edge=bits.u(1)
   if not edge:
    style=bits.u(5)
    if not style:break
    if style&1:
     if opened:pen.closePath();opened=False
     n=bits.u(5);x=bits.s(n);y=bits.s(n)
    if style&2:bits.u(fill)
    if style&4:bits.u(fill)
    if style&8:bits.u(line)
    assert not style&16,'Unexpected new styles in glyph'
   else:
    ensure();straight=bits.u(1);n=bits.u(4)+2
    if straight:
     if bits.u(1):x+=bits.s(n);y+=bits.s(n)
     elif bits.u(1):y+=bits.s(n)
     else:x+=bits.s(n)
     pen.lineTo((round(x/20),round(-y/20)))
    else:
     cx=x+bits.s(n);cy=y+bits.s(n);x=cx+bits.s(n);y=cy+bits.s(n)
     pen.qCurveTo((round(cx/20),round(-cy/20)),(round(x/20),round(-y/20)))
  if opened:pen.closePath()
  return pen.glyph()
 glyphs={'.notdef':TTGlyphPen(None).glyph()};order=['.notdef'];cmap={};advances=[10240]*count;asc=16400;desc=4080
 if flags&128:
  at=codesoff+count*codesize;asc,desc,lead=struct.unpack_from('<hhh',font,at);at+=6;advances=list(struct.unpack_from('<'+'h'*count,font,at))
 for i,o in enumerate(offsets):
  name='g'+str(i);order.append(name);glyphs[name]=glyph(font[base+o:]);cmap[codes[i]]=name
 metrics={'.notdef':(512,0)}
 for i,name in enumerate(order[1:]):
  g=glyphs[name]
  if g.numberOfContours:g.recalcBounds(None)
  metrics[name]=(max(0,round(advances[i]/20)),getattr(g,'xMin',0))
 fb=FontBuilder(1024,isTTF=True);fb.setupGlyphOrder(order);fb.setupCharacterMap(cmap);fb.setupGlyf(glyphs);fb.setupHorizontalMetrics(metrics);fb.setupHorizontalHeader(ascent=round(asc/20),descent=-abs(round(desc/20)));fb.setupNameTable({'familyName':'Bloodborne Menu Local','styleName':'Regular','uniqueFontIdentifier':'LocalBloodborneMenu','fullName':'Bloodborne Menu Local','psName':'BloodborneMenuLocal'});fb.setupOS2(sTypoAscender=round(asc/20),sTypoDescender=-abs(round(desc/20)),usWinAscent=max(1024,round(asc/20)),usWinDescent=max(256,abs(round(desc/20))));fb.setupPost();fb.setupMaxp();out=destination;out.mkdir(parents=True,exist_ok=True);fb.save(out/'BloodborneMenu.ttf')
 from fontTools.ttLib import TTFont
 f=TTFont(out/'BloodborneMenu.ttf');assert len(f.getGlyphOrder())==count+1
 print('Converted',len(f.getBestCmap()),'Unicode glyphs, bytes',(out/'BloodborneMenu.ttf').stat().st_size)


def unpack_texture(path,wanted):
 b=path.read_bytes()
 if b[:4]!=b'TPF\0':
  if b[:4]!=b'DCX\0' or b'DFLT' not in b[:64]:raise ValueError('Expected Bloodborne DFLT/DCX texture package: '+str(path))
  for offset in range(64,128):
   try:b=zlib.decompress(b[offset:]);break
   except zlib.error:continue
  else:raise ValueError('Could not unpack texture package: '+str(path))
  if b[:4]!=b'TPF\0':raise ValueError('Decompressed file is not a TPF')
 if b[12]!=4:raise ValueError('Expected PS4 texture package')
 result={}
 for i in range(struct.unpack_from('<I',b,8)[0]):
  at=16+i*36;off,size=struct.unpack_from('<II',b,at);w,h=struct.unpack_from('<HH',b,at+12);dxgi=struct.unpack_from('<I',b,at+32)[0];no=struct.unpack_from('<I',b,at+24)[0];end=no
  while b[end:end+2]!=b'\0\0':end+=2
  name=b[no:end].decode('utf-16-le')
  if name not in wanted:continue
  if dxgi not in (71,98):raise ValueError('Unsupported menu texture format: '+str(dxgi))
  stride=8 if dxgi==71 else 16;bw=(w+3)//4;bh=(h+3)//4;src=b[off:off+size];linear=bytearray(bw*bh*stride)
  for y in range(bh):
   for x in range(bw):
    morton=sum(((x>>j)&1)<<(2*j) | ((y>>j)&1)<<(2*j+1) for j in range(3))
    index=((y//8)*((bw+7)//8)+x//8)*64+morton
    block=src[index*stride:(index+1)*stride]
    if len(block)!=stride:raise ValueError('Truncated menu texture')
    linear[(y*bw+x)*stride:(y*bw+x+1)*stride]=block
  header=[124,0x81007,h,w,len(linear),0,1]+[0]*11+[32,4,struct.unpack('<I',b'DX10')[0],0,0,0,0,0]+[0x1000,0,0,0,0]
  result[name]=Image.open(io.BytesIO(b'DDS '+struct.pack('<31I',*header)+struct.pack('<5I',dxgi,3,0,1,0)+linear)).convert('RGBA')
 return result

def overlay(image):
 # Scaleform draws these black-backed highlights additively. Convert their
 # premultiplied colour to straight-alpha PNG for WPF's source-over blending.
 rgba=image.convert('RGBA');pixels=[]
 raw=rgba.tobytes()
 for r,g,b,_ in zip(raw[0::4],raw[1::4],raw[2::4],raw[3::4]):
  alpha=max(r,g,b)
  pixels.append((round(r*255/alpha),round(g*255/alpha),round(b*255/alpha),alpha) if alpha else (0,0,0,0))
 rgba.putdata(pixels);return rgba

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('game',type=Path,help='Folder containing eboot.bin and dvdroot_ps4');p.add_argument('--out',type=Path,default=Path('gui'));a=p.parse_args()
 game=a.game/'dvdroot_ps4' if (a.game/'dvdroot_ps4').is_dir() else a.game
 menu=game/'menu';out=a.out;out.mkdir(parents=True,exist_ok=True)
 titles=unpack_texture(menu/'title.tpf.dcx',{'MENU_Title_00001','MENU_Title_00100'});title=titles['MENU_Title_00001'];common=unpack_texture(menu/'common.tpf.dcx',{'MENU_Common_00001','MENU_Common_00003'})
 title.crop((0,0,1920,1024)).save(out/'background.png')
 icon=titles['MENU_Title_00100'];icon.save(out/'game-icon.png')
 # One PNG-backed entry keeps the original pixels instead of baking in resized artwork.
 payload=(out/'game-icon.png').read_bytes()
 (out/'game-icon.ico').write_bytes(struct.pack('<HHHBBBBHHII',0,1,1,icon.width,icon.height,0,0,1,32,len(payload),22)+payload)
 overlay(common['MENU_Common_00003'].crop((0,50,480,108))).save(out/'menu-frame.png')
 overlay(common['MENU_Common_00003'].crop((0,230,184,250))).save(out/'menu-underline.png')
 overlay(common['MENU_Common_00003'].crop((7,813,367,921))).save(out/'menu-glow-half.png')
 font=next((menu/n/'font.gfx' for n in ['spaes','engus','enggb','spaar'] if (menu/n/'font.gfx').is_file()),None)
 if font is None:raise FileNotFoundError('No supported menu/<locale>/font.gfx found')
 export_font(font,out)
 manifest={n.name:hashlib.sha256(n.read_bytes()).hexdigest() for n in out.iterdir() if n.suffix.lower() in ('.png','.ttf','.ico')}
 (out/'assets-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
 print('Local launcher assets exported to:',out.resolve())
if __name__=='__main__':main()
