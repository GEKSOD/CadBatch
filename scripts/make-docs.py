"""One source for the user guide: docs/Start.md. HTML (opened from the application) and TXT are built from it."""
from pathlib import Path
import html,sys

root=Path(__file__).resolve().parents[1]
version,target=sys.argv[1],Path(sys.argv[2])
target.mkdir(parents=True,exist_ok=True)
source=(root/'docs'/'Start.md').read_text(encoding='utf-8-sig')
lines=source.splitlines()
title=lines[0].lstrip('# ').strip() if lines and lines[0].startswith('#') else 'CAD Batch'
body='\n'.join(lines[1:]).strip()+'\n'
(target/'Start.md').write_text(source,encoding='utf-8')
(target/'Start.txt').write_text(body,encoding='utf-8-sig')
style='body{font:17px/1.65 "Segoe UI",sans-serif;color:#1d1d1f;background:#f5f5f7;margin:0}main{max-width:900px;margin:32px auto;padding:40px;background:#fff;border:1px solid #e5e5ea;border-radius:18px}pre{font:inherit;white-space:pre-wrap;overflow-wrap:anywhere}a{color:#0066cc}h1{font-size:30px;margin:0 0 6px}'
page=('<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
      '<title>'+html.escape(title)+'</title><style>'+style+'</style><main><h1>CAD Batch '+html.escape(version)+'</h1>'
      '<p><a href="https://github.com/Geksod/CadBatch/releases">Загрузки и обновления</a></p><pre>'+html.escape(body)+'</pre></main></html>\n')
(target/'Start.html').write_text(page,encoding='utf-8')
print('Guide built:',target)
