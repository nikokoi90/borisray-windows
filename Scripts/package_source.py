#!/usr/bin/env python3
"""Package committed client source and the exact unmodified upstream core source."""
import argparse
import io
from pathlib import Path
import subprocess
import tarfile
import zipfile

CORE_COMMIT = '7054cac5124465ffdb145870d7c01885566711b1'
parser = argparse.ArgumentParser()
parser.add_argument('--core-source', type=Path, required=True, help='Local SagerNet/sing-box Git checkout containing the pinned commit')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
upstream_commit = subprocess.check_output(['git', '-C', str(args.core_source), 'rev-parse', 'v1.14.3^{commit}'], text=True).strip()
if upstream_commit != CORE_COMMIT:
    raise SystemExit('Upstream tag differs from the pinned release commit')
args.output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
    for folder, commit, prefix in [(root, 'HEAD', 'borisray-windows/'), (args.core_source, CORE_COMMIT, 'upstream/sing-box/')]:
        archive = subprocess.check_output(['git', '-C', str(folder), 'archive', '--format=tar', commit])
        with tarfile.open(fileobj=io.BytesIO(archive)) as source:
            for member in source:
                if not member.isfile():
                    continue
                if Path(member.name).suffix.lower() in {'.p12', '.pfx', '.jks', '.keystore', '.key', '.secret'}:
                    raise SystemExit('Private-looking source filename: ' + member.name)
                output.writestr(prefix + member.name, source.extractfile(member).read())
    output.writestr('SOURCE.txt', 'BorisRay client source and unmodified sing-box v1.14.3 source.\nCore commit: ' + CORE_COMMIT + '\nBuild the Windows client with borisray-windows/Scripts/build.ps1; it fetches the official pinned core binary.\nCore build: upstream/sing-box/.github/workflows/build.yml, cmd/internal/build and go.mod. Dependencies and exact revisions: go.mod/go.sum.\nUpstream source: https://github.com/SagerNet/sing-box/tree/v1.14.3\nNo subscription, family administrator credential or signing key is included.\n')
print(args.output, args.output.stat().st_size)
