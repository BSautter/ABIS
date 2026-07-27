"""Extract literal SQL statements from the repository so Oracle can PARSE them without executing.

Only fully-literal raw strings are emitted. Anything with C# interpolation ({...} or {{...}}) is
skipped and counted, because its final text is not knowable statically — reporting a guess as
'validated' would be worse than reporting nothing.
"""
import io, json, re, sys

SRC = 'api/src/ABIS.Api/Data/AbisRepository.cs'
OUT = 'sql_statements.json'

text = io.open(SRC, encoding='utf-8').read()
lines = text.split('\n')

statements, skipped_interp, skipped_short = [], 0, 0
i = 0
while i < len(lines):
    line = lines[i]
    stripped = line.strip()
    # A raw string literal opens with """ (possibly after $ or $$ for interpolation).
    if stripped.endswith('"""'):
        is_interp = stripped.startswith('$') or stripped.startswith('$$')
        start_line = i + 1
        body = []
        i += 1
        # A raw string closes on a line whose first token is triple-quote, followed by any of
        # ) ; , or nothing. Matching only the bare and comma forms missed the ');' form and
        # swallowed the C# after it, producing a bogus ORA-01741 on a statement that was fine.
        while i < len(lines) and not lines[i].strip().startswith(chr(34)*3):
            body.append(lines[i])
            i += 1
        sql = '\n'.join(body).strip()
        i += 1

        if not sql:
            continue
        head = sql.lstrip().upper()
        if not head.startswith(('SELECT', 'INSERT', 'UPDATE', 'DELETE', 'WITH', 'MERGE')):
            continue
        if is_interp or '{' in sql:
            skipped_interp += 1
            continue
        if len(sql) < 20:
            skipped_short += 1
            continue
        statements.append({'line': start_line, 'sql': sql})
        continue
    i += 1

# De-duplicate identical SQL (several methods share a statement shape).
seen, uniq = set(), []
for s in statements:
    if s['sql'] in seen:
        continue
    seen.add(s['sql'])
    uniq.append(s)

kinds = {}
for s in uniq:
    k = s['sql'].lstrip().split()[0].upper()
    kinds[k] = kinds.get(k, 0) + 1

io.open(OUT, 'w', encoding='utf-8').write(json.dumps(uniq, indent=1))
print(f"extracted {len(uniq)} unique literal statements  {kinds}")
print(f"skipped: {skipped_interp} interpolated (text not statically knowable), {skipped_short} trivial")
