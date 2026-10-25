#!/usr/bin/env python3
"""Read upstream contracts and report native UI drift; never edit upstream code."""
import argparse,hashlib,json,re,sys,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'launcher/windows/BloodborneLauncher.Native'
ORIGINAL=ROOT/'launcher/windows/BloodborneLauncher'
VIEWS=['GraphicsPage.cs','DisplayPage.cs','PlayPage.cs','ControlsPage.cs','TweaksPage.cs','CheatsPage.cs','OnlinePage.cs','TrophiesPage.cs','SettingsPage.cs','MorePage.cs','ModsPage.cs','OutputPage.cs']
def links(project):
 return sorted({Path(p).name for item in ET.parse(project/'BloodborneLauncher.Native.csproj').iter('Compile') for p in item.get('Include','').split(';') if '/Core/' in p})
KEY=re.compile(r'^(?:effect_|graphics_|exp_|show_fps|hdr|gpu_)[a-z0-9_]*$|^(?:fps_mode|display_device|output_res|live_resolution|fullscreen|widescreen|present_mode|upscaler|preset|render_scale|sharpen|sharpness|mip_bias|object_motion|frame_gen(?:_.*)?|dlss_.*|reflex|compile_note|model_lod|debug_menu)$')
STRING=re.compile(r'"((?:\\.|[^"\\])*)"')
def literals(text):
 out=[]
 for value in STRING.findall(text):
  try:out.append(json.loads('"'+value+'"'))
  except ValueError:pass
 return out
def calls(source):
 for m in re.finditer(r'\b(?:IniCombo|IniToggle|SettingCombo|SettingToggle|ComboRow|ToggleRow|IniSlider|SliderRow)\s*\(',source):
  at=m.end();depth=1;quote=False;escape=False
  for end in range(at,len(source)):
   c=source[end]
   if quote:
    if escape:escape=False
    elif c=='\\':escape=True
    elif c=='"':quote=False
   elif c=='"':quote=True
   elif c=='(':depth+=1
   elif c==')':
    depth-=1
    if not depth:yield source[at:end];break

def audit(root=ROOT):
 original=root/'launcher/windows/BloodborneLauncher';project=root/'launcher/windows/BloodborneLauncher.Native';names=links(project);missing=[str(original/'Core'/p) for p in names if not (original/'Core'/p).exists()]
 if missing:raise RuntimeError('Missing source-linked contracts: '+', '.join(missing))
 source='\n'.join((original/'Views'/p).read_text(encoding='utf-8-sig') for p in ['GraphicsPage.cs','DisplayPage.cs'])
 native='\n'.join(p.read_text(encoding='utf-8') for p in (project/'Backend').glob('*.cs'))
 original_keys={s for s in literals(source) if KEY.fullmatch(s)}
 native_keys={s for s in literals(native) if KEY.fullmatch(s)}
 effects=set(re.findall(r'new\("([a-z_]+)",', (original/'Core/Choices.cs').read_text(encoding='utf-8-sig').split('Effect[] Effects',1)[-1].split('Choice[] CameraTuning',1)[0]))-{'skip_intro','debug_camera'}
 expected={'gpu_id','gpu_name'} # NativeWindow owns the lazy Vulkan selector and paired name.
 help_text={}
 for call in calls(source):
  strings=literals(call);keys=[s for s in strings if KEY.fullmatch(s)];hint=next((s for s in strings[1:] if len(s)>28 and ' ' in s),None)
  if hint:
   for key in keys:help_text[key]=hint
 files={f'Core/{name}':hashlib.sha256((original/'Core'/name).read_bytes()).hexdigest() for name in names}
 return {'format':2,'upstream_views':{f'Views/{name}':hashlib.sha256((original/'Views'/name).read_bytes()).hexdigest() for name in VIEWS},'linked_sources':files,'upstream_graphics_keys':sorted(original_keys),'automatically_linked_effects':sorted(effects),'unmapped_keys':sorted(original_keys-native_keys-effects-expected),'help':help_text,'launch_contract':'Original launcher --play; no duplicate engine preparation code'}
def main():
 p=argparse.ArgumentParser();p.add_argument('--root',type=Path,default=ROOT);p.add_argument('--output',type=Path);p.add_argument('--strict',action='store_true');a=p.parse_args()
 try:report=audit(a.root)
 except (OSError,RuntimeError) as error:print(error,file=sys.stderr);return 2
 if a.output and a.output.exists():
  previous=json.loads(a.output.read_text(encoding='utf-8'));changed=[name for group in ['linked_sources','upstream_views'] for name,digest in report[group].items() if previous.get(group,{}).get(name)!=digest]
  if changed:print('Changed upstream contracts/views to review:',', '.join(changed))
 if a.output:a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
 print('Native source links verified:',len(report['linked_sources']))
 if report['unmapped_keys']:print('Review new or unmapped graphics keys:',', '.join(report['unmapped_keys']));return 1 if a.strict else 0
 print('Native graphics key coverage matches the current source.');return 0
if __name__=='__main__':raise SystemExit(main())
