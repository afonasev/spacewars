"""Audit native numeric balance against the retained, normalized last web release."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def flatten(value, prefix=''):
    if isinstance(value, dict):
        for key, item in value.items():
            yield from flatten(item, f'{prefix}.{key}' if prefix else key)
    else:
        yield prefix, value


def audit():
    contract = json.loads((ROOT / 'unity/Assets/Spacewars/Content/BalanceLabContract/catalog.json').read_text())
    source = contract['source']
    assert hashlib.sha256(source['bytesUtf8'].encode()).hexdigest() == source['sha256']
    release = contract['releaseRef']
    revision = next(r for r in contract['revisions'] if r['ref']['id'] == release['id'] and r['ref']['revision'] == release['revision'])
    effective = dict(flatten(revision['effective']['profile']))
    native = json.loads((ROOT / 'unity/Assets/Spacewars/Content/Resources/PlayableProfile.json').read_text())
    mapping = json.loads((ROOT / 'docs/web-release-balance/mapping.json').read_text())
    assert release['revision'] == mapping['sourceRevision'] == native['sourceProfileRevision']
    assert release['id'] == native['sourceProfileId']
    assert source['sha256'] == native['sourceManifestSha256']
    fields = []
    failures = []
    for path, field in mapping['fields'].items():
        expected = effective[path]
        actual = native[field]
        exception = mapping['presentationExceptions'].get(path)
        fields.append(dict(path=path, nativeField=field, web=expected, numericWeb=expected if type(expected) in (int, float) else 0, native=actual, exception=exception))
        if not exception and expected != actual:
            failures.append(f'{path}: native {actual!r}, web {expected!r}')
    unmatched = {path: value for path, value in effective.items() if path not in mapping['fields'] and not path.startswith('entityText.') and path not in ('name', 'revision')}
    report = dict(releaseRef=release, sourceManifestSha256=source['sha256'], mappedFields=len(fields), fields=fields,
                  unmappedWebInputs=unmatched, failures=failures,
                  limitation='Unmapped inputs are explicitly unqualified: legacy map/runtime algorithms or presentation without a profile counterpart. Numerical equality does not prove identical engine behavior.')
    assert not failures, '\n'.join(failures)
    return report


if __name__ == '__main__':
    report = audit()
    path = ROOT / 'docs/web-release-balance/audit.json'
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    numeric = [f"{row['path']}\t{row['nativeField']}\t{row['web']}" for row in report['fields']
               if not row['exception'] and type(row['web']) in (int, float)]
    (path.parent / 'numeric-fields.tsv').write_text('\n'.join(numeric) + '\n')
    print(json.dumps(dict(status='PASS', mappedFields=report['mappedFields'], exceptions=3,
                          unmappedWebInputs=len(report['unmappedWebInputs']), report=str(path))))
