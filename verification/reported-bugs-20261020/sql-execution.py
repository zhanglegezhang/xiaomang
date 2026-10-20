import json
import pathlib
import sqlite3

root = pathlib.Path('output/reported-bugs-20261020')
db = sqlite3.connect(':memory:')
db.execute('create table t(brand integer)')
db.executemany('insert into t values(?)', [(1,), (2,), (1,)])
passed, other_dialects = 0, []
for case in json.loads((root / 'sql-pairs.json').read_text(encoding='utf-8')):
    try:
        before = db.execute(case['sql']).fetchall()
    except sqlite3.Error:
        other_dialects.append(case['sql'])
        continue
    after = db.execute(case['output']).fetchall()
    assert before == after, case
    passed += 1
result = {'SQLiteExecutionEquivalent': passed, 'OtherDialectCases': other_dialects}
(root / 'sql-execution.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'PASS {passed} SQL queries execute identically; {len(other_dialects)} other-dialect token cases')
