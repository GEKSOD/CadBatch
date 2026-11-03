from pathlib import Path
import hashlib,os,zipfile
root=Path(os.environ['CADBATCH_PACKAGE_ROOT']);dest=Path(os.environ['CADBATCH_DOWNLOAD_DIR']);v=os.environ['CADBATCH_PACKAGE_VERSION']
archive=dest/('CadBatch-Portable-'+v+'.zip')
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as output:
 for p in root.rglob('*'):
  if p.is_file():output.write(p,'CadBatch-'+v+'/'+p.relative_to(root).as_posix())
files=[p for p in dest.iterdir() if p.is_file() and p.suffix in ['.exe','.zip']]
(dest/'SHA256SUMS.txt').write_text('\n'.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name for p in files)+'\n',encoding='utf-8')
print('Download assets:',', '.join(p.name for p in files))
