# SPDX-License-Identifier: MIT
"""Sammelt alle PowerShell-Schritte aus .github/workflows/*.yml für die Syntaxprüfung (ps-syntax.ps1).

Hintergrund: Ein Release-Schritt lief nur bei Tags und brach dort zweimal an einem ParserError ab
(typografische Anführungszeichen in einem String, die PowerShell als Begrenzer liest). Die Prüfung läuft
deshalb bei jedem Push und PR für alle Schritte, auch für die, die nur bei Tags oder auf main laufen.
"""
import glob
import json
import sys

import yaml

out = []
paths = sorted(glob.glob(".github/workflows/*.yml") + glob.glob(".github/workflows/*.yaml"))
for path in paths:
    with open(path, encoding="utf-8") as f:
        doc = yaml.safe_load(f)
    default = ((doc.get("defaults") or {}).get("run") or {}).get("shell")
    for job_name, job in (doc.get("jobs") or {}).items():
        job_default = ((job.get("defaults") or {}).get("run") or {}).get("shell") or default
        # Windows-Runner (Standard-Shell pwsh), auch über eine Matrix wie runs-on: ${{ matrix.os }}
        runs_on = str(job.get("runs-on", "")).lower()
        windows = "windows" in runs_on
        if "matrix" in runs_on:
            # Werte der Matrix, die mit „windows“ beginnen (windows-latest, windows-2022 …)
            def values(x):
                if isinstance(x, dict):
                    for v in x.values():
                        yield from values(v)
                elif isinstance(x, list):
                    for v in x:
                        yield from values(v)
                else:
                    yield str(x).lower()
            windows = any(v.startswith("windows") for v in values((job.get("strategy") or {}).get("matrix") or {}))
        for i, step in enumerate(job.get("steps") or []):
            if "run" not in step:
                continue
            shell = str(step.get("shell") or job_default or ("pwsh" if windows else "bash"))
            # auch eigene Aufrufe wie "pwsh -command ... {0}"
            if shell.split()[0] in ("pwsh", "powershell"):
                out.append({"name": f"{path}:{job_name}:{step.get('name', i)}", "code": step["run"]})

json.dump(out, sys.stdout)
