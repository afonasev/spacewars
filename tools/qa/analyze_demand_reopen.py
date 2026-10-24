#!/usr/bin/env python3
"""Offline evidence only: exact dyadic sums of recorded actual-double edges.

This does not implement or rerun a solver. It follows recorded label revisions
and independently replays only the logged frontier key operations.
"""
import argparse
import collections
from fractions import Fraction
from functools import lru_cache
import json
import math
from pathlib import Path
import struct


@lru_cache(None)
def number(bits):
    value = struct.unpack('>d', bytes.fromhex(bits))[0]
    if not math.isfinite(value):
        raise ValueError('nonfinite recorded value')
    return value


@lru_cache(None)
def exact(bits):
    return Fraction.from_float(number(bits))


def bits(value):
    return struct.pack('>d', value).hex()


def fraction(value):
    return {'numerator': str(value.numerator), 'denominator': str(value.denominator),
            'approx': float(value)}


def describe(values):
    if not values:
        return {'count': 0}
    ordered = sorted(values)
    return {'count': len(ordered), 'min': float(ordered[0]),
            'median': float(ordered[len(ordered)//2]), 'max': float(ordered[-1])}


def analyze(path):
    labels = {}
    queue = set()
    latest = {}
    last_pop = {}
    counts = collections.Counter()
    categories = collections.Counter()
    attempts = {'G': collections.Counter(), 'R': collections.Counter()}
    samples = collections.defaultdict(list)
    improvements_ulp = collections.defaultdict(list)
    consistency_ulp = []
    repeated_consistency_ulp = []
    stored_decrease_ulps = collections.Counter()
    checks = collections.Counter()
    row_count = 0

    def label(rev):
        if rev not in labels:
            raise ValueError(f'missing parent/label revision {rev}')
        return labels[rev]

    def key(node, f):
        return number(f), node  # production Cost, Node, Candidate=0 ordering

    def edge_residual(parent, arc, child_h):
        residual = exact(parent['h']) - (exact(arc) + exact(child_h))
        scale = Fraction.from_float(math.ulp(number(parent['h'])))
        return residual, residual / scale

    with Path(path).open() as stream:
        for line in stream:
            r = json.loads(line)
            row_count += 1
            kind = r['k']
            counts[kind] += 1
            if kind in ('Q', 'D'):
                qkey = key(r['node'], r['f'])
                present = qkey in queue
                checks['remove_return_mismatch'] += present != r['removed']
                checks['observer_expected_present_mismatch'] += present != r['expectedPresent']
                if kind == 'Q':
                    checks['pop_not_minimum'] += not queue or min(queue) != qkey
                if r['removed']:
                    queue.discard(qkey)
                continue
            if kind in ('S', 'U'):
                rev = r['rev']
                if rev in labels or rev != len(labels)+1:
                    raise ValueError('noncanonical label revision')
                if kind == 'S':
                    path_sum = exact(r['seed'])
                    checks['seed_g_mismatch'] += r['g'] != r['seed']
                else:
                    parent = label(r['parent'])
                    if r['parent'] >= rev:
                        raise ValueError('noncausal parent revision')
                    checks['parent_node_mismatch'] += parent['node'] != r['from']
                    checks['parent_g_bits_mismatch'] += parent['g'] != r['fromG']
                    checks['actual_double_add_mismatch'] += bits(number(r['fromG']) + number(r['arc'])) != r['g']
                    checks['old_revision_binding_mismatch'] += latest.get(r['node'], 0) != r['oldRev']
                    if r['had']:
                        old = label(r['oldRev'])
                        checks['old_g_bits_mismatch'] += old['g'] != r['oldG']
                        checks['nondecreasing_successful_float_update'] += not (number(r['g']) < number(r['oldG']))
                    path_sum = parent['exact'] + exact(r['arc'])
                    residual, ulps = edge_residual(parent, r['arc'], r['h'])
                    if residual > 0:
                        consistency_ulp.append(ulps)
                qkey = key(r['node'], r['f'])
                checks['add_return_mismatch'] += (qkey not in queue) != r['added']
                if r['added']:
                    queue.add(qkey)
                labels[rev] = dict(r, exact=path_sum,
                                   accumulation_error=exact(r['g'])-path_sum)
                latest[r['node']] = rev
                continue
            if kind in ('G', 'R'):
                parent, old = label(r['parent']), label(r['oldRev'])
                checks['attempt_parent_g_mismatch'] += parent['g'] != r['fromG']
                checks['attempt_old_g_mismatch'] += old['g'] != r['oldG']
                checks['attempt_double_add_mismatch'] += bits(number(r['fromG'])+number(r['arc'])) != r['g']
                actual_improves = number(r['g']) < number(r['oldG'])
                checks['attempt_decision_mismatch'] += actual_improves != (not r['decision'] if kind == 'G' else r['decision'])
                difference = parent['exact'] + exact(r['arc']) - old['exact']
                category = ('accepted' if actual_improves else 'rejected') + (
                    '_exact_better' if difference < 0 else
                    '_exact_equal' if difference == 0 else '_exact_worse')
                attempts[kind][category] += 1
                continue
            if kind != 'P':
                raise ValueError(f'unknown row kind {kind}')
            current = label(r['rev'])
            checks['pop_g_bits_mismatch'] += r['g'] != current['g']
            checks['pop_queued_recomputed_f_mismatch'] += r['queuedF'] != r['computedF']
            checks['pop_label_f_mismatch'] += r['queuedF'] != current['f']
            checks['pop_h_mismatch'] += r['h'] != current['h']
            previous_rev = last_pop.get(r['node'], 0)
            checks['previous_pop_revision_mismatch'] += previous_rev != r['previousPopRev']
            last_pop[r['node']] = r['rev']
            if not previous_rev:
                continue
            previous = label(previous_rev)
            difference = current['exact'] - previous['exact']
            category = ('true_better_actual_double_arc_path' if difference < 0 else
                        'fp_false_equal_exact_path' if difference == 0 else
                        'fp_false_worse_exact_path')
            if r['rev'] == previous_rev:
                category = 'same_revision_repeat'
            categories[category] += 1
            checks['repeated_pop_non_decreasing_float_g'] += not (number(r['g']) < number(previous['g']))
            ulp = Fraction.from_float(math.ulp(number(previous['g'])))
            improvements_ulp[category].append(-difference/ulp)
            stored_decrease = (exact(previous['g'])-exact(r['g']))/ulp
            stored_decrease_ulps[str(stored_decrease)] += 1
            edge = None
            if current['k'] == 'U':
                parent = label(current['parent'])
                residual, residual_ulp = edge_residual(parent, current['arc'], current['h'])
                priority_drop = number(current['f']) < number(parent['f'])
                if residual > 0:
                    repeated_consistency_ulp.append(residual_ulp)
                    counts['repeated_latest_edge_positive_consistency_residual'] += 1
                if priority_drop:
                    counts['repeated_latest_edge_actual_priority_drop'] += 1
                edge = {'parent': current['parent'], 'from': current['from'],
                        'arc': current['arc'], 'hFrom': parent['h'], 'hTo': current['h'],
                        'consistencyResidual': fraction(residual),
                        'residualInHFromUlps': float(residual_ulp),
                        'parentQueuedF': parent['f'], 'childQueuedF': current['f'],
                        'actualPriorityDrop': priority_drop}
            if len(samples[category]) < 5:
                samples[category].append({'node': r['node'], 'eventId': r['eventId'],
                    'previousRev': previous_rev, 'currentRev': r['rev'],
                    'previousG': previous['g'], 'currentG': current['g'],
                    'exactNewMinusOld': fraction(difference),
                    'previousAccumulationError': fraction(previous['accumulation_error']),
                    'currentAccumulationError': fraction(current['accumulation_error']),
                    'latestEdge': edge})
    total_repeated = sum(categories.values())
    return {'schema': 'exact-demand-reopen-dyadic-v1', 'rows': row_count,
        'events': dict(counts), 'labels': len(labels), 'uniquePopped': len(last_pop),
        'repeatedPops': total_repeated, 'remainingFrontierKeys': len(queue),
        'repeatedPathClassification': {k: {'count': v, 'share': v/total_repeated}
                                       for k, v in categories.items()},
        'storedGDecreaseInPreviousGUlpsHistogram': dict(stored_decrease_ulps),
        'recordedAttemptsToExpanded': {k: dict(v) for k, v in attempts.items()},
        'exactImprovementInPreviousGUlps': {k: describe(v) for k, v in improvements_ulp.items()},
        'positiveConsistencyResidualInHFromUlpsAllUpdates': describe(consistency_ulp),
        'positiveConsistencyResidualInHFromUlpsRepeatedLatestEdges': describe(repeated_consistency_ulp),
        'checks': dict(checks), 'allRecordedBindingsAndQueueChecksPass': not any(checks.values()),
        'examples': dict(samples),
        'interpretation': [
            'Exact path=sum(recorded actual-double seed cost and actual-double arc weights), represented as rational dyadics. This does not substitute ideal real-geometry edge weights.',
            'Repeated classes compare each accepted revision with that node previous accepted Pop revision. Float g decrease is independently checked. Equal/worse exact path with smaller stored g is FP false improvement.',
            'Consistency and priority-drop flags concern the latest triggering edge and overlap the path classes. They are associations, not exclusive counterfactual causal shares.',
            'All frontier Add/Remove operations and popped minimum keys are independently replayed from event records. An absent old-key Remove=false after an earlier Pop is normal.',
            'G/R attempts are separate Graph guard and Connector commit stages, often the same arc; do not add them into a single unique-attempt count.',
            'No new solver execution, numeric tolerance, closed set, tie, weight, quota or policy is introduced by this analysis.'
        ]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('events', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    result = analyze(args.events)
    args.output.write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps({k: result[k] for k in ['rows', 'uniquePopped', 'repeatedPops',
          'repeatedPathClassification', 'allRecordedBindingsAndQueueChecksPass']}, indent=2))


if __name__ == '__main__':
    main()
