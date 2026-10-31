#!/usr/bin/env python3
"""Decode opt-in NAD1/schema2 diagnostic bytes; never launches the game."""
import argparse,collections,json,struct
from pathlib import Path
REASONS=['prepare_validity','placement_start','request_create','completion','historical_header','historical_member','historical_source','historical_geometry','page0_control','page0_region_compare','page0_predicate_compare','historical_endpoint','historical_route']
class Reader:
 def __init__(self,data):self.data=data;self.p=0
 def read(self,fmt):
  n=struct.calcsize('<'+fmt)
  if self.p+n>len(self.data):raise ValueError('Truncated diagnostic')
  v=struct.unpack_from('<'+fmt,self.data,self.p)[0];self.p+=n;return v
 def text(self):
  n=self.read('i')
  if not 0<=n<=256:raise ValueError('Invalid bounded text')
  return ''.join(chr(self.read('i')) for _ in range(n))
 def state(self):
  if not self.read('i'):return None
  d={k:self.text() for k in ['input','source','occupancy','provider','profile','preparing_key']}
  for k in ['group','command','request']:d[k]=self.read('q')
  for k in ['source_count','member_count']:d[k]=self.read('i')
  d['work_epoch']=self.read('q');d['remaining']=self.read('i')
  for k in ['allocation','candidate','occupancy_work','direct']:d[k]=self.read('q')
  for k in ['status','phase','member','page','candidate_cursor','occupant_cursor','preparation_cursor','preparation_page','prepared_keys','assignments','spatial_built','spatial_phase','spatial_x','spatial_z','spatial_next','spatial_min_z','spatial_max_x','spatial_max_z','spatial_fallback']:d[k]=self.read('i')
  d['spatial_radius_bits']=self.read('q');d['retention_blocked']=self.read('i');has=self.read('i')
  for k in ['historical_gate','historical_cursor','historical_member','historical_leg','historical_source_old','historical_preparation_member','historical_region_cursor','historical_removal_stage','historical_occupancy_stage']:d[k]=self.read('i')
  if has:
   o={'input':self.text()}
   for k in ['status','phase','member','page','preparation_cursor','preparation_page']:o[k]=self.read('i')
   o['preparing_key']=self.text();d['oracle']=o
  return d

def decode(path):
 r=Reader(path.read_bytes());assert r.read('i')==0x3144414e;assert r.read('i')==2
 capacity,count,dropped=(r.read('i') for _ in range(3));relevant=r.read('q');stopped=r.read('?');assert r.read('i')==len(REASONS)
 if not 0<=count<=capacity<=16384:raise ValueError('Invalid bounded record count')
 records=[]
 for index in range(count):
  start=r.p;n=r.read('i');assert 0<n<=4096
  a={'index':index,'kind':r.read('i'),'stamp':r.read('q'),'thread':r.read('i')}
  for k in ['service','attempt','epoch','quantum']:a[k]=r.read('q')
  a['grant']=r.read('i')
  for k in ['extra','old_extra','profiles']:a[k]=r.read('q')
  a['label']=r.text();a['state']=r.state()
  a['copied_oracle']={k:r.read('q') for k in ['allocation','candidate','occupancy','direct']}
  a['reasons']={k:r.read('q') for k in REASONS};assert r.p==start+n,(index,r.p-start,n)
  records.append(a)
 assert r.p==len(r.data),'Trailing diagnostic bytes'
 return {'capacity':capacity,'count':count,'dropped':dropped,'relevant_index':relevant,'stopped_after_event':stopped},records

def analyze(header,rows):
 services=collections.defaultdict(dict);keys=collections.defaultdict(set);births=[];transitions=[];markers=[]
 for a in rows:
  if a['kind'] in [1,2,3]:services[a['service']][a['kind']]=a
  elif a['kind']==10:keys[a['state']['input']].add(a['label'])
  elif a['kind']==11:births.append(a)
  elif a['kind']==20:transitions.append(a)
  elif a['kind']==21:markers.append(a)
 out={'header':header,'valid':header['dropped']==0,'errors':[],'services':len(services),'outcomes':dict(collections.Counter(a[3]['label'] for a in services.values() if 3 in a)),'attempted_reasons':collections.Counter(),'committed_reasons':collections.Counter(),'refused_reasons':collections.Counter(),'committed_allocation':0,'refused_allocation':0,'births':[],'transitions':transitions,'markers':markers}
 for sid,parts in services.items():
  if 1 not in parts or 3 not in parts:out['errors'].append(['incomplete_service',sid]);continue
  first,last=parts[1],parts[3];proposed=parts.get(2)
  if proposed:
   delta=proposed['state']['candidate']-first['state']['candidate'];n=sum(proposed['reasons'].values())
   if n!=delta:out['errors'].append(['candidate_reason_mismatch',sid,n,delta])
   out['attempted_reasons'].update(proposed['reasons']);target='committed_reasons' if last['label']=='committed' else 'refused_reasons';out[target].update(proposed['reasons'])
   delta=proposed['state']['allocation']-first['state']['allocation'];out['committed_allocation' if last['label']=='committed' else 'refused_allocation']+=delta
   if last['label']=='committed' and last['state']!=proposed['state']:out['errors'].append(['commit_differs_from_proposal',sid])
  if last['label']=='exception':out['errors'].append(['runtime_exception',sid])
 for a in births:
  d=a['state'];out['births'].append({'attempt':a['attempt'],'input':d['input'],'group':d['group'],'request':d['request'],'N':d['source_count'],'K':len(keys[d['input']]),'phase':d['phase'],'historical_gate':d['historical_gate'],'oracle':d.get('oracle')})
 out['last_by_attempt']={}
 for a in rows:
  if a['kind']==3:out['last_by_attempt'][a['attempt']]={'outcome':a['label'],'epoch':a['epoch'],'state':a['state']}
 out['valid']&=not out['errors'];return out

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('path',type=Path);a=p.parse_args();h,r=decode(a.path);out=analyze(h,r)
 a.path.with_suffix('.jsonl').write_text(''.join(json.dumps(x,separators=(',',':'))+'\n' for x in r));a.path.with_suffix('.summary.json').write_text(json.dumps(out,indent=2)+'\n')
 print(json.dumps({k:v for k,v in out.items() if k not in ['last_by_attempt','markers','transitions']},indent=2));return 0 if out['valid'] else 1
if __name__=='__main__':raise SystemExit(main())
