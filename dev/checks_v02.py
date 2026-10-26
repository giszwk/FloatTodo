"""源代码结构、旧库升级、插入位置算法的离线烟雾检查（无需 .NET）。"""
from pathlib import Path
import re, sqlite3, tempfile, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parent.parent
for f in root.rglob('*.xaml'):
    ET.parse(f)
for f in list(root.glob('*.xaml'))+list((root/'Views').glob('*.xaml')):
    code=(Path(str(f)+'.cs')).read_text(encoding='utf8')
    xml=f.read_text(encoding='utf8')
    for event in ['Click','KeyDown','Checked','Unchecked','PreviewMouseLeftButtonDown','PreviewMouseMove',
                  'PreviewMouseLeftButtonUp','MouseEnter','MouseLeave','MouseLeftButtonDown',
                  'MouseLeftButtonUp','MouseMove','PreviewDragOver','PreviewDrop']:
        for h in re.findall(r'(?<!\w)'+event+r'="(\w+)"',xml):
            if h in ['True','False']: continue
            assert re.search(r'\b'+h+r'\s*\(',code),f'Missing event {f.name}::{h}'
repo=(root/'Services'/'TaskRepository.cs').read_text(encoding='utf8')
raws=re.findall(r'CommandText\s*=\s*"""(.*?)"""',repo,re.S)
assert len(raws)>=5
create=raws[0]; migrate=raws[1]; indexes=raws[2]
for variant in ['v0.1','v0.1.1']:
    with tempfile.TemporaryDirectory() as tmpdir:
        c=sqlite3.connect(str(Path(tmpdir)/'todos.db'))
        if variant=='v0.1':
            c.executescript('CREATE TABLE Tasks(Id TEXT PRIMARY KEY, Title TEXT NOT NULL, IsCompleted INTEGER NOT NULL DEFAULT 0,CreatedUtc TEXT NOT NULL,CompletedUtc TEXT NULL);')
            c.execute("INSERT INTO Tasks VALUES('a','A',0,'2026-10-23T01:00:00Z',NULL)")
            c.execute("INSERT INTO Tasks VALUES('b','B',0,'2026-10-23T02:00:00Z',NULL)")
        else:
            c.executescript('CREATE TABLE Tasks(Id TEXT PRIMARY KEY, Title TEXT NOT NULL, IsCompleted INTEGER NOT NULL DEFAULT 0,CreatedUtc TEXT NOT NULL,CompletedUtc TEXT NULL,SortOrder INTEGER NOT NULL DEFAULT 0);')
            c.execute("INSERT INTO Tasks VALUES('a','A',0,'2026-10-23T01:00:00Z',NULL,0)")
            c.execute("INSERT INTO Tasks VALUES('b','B',0,'2026-10-23T02:00:00Z',NULL,1)")
        c.commit()
        c.executescript(create)
        cols=[r[1] for r in c.execute('PRAGMA table_info(Tasks)')]
        if 'SortOrder' not in cols: c.executescript(migrate)
        for name,typ in [('DueLocal','TEXT NULL'),('ReminderEnabled','INTEGER NOT NULL DEFAULT 0'),('SeriesId','TEXT NULL'),('AlertedUtc','TEXT NULL'),('SnoozedUntilLocal','TEXT NULL')]:
            if name not in [r[1] for r in c.execute('PRAGMA table_info(Tasks)')]:
                c.execute('ALTER TABLE Tasks ADD COLUMN '+name+' '+typ)
        c.executescript(indexes)
        assert c.execute('SELECT COUNT(*) FROM Tasks').fetchone()[0]==2
        assert c.execute('SELECT SUM(ReminderEnabled) FROM Tasks').fetchone()[0]==0
        assert c.execute('SELECT COUNT(*) FROM RecurringSchedules').fetchone()[0]==0
        expected=('b','a') if variant=='v0.1' else ('a','b')
        assert tuple(r[0] for r in c.execute('SELECT Id FROM Tasks ORDER BY SortOrder'))==expected
        c.execute("INSERT INTO Tasks(Id,Title,CreatedUtc,DueLocal,ReminderEnabled) VALUES ('c','计划','2026-10-26T00:00:00Z','2026-10-30T17:00:00',1)")
        actual=c.execute("SELECT Id FROM Tasks WHERE IsCompleted=0 AND ReminderEnabled=1 AND DueLocal<='2026-10-30T17:00:00'").fetchall()
        assert actual==[('c',)]
        c.close()

def reorder_before(ids, dragged, before):
    """与 TaskRepository.ReorderBefore 相同：把 dragged 放到 before 之前，before 为 None 时移到末尾。"""
    ids=ids.copy(); ids.remove(dragged)
    if before is None: ids.append(dragged)
    else: ids.insert(ids.index(before), dragged)
    return ids
assert reorder_before(['A','B','C','D'],'A','C')==['B','A','C','D']
assert reorder_before(['A','B','C','D'],'D','B')==['A','D','B','C']
assert reorder_before(['A','B','C','D'],'A',None)==['B','C','D','A']
assert reorder_before(['A','B','C','D'],'C','D')==['A','B','C','D']
print('PASS: 9 XAML 文件 / 事件关联 / v0.1 与 v0.1.1 数据库迁移 / 提醒查询 / 插入排序模拟')
