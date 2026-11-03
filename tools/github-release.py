"""Maintainer CLI. Authentication stays in Git Credential Manager, never in files or output."""
import json, os, subprocess, sys, urllib.request, urllib.error
from pathlib import Path

GIT=r'C:\Program Files\Git\cmd\git.exe'
REPO='Geksod/CadBatch'
def authentication():
    env=dict(os.environ,GIT_TERMINAL_PROMPT='0',GCM_INTERACTIVE='never')
    process=subprocess.run([GIT,'credential','fill'],input='protocol=https\nhost=github.com\nusername=GEKSOD\n\n',capture_output=True,text=True,env=env)
    if process.returncode: raise RuntimeError('GitHub credential is unavailable; sign in using Git Credential Manager.')
    fields=dict(row.split('=',1) for row in process.stdout.splitlines() if '=' in row)
    token=fields.get('password')
    if not token: raise RuntimeError('No GitHub credential returned.')
    return token

def api(token,path,method='GET',body=None,content_type='application/json'):
    url=path if path.startswith('https://uploads.github.com/') else 'https://api.github.com/'+path.lstrip('/')
    data=body if isinstance(body,bytes) else json.dumps(body).encode() if body is not None else None
    request=urllib.request.Request(url,data=data,method=method,headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28','User-Agent':'CadBatch-release','Content-Type':content_type})
    try:
        with urllib.request.urlopen(request,timeout=120) as response:return json.load(response)
    except urllib.error.HTTPError as ex:
        if ex.code==404 and method=='GET':return None
        # Never print request headers/credentials.
        detail=json.loads(ex.read().decode()).get('message','GitHub error')
        raise RuntimeError(str(ex.code)+': '+detail) from None

def main():
    token=authentication();identity=api(token,'user')
    if identity['login'].lower()!='geksod':raise RuntimeError('Authenticated account does not match Geksod.')
    cmd=sys.argv[1]
    if cmd=='check':
        repository=api(token,'repos/'+REPO)
        print(json.dumps({'login':identity['login'],'repositoryExists':bool(repository),'repository':repository['html_url'] if repository else None}))
    elif cmd=='create':
        repository=api(token,'repos/'+REPO)
        if repository:raise RuntimeError('Repository already exists; will not replace it automatically.')
        result=api(token,'user/repos','POST',{'name':'CadBatch','description':'Windows application for AutoCAD drawing management and batch PDF output.','private':False,'auto_init':False,'has_issues':True,'has_wiki':False})
        print(result['html_url'])
    elif cmd=='release':
        version=sys.argv[2];folder=Path(sys.argv[3]);notes=Path(sys.argv[4]).read_text(encoding='utf-8-sig')
        if api(token,'repos/'+REPO+'/releases/tags/v'+version):raise RuntimeError('Release already exists; refusing to replace release assets.')
        release=api(token,'repos/'+REPO+'/releases','POST',{'tag_name':'v'+version,'target_commitish':'main','name':'CAD Batch '+version,'body':notes,'draft':True,'prerelease':False})
        for file in sorted(folder.iterdir()):
            if not file.is_file():continue
            from urllib.parse import quote
            asset=api(token,release['upload_url'].split('{')[0]+'?name='+quote(file.name),'POST',file.read_bytes(),'application/octet-stream')
            print(json.dumps({'asset':asset['name'],'size':asset['size'],'digest':asset.get('digest')}))
        published=api(token,'repos/'+REPO+'/releases/'+str(release['id']),'PATCH',{'draft':False})
        print(published['html_url'])
    else:raise RuntimeError('Unknown command.')

if __name__=='__main__':main()
