# SPDX-License-Identifier: MIT
"""Prüft, dass THIRD-PARTY-NOTICES.txt jede mitgelieferte Bibliothek nennt (Checkliste vor dem Release).

    python3 check-notices.py <Lupe.deps.json> <publish-ordner>

- Jedes NuGet-Paket aus der deps.json (außer der .NET-Laufzeit) steht mit Namen und Version in den Notices.
- Jede Datei, die ein solches Paket mitbringt (verwaltet und nativ), passt zu einem Dateinamen in den Notices
  (Platzhalter wie Avalonia*.dll erlaubt).
- Jede Datei im Publish-Ordner ist entweder Lupe selbst, Teil der .NET-Laufzeit oder in den Notices genannt – auch
  Dateien, die an der deps.json vorbei hineinkommen (z. B. die selbst gebaute zxing-cpp-Bibliothek).
Bei Einzeldatei-Veröffentlichungen liegt die deps.json unter obj/…/<rid>/Lupe.deps.json.
"""
import fnmatch
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# Lupes eigene Dateien und Beigaben (keine Drittanbieter)
OWN = ['Lupe', 'Lupe.exe', 'Lupe.dll', 'Lupe.*.dll', 'Lupe.deps.json', 'Lupe.runtimeconfig.json', 'libLupeCamera.dylib',
       'THIRD-PARTY-NOTICES.txt', 'LICENSE.txt', 'README.md', '*.resources.dll']


def main(deps_path, publish_dir):
    notices = open(os.path.join(REPO, 'THIRD-PARTY-NOTICES.txt'), encoding='utf-8').read()
    # Nur die Aufzählung vor den Lizenztexten zählt
    listing = notices[:notices.index('\nA. Apache License')]
    patterns = set(re.findall(r'[A-Za-z0-9_.*+-]+\.(?:dll|so|dylib)\b', listing))
    lower = listing.lower()

    deps = json.load(open(deps_path, encoding='utf-8-sig'))
    libraries = deps['libraries']
    runtime_files, problems = set(), []

    def covered(name):
        return any(fnmatch.fnmatch(name.lower(), p.lower()) for p in patterns)

    for target in deps['targets'].values():
        for key, info in target.items():
            name, version = key.split('/', 1)
            kind = libraries.get(key, {}).get('type')
            files = [os.path.basename(f) for section in ('runtime', 'native') for f in (info.get(section) or {})]
            if kind == 'project':
                continue
            if kind == 'runtimepack' and name.startswith('runtimepack.Microsoft.NETCore.App.Runtime'):
                runtime_files.update(files)  # .NET-Laufzeit: Abschnitt 4 der Notices
                continue
            plain = name.removeprefix('runtimepack.')
            if not re.search(r'(?<![\w.])' + re.escape(plain.lower()) + r'(?![\w])', lower):
                problems.append(f'Paket {plain} fehlt in THIRD-PARTY-NOTICES.txt')
            elif kind == 'package' and version not in listing:
                problems.append(f'Paket {plain}: Version {version} fehlt in THIRD-PARTY-NOTICES.txt')
            for f in files:
                if not f.endswith('.pdb') and not covered(f):
                    problems.append(f'{f} (aus {plain}) fehlt unter „Files:“ in THIRD-PARTY-NOTICES.txt')

    for root, dirs, names in os.walk(publish_dir):
        for f in names:
            if f in runtime_files or any(fnmatch.fnmatch(f, p) for p in OWN) or f.endswith('.pdb'):
                continue
            if not covered(f):
                problems.append(f'{os.path.relpath(os.path.join(root, f), publish_dir)} liegt im Paket, steht aber nicht in THIRD-PARTY-NOTICES.txt')

    for p in sorted(set(problems)):
        print('FEHLER: ' + p, file=sys.stderr)
    if problems:
        return 1
    print(f'THIRD-PARTY-NOTICES.txt vollständig für {publish_dir}')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1], sys.argv[2]))
