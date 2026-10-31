"""Validate observation integrity only. Never infer game progress/arrival from a CSV."""
import csv
from pathlib import Path


COLUMNS = "kind,generation,sequence,group,attempt,entity,request,order,incarnation,revision,geometry,profile,epoch,quantum,grant,committed,reason,state_known,before_epoch,before_total,before_candidate_work,before_occupancy_work,before_direct_work,before_member,before_page,before_raw,before_candidate_cursor,before_occupancy_cursor,before_preparation_page,before_preparation_member,before_historical_gate,before_historical_cursor,before_phase,before_status,proposed_epoch,proposed_total,proposed_candidate_work,proposed_occupancy_work,proposed_direct_work,proposed_member,proposed_page,proposed_raw,proposed_candidate_cursor,proposed_occupancy_cursor,proposed_preparation_page,proposed_preparation_member,proposed_historical_gate,proposed_historical_cursor,proposed_phase,proposed_status".split(",")

def validate(path):
    try:
        lines = Path(path).read_text().splitlines()
        if not lines or not lines[-1].startswith('# '):
            return False
        meta = dict(field.split('=', 1) for field in lines[-1][2:].split(','))
        if int(meta['schema']) != 1 or int(meta['expected']) != 8 or int(meta['dropped']) != 0:
            return False
        if lines[0].split(',') != COLUMNS:
            return False
        rows = list(csv.DictReader(lines[:-1]))
        if not 8 <= len(rows) == int(meta['rows']) <= int(meta['limit']) <= 8192:
            return False
        if min(int(meta[k]) for k in ('filtered', 'preselection_unattributed', 'preselection_refused', 'observer_ticks')) < 0 or int(meta['frequency']) <= 0:
            return False
        registrations = [r for r in rows if r['kind'] == 'registration']
        bindings = {(int(r['generation']), int(r['sequence'])) for r in registrations}
        if len(registrations) != 8 or len(bindings) != 8 or len({g for g, _ in bindings}) != 1 or any(g < 1 or s < 1 for g, s in bindings):
            return False
        for row in rows:
            if set(row) != set(COLUMNS) or any(row[key] is None for key in COLUMNS):
                return False
            for key in COLUMNS:
                if key not in ('kind', 'reason'):
                    int(row[key])
            if (int(row['generation']), int(row['sequence'])) not in bindings:
                return False
            if row['kind'] not in ('registration', 'binding', 'birth', 'advance', 'retire'):
                return False
            if row['kind'] != 'registration' and (int(row['group']) <= 0 or int(row['attempt']) <= 0):
                return False
            if row['committed'] not in ('-1', '0', '1') or row['state_known'] not in ('0', '1') or not row['reason']:
                return False
            if row['committed'] == '-1' and (row['kind'] != 'advance' or row['reason'] != 'exception'):
                return False
            if row['kind'] == 'advance' and row['committed'] == '1' and (row['reason'] != 'committed' or row['state_known'] != '1'):
                return False
            # Proposed counters can increase on a discarded fork: no progress claim.
            if row['kind'] == 'advance':
                for key in ('epoch', 'quantum', 'grant', 'before_total', 'proposed_total'):
                    if int(row[key]) < 0:
                        return False
        return True
    except (OSError, ValueError, KeyError, TypeError, csv.Error):
        return False
