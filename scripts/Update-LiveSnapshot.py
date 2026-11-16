"""Refresh public repository facts on demand; unchanged facts keep their source time."""
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[1]
def git(*args):
    return subprocess.check_output(['git', '-C', str(root), *args], encoding='utf-8').strip()

files = git('ls-files').splitlines()
groups = dict(docs_files=[], code_files=[], config_files=[])
chars = dict(docs_files=0, code_files=0)
lines = 0
for name in files:
    path = root / name
    kind = ('docs_files' if path.suffix == '.md' else 'code_files' if path.suffix in {'.cs', '.ps1', '.py', '.xaml'}
            else 'config_files' if path.suffix in {'.csproj', '.slnx', '.manifest'} or path.name in {'.gitattributes', '.gitignore'} else None)
    if kind is None:
        continue
    groups[kind].append(dict(text=name, state_text='已跟踪'))
    if kind in chars:
        text = path.read_text(encoding='utf-8-sig')
        chars[kind] += sum(not c.isspace() for c in text)
        if kind == 'code_files': lines += len(text.splitlines())
artifacts = []
for alias, name, title in [('desktop', 'assets/verdant-rain-4k.png', '雨林黑猫 · 电脑壁纸 4K'), ('mobile', 'assets/mobile-wallpaper/emerald-elf-1440x3200.png', '翠幕精灵 · 手机壁纸')]:
    if (root / name).is_file():
        artifacts.append(dict(at=git('log', '-1', '--format=%cI', '--', name), kind='image', title=title, status='completed', public='same',
                              open=dict(source='emerald.artifact.'+alias, href='https://github.com/wlyaaaaa/emerald-veil/blob/main/'+name)))
value = dict(v=1, scope='按需统计 Git 跟踪的自有文档、源码和配置；不含生成物及第三方',
             metrics={**{k:len(v) for k,v in groups.items()}, 'tracked_files':len(files)}, records=groups,
             size=dict(docs_chars=chars['docs_files'], code_lines=lines, code_chars=chars['code_files']), artifacts=artifacts)
output = root / 'docs/live.snapshot.json'
old = json.loads(output.read_text(encoding='utf-8')) if output.exists() else {}
old.pop('at', None)
old.get('size', {}).pop('at', None)
if old != value:
    value['at'] = value['size']['at'] = datetime.now(timezone.utc).isoformat()
    temporary = output.with_suffix('.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    temporary.replace(output)
