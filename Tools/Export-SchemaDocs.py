"""Generate editable ERD/DOT and dictionary from the checked-in MySQL DDL, never cloud data."""
from pathlib import Path
import re, html

root = Path(__file__).resolve().parents[1]
sql = (root / 'MusicBoxManagement.Wpf/Data/MySqlSchema.sql').read_text(encoding='utf-8-sig')
tables = {}
relations = []
constraints = {}
primary_keys = {}
for match in re.finditer(r'CREATE TABLE IF NOT EXISTS (\w+) \((.*?)\) ENGINE=InnoDB;', sql, re.S):
    name, body = match.groups()
    columns = []
    for line in body.splitlines():
        line = line.strip().rstrip(',')
        column = re.match(r'(\w+)\s+((?:VARCHAR|CHAR|DECIMAL)\([^)]*\)|INT|BIGINT|TINYINT|TEXT)(.*)', line)
        if column:
            columns.append(column.groups())
    tables[name] = columns
    constraints[name] = [line.strip().rstrip(',') for line in body.splitlines() if re.match(r'\s*(PRIMARY KEY|UNIQUE |INDEX |FOREIGN KEY)', line)]
    primary_keys[name] = set()
    for pk in re.finditer(r'PRIMARY KEY\(([^)]+)\)', body):
        primary_keys[name].update(pk.group(1).split(','))
    for fk in re.finditer(r'FOREIGN KEY\((\w+)\) REFERENCES (\w+)\((\w+)\)', body):
        childcol, parent, parentcol = fk.groups()
        relations.append((parent, name, childcol))
tables['_MusicBoxSchemaVersion'] = [('Id', 'INT', ' PRIMARY KEY CHECK(Id=1)'), ('Version', 'INT', ' NOT NULL CHECK(Version>=0)')]
out = root / 'docs/schema'
out.mkdir(parents=True, exist_ok=True)
mmd = ['erDiagram']
dot = ['digraph MusicBox {', 'graph [rankdir=LR, bgcolor="#fbfaf7", pad="0.3", nodesep="0.35", ranksep="0.9", splines=polyline];', 'node [shape=plain, fontname="Arial", fontsize=12];', 'edge [fontname="Arial", fontsize=10, color="#72826d", arrowsize=0.7];']
dictionary = ['# Từ điển dữ liệu và ERD tổng thể', '', 'Nguồn: MySqlSchema.sql v1 và MySqlDatabase.cs (_MusicBoxSchemaVersion), tương ứng SQLite v8. Không introspect hoặc chứa dữ liệu thật. Chạy lại `python Tools/Export-SchemaDocs.py` khi DDL đổi.', '', 'Có 15 bảng nghiệp vụ + bảng phiên bản. PK/FK/unique/check trong DDL là nguồn chuẩn; sơ đồ không thay các luật transaction trong service.', '', '## Quy ước', '', '- Timestamp nghiệp vụ lưu UTC round-trip O, 7 chữ số phần giây; importer chuẩn hóa offset/Z và giữ ticks. AuditLog.CreatedAt là VARCHAR(33), writer/importer chuẩn hóa; không suy rằng riêng cột này có CHECK regex trong DDL.', '- RoomSession/OrderItem/Invoice giữ snapshot lịch sử; chỉnh danh mục không sửa snapshot.', '- Giá đơn vị là BIGINT; tổng Invoice là DECIMAL(29,0), trong phạm vi .NET decimal. SQLite lưu tổng tiền dạng TEXT.', '- ActiveRoomId/ActiveCustomerId là cột generated MySQL để unique khi Active; SQLite dùng partial unique index. Không nhập trực tiếp hai cột generated.', '- User có đúng một role theo PK MySQL AspNetUserRoles.UserId và service; Staff/Manager dùng RolePermission, Admin toàn quyền cố định từ code.', '- Reservation → RoomSession và RoomSession → Invoice có unique; trạng thái Completed có Invoice là luật service/transaction, FK không tự tạo hóa đơn.', '- FK Nullable giữ Guest/System audit không user, walk-in không reservation, ExpectedEnd null cho walk-in.', '', '## Các bảng', '']
for name, cols in tables.items():
    mmd.append(f'    {name} {{')
    rows = []
    dictionary += [f'### {name}', '', '| Cột | Kiểu MySQL | Ràng buộc/ghi chú từ DDL |', '|---|---|---|']
    for col, typ, rest in cols:
        fk = any(child == name and fkcol == col for _, child, fkcol in relations)
        pk = 'PRIMARY KEY' in rest or col in primary_keys.get(name, set())
        key = (' PK, FK' if fk else ' PK') if pk else (' FK' if fk else '')
        mmd.append(f'        {typ.replace(",", "_")} {col}{key}')
        rows.append('<TR><TD ALIGN="LEFT">' + html.escape(col + key) + '</TD><TD ALIGN="LEFT">' + html.escape(typ) + '</TD></TR>')
        dictionary.append(f'| {col} | {typ} | {rest.strip().replace("|", "&#124;")} |')
    mmd.append('    }')
    dot.append(f'"{name}" [label=<<TABLE BORDER="1" CELLBORDER="0" CELLSPACING="0" CELLPADDING="13" COLOR="#465344" BGCOLOR="#ffffff"><TR><TD COLSPAN="2" BGCOLOR="#344238"><FONT COLOR="white"><B>{name}</B></FONT></TD></TR>{"".join(rows)}</TABLE>>];')
    if constraints.get(name):
        dictionary += ['', 'Ràng buộc cấp bảng:'] + ['- `' + c + '`' for c in constraints[name]]
    dictionary.append('')
for parent, child, col in relations:
    unique = (child, col) in [('RoomSessions', 'ReservationId'), ('Invoices', 'RoomSessionId'), ('AspNetUserRoles', 'UserId')]
    rest = next(r for c, _, r in tables[child] if c == col)
    optional = 'NOT NULL' not in rest and 'PRIMARY KEY' not in rest
    left = '|o' if optional else '||'
    identifying = col in primary_keys.get(child, set()) or 'PRIMARY KEY' in rest
    line = '--' if identifying else '..'
    mmd.append(f'    {parent} {left}{line}o{"|" if unique else "{"} {child} : "{col}"')
    dot.append(f'"{parent}" -> "{child}" [label="{col}"];')
dot.append('}')
(out / 'MusicBox_Full.mmd').write_text('\n'.join(mmd) + '\n', encoding='utf-8')
(out / 'MusicBox_Full.dot').write_text('\n'.join(dot) + '\n', encoding='utf-8')
(root / 'docs/ERD_Full_And_DataDictionary.md').write_text('\n'.join(dictionary) + '\n\n## Sơ đồ\n\nNguồn [Mermaid](schema/MusicBox_Full.mmd), [Graphviz DOT](schema/MusicBox_Full.dot), bản xuất [SVG](schema/MusicBox_Full.svg) và [PNG](schema/MusicBox_Full.png). Quy ước ký hiệu theo [Mermaid ER syntax](https://mermaid.js.org/syntax/entityRelationshipDiagram.html).\n\n```mermaid\n' + '\n'.join(mmd) + '\n```\n', encoding='utf-8')
assert len(tables) == 16
print(f'Generated {len(tables)} tables / {len(relations)} FK relationships from repository DDL.')
