from pathlib import Path
import hashlib,os,zipfile
root=Path(os.environ['CADBATCH_PACKAGE_ROOT']); destination=Path(os.environ['CADBATCH_PAYLOAD_DIR'])/'Payload.zip'
files=[p for base in ['App','CadBatch.bundle','docs'] for p in (root/base).rglob('*') if p.is_file()]+[root/'product.json']
rows=[hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.relative_to(root).as_posix() for p in files]
with zipfile.ZipFile(destination,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as output:
 for p in files: output.write(p,p.relative_to(root).as_posix())
 output.writestr('SHA256SUMS.txt','\n'.join(rows)+'\n')
print('Verified installer payload:',len(files),'files;',destination.stat().st_size,'bytes')
