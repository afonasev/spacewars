#!/usr/bin/env python3
"""Decode OGD1 v1 fixed admitted original-group records; no runtime launch."""
import argparse,json,struct
from pathlib import Path
LONGS='tick sequence replacement group attempt entity request epoch quantum operation geometry profile total candidate occupancy direct proposed_total pending_sequence current_sequence active_sequence oracle_group'.split()
INTS='members member page raw preparation_page assignments prepared_keys historical_gate historical_cursor current_phase pending_count registered_count installed_count grant reason remaining preparation_member'.split()
def decode(data):
    header=struct.Struct('<5i2q?q')
    magic,version,slot,count,dropped,barrier,stop_after,stopped,frequency=header.unpack_from(data)
    if magic!=0x3144474f or version!=1 or slot!=4096 or count<0 or len(data)!=header.size+count*slot:raise ValueError('Invalid OGD1 header/length')
    rows=[]
    for index in range(count):
        at=header.size+index*slot;kind,root,committed,status,clock,thread=struct.unpack_from('<4i2q',data,at);at+=32
        r=dict(index=index,kind=kind,root=root,committed=committed,status=status,clock=clock,thread=thread)
        for name in LONGS:r[name]=struct.unpack_from('<q',data,at)[0];at+=8
        for name in INTS:r[name]=struct.unpack_from('<i',data,at)[0];at+=4
        for name in ['input','source','oracle','preparing']:
            n=struct.unpack_from('<i',data,at)[0];at+=4
            if n<0 or n>256 or at+n*4>header.size+(index+1)*slot:raise ValueError('Invalid OGD1 text')
            r[name]=''.join(chr(struct.unpack_from('<i',data,at+i*4)[0]) for i in range(n));at+=n*4
        if kind<1 or kind>12 or root<-1 or root>7 or committed not in (-1,0,1):raise ValueError('Invalid OGD1 disposition')
        rows.append(r)
    return dict(version=version,count=count,dropped=dropped,barrier=barrier,stop_after=stop_after,stopped=stopped,frequency=frequency,qualification='INVALID_OVERFLOW' if dropped else 'DECODED_ONLY_NOT_GAME_ACCEPTANCE',records=rows)
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('input',type=Path);p.add_argument('--output',type=Path,required=True);a=p.parse_args();result=decode(a.input.read_bytes());a.output.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({k:v for k,v in result.items() if k!='records'}))
if __name__=='__main__':main()
