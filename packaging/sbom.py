# SPDX-License-Identifier: MIT
"""SBOM (Stückliste) der Lupe-Pakete im Format CycloneDX 1.5 (JSON).

    python3 sbom.py <version> <Lupe.deps.json>... > Lupe-sbom.cdx.json

Bestandteile sind alle NuGet-Pakete aus den deps.json der Veröffentlichungen (Windows, Linux, macOS – zusammengeführt,
mit den Plattformen, auf denen sie mitkommen), die .NET-Laufzeit und die selbst gebaute zxing-cpp-Bibliothek
(packaging/zxing/versions.env). Lizenzen kommen aus den .nuspec-Dateien im NuGet-Zwischenspeicher, soweit vorhanden.
"""
import datetime
import json
import os
import re
import sys
import uuid

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NUGET = os.environ.get('NUGET_PACKAGES') or os.path.join(os.path.expanduser('~'), '.nuget', 'packages')


# Pakete, die ihre Lizenz nur als Datei mitbringen (geprüft, siehe THIRD-PARTY-NOTICES.txt)
LICENSE_FILES = {'Avalonia.Angle.Windows.Natives': 'BSD-3-Clause'}


def nuspec_license(name, version):
    """Lizenzausdruck aus der .nuspec im NuGet-Zwischenspeicher, sonst die Lizenz-URL, sonst None."""
    folder = os.path.join(NUGET, name.lower(), version.lower())
    try:
        spec = next(f for f in os.listdir(folder) if f.endswith('.nuspec'))
        text = open(os.path.join(folder, spec), encoding='utf-8-sig').read()
    except (OSError, StopIteration):
        return None
    m = re.search(r'<license type="expression">([^<]+)</license>', text)
    if m:
        return {'expression': m.group(1)}
    if '<license type="file">' in text and name in LICENSE_FILES:
        return {'expression': LICENSE_FILES[name]}
    m = re.search(r'<licenseUrl>([^<]+)</licenseUrl>', text)
    if m and 'deprecateLicenseUrl' not in m.group(1):
        return {'license': {'name': 'see URL', 'url': m.group(1)}}
    return None


def platform_of(rid):
    return 'windows' if rid.startswith('win') else 'macos' if rid.startswith('osx') else 'linux'


def main(version, deps_files):
    components = {}
    for path in deps_files:
        deps = json.load(open(path, encoding='utf-8-sig'))
        target = deps['runtimeTarget']['name']  # z. B. ".NETCoreApp,Version=v10.0/linux-x64"
        rid = target.split('/')[1] if '/' in target else ''
        for key, lib in deps['libraries'].items():
            name, ver = key.split('/', 1)
            if lib.get('type') == 'project':
                continue
            if name.startswith('runtimepack.'):
                name = name[len('runtimepack.'):]
                if name.startswith('Microsoft.NETCore.App.Runtime'):
                    name = 'Microsoft.NETCore.App.Runtime'  # .NET-Laufzeit, je RID ein Paket
            comp = components.setdefault((name, ver), {
                'type': 'library' if name != 'Microsoft.NETCore.App.Runtime' else 'framework',
                'bom-ref': f'pkg:nuget/{name}@{ver}',
                'name': name,
                'version': ver,
                'purl': f'pkg:nuget/{name}@{ver}',
                'platforms': set(),
            })
            comp['platforms'].add(platform_of(rid))
            if 'licenses' not in comp:
                lic = nuspec_license(name, ver) if not name.startswith('Microsoft.NETCore.App') else {'license': {'id': 'MIT'}}
                if lic:
                    comp['licenses'] = [lic]

    env = dict(line.split('=', 1) for line in open(os.path.join(REPO, 'packaging', 'zxing', 'versions.env'), encoding='utf-8').read().splitlines()
               if line and not line.startswith('#') and '=' in line)
    tag = env['ZXING_TAG']
    native = {
        'type': 'library', 'bom-ref': f'pkg:github/zxing-cpp/zxing-cpp@{tag}', 'name': 'zxing-cpp (native library)',
        'version': tag.lstrip('v'), 'purl': f'pkg:github/zxing-cpp/zxing-cpp@{tag}',
        'licenses': [{'expression': 'Apache-2.0 AND BSD-3-Clause'}],
        'description': f'ZXing.dll / libZXing.so / libZXing.dylib, built by Lupe CI from commit {env["ZXING_COMMIT"]} '
                       '(includes libzint and libzueci, BSD-3-Clause)',
        'platforms': {'windows', 'linux', 'macos'},
    }
    components[('zxing-cpp-native', tag)] = native

    out = []
    for comp in sorted(components.values(), key=lambda c: (c['name'].lower(), c['version'])):
        comp = dict(comp)
        comp['properties'] = [{'name': 'lupe:platforms', 'value': ','.join(sorted(comp.pop('platforms')))}]
        out.append(comp)
    bom = {
        'bomFormat': 'CycloneDX',
        'specVersion': '1.5',
        'serialNumber': f'urn:uuid:{uuid.uuid4()}',
        'version': 1,
        'metadata': {
            'timestamp': datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
            'component': {'type': 'application', 'bom-ref': 'lupe', 'name': 'Lupe', 'version': version,
                          'licenses': [{'license': {'id': 'MIT'}}],
                          'externalReferences': [{'type': 'vcs', 'url': 'https://github.com/ict-baden/lupe'}]},
        },
        'components': out,
        'dependencies': [{'ref': 'lupe', 'dependsOn': [c['bom-ref'] for c in out]}],
    }
    json.dump(bom, sys.stdout, indent=2, ensure_ascii=False)
    sys.stdout.write('\n')
    return 0


if __name__ == '__main__':
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    sys.exit(main(sys.argv[1], sys.argv[2:]))
