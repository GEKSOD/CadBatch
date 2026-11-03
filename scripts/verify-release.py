from pathlib import Path
import hashlib,json,zipfile,xml.etree.ElementTree as E,re
root=Path(__file__).resolve().parents[1];v=E.parse(root/'Directory.Build.props').findtext('./PropertyGroup/Version')
release=root/'artifacts/release'/('CadBatch-'+v);downloads=root/('artifacts/downloads-'+v)
summary={'version':v,'targets':{}}
for framework,year in {'net48':'2023-2024','net8.0-windows':'2025-2026','net10.0-windows':'2027'}.items():
 source=root/'src/CadBatch.AutoCAD/bin/Release'/framework;dest=release/'CadBatch.bundle/Contents'/year
 for p in source.glob('*.dll'):
  assert p.read_bytes()==(dest/p.name).read_bytes(),p
  assert not re.match(r'(?i)(AcDbMgd|AcCoreMgd|AcMgd|AdWindows|AcWindows)',p.name)
 summary['targets'][year]=len(list(dest.glob('*.dll')))
manifest=E.parse(release/'CadBatch.bundle/PackageContents.xml').getroot();assert manifest.attrib['AppVersion']==v
assert all(x.attrib['LoadOnAutoCADStartup']=='True' for x in manifest.iter('ComponentEntry'))
config=json.loads((release/'App/CadBatch.runtimeconfig.json').read_text())['runtimeOptions'];assert 'includedFrameworks' in config and 'frameworks' not in config
assert all(x['version']=='10.0.14' for x in config['includedFrameworks'])
for line in (downloads/'SHA256SUMS.txt').read_text().splitlines():
 digest,name=line.split('  ',1);assert hashlib.sha256((downloads/name).read_bytes()).hexdigest()==digest,name
archive=downloads/('CadBatch-Portable-'+v+'.zip')
with zipfile.ZipFile(archive) as z:
 assert z.testzip() is None
 for name in z.namelist():
  assert not name.lower().endswith(('.dwg','.pdb')),name
  assert z.read(name)==(release/Path(*name.split('/')[1:])).read_bytes(),name
setup=downloads/('CadBatch-Setup-'+v+'.exe');assert setup.read_bytes()[:2]==b'MZ'
summary.update(selfContained=True,runtime='10.0.14',portableBytes=archive.stat().st_size,installerBytes=setup.stat().st_size,sha256=hashlib.sha256(setup.read_bytes()).hexdigest(),auditPassed=True)
(root/'artifacts/verification/release-0.4.0.json').write_text(json.dumps(summary,indent=2),encoding='utf-8');print(json.dumps(summary,indent=2))
