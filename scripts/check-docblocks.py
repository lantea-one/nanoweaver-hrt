#!/usr/bin/env python3
"""Assert every documented member has one <param> per parameter and a <returns>.

WHAT: Scans C# sources for method, constructor and positional-record declarations
      and compares the parameters in the signature against the
      <param name="..."> tags in the XML doc comment immediately above it.
WHY:  AGENTS.md Prime Directive 2 requires complete XML docs. The compiler cannot
      enforce this: CS1591 only fires on a *missing* comment and only for publicly
      visible members, and CS1573 only fires when a comment already carries SOME
      <param> tags -- a doc comment with none at all produces no diagnostic.
HOW:  Line-oriented parse. Walks backwards from each declaration collecting the
      contiguous /// block, then diffs signature parameters against param tags.
      Exits 1 if any member is incomplete, so it can gate a build.
"""
import re
import sys
import pathlib
import argparse

# Keywords that mean "this declaration introduces a type, not a method".
TYPE_KEYWORDS = frozenset(
    ('class', 'struct', 'record', 'interface', 'enum', 'delegate', 'event'))

DECL = re.compile(
    r'^\s*(?:public|private|protected|internal)\s+'
    r'(?!class\b|struct\b|record\b|interface\b|enum\b|delegate\b|event\b)'
    r'(?:(?:static|virtual|override|sealed|async|partial|unsafe|extern|new|readonly)\s+)*'
    r'(?P<ret>[\w\<\>\[\]\?\,\.\s]+?)\s+'
    r'(?P<name>[A-Za-z_]\w*)\s*(?P<generic><[^(]*>)?\s*\((?P<params>.*)$'
)


def split_params(text):
    """Split a parameter list on top-level commas, ignoring generics and arrays.

    :param text: The raw text between the signature's outermost parentheses.
    :returns: One string fragment per declared parameter.
    """
    out, depth, cur = [], 0, ''
    for ch in text:
        if ch in '<([':
            depth += 1
        elif ch in '>)]':
            depth -= 1
        if ch == ',' and depth == 0:
            out.append(cur)
            cur = ''
        else:
            cur += ch
    if cur.strip():
        out.append(cur)
    return out


def param_name(frag):
    """Extract a parameter's name, which is the last identifier before any '='.

    :param frag: A single parameter fragment such as ``CancellationToken ct = default``.
    :returns: The parameter name, or None when the fragment holds no identifier.
    """
    frag = frag.split('=')[0].strip()
    frag = re.sub(r'^\[[^\]]*\]\s*', '', frag)
    for kw in ('this ', 'ref ', 'out ', 'in ', 'params ', 'scoped '):
        if frag.startswith(kw):
            frag = frag[len(kw):].strip()
    toks = re.findall(r'[A-Za-z_]\w*', frag)
    return toks[-1] if toks else None


def scan(path):
    """Report every member in one file whose doc comment is incomplete.

    :param path: Path to the C# source file to inspect.
    :returns: A list of (line number, member name, missing params, note) tuples.
    """
    lines = path.read_text(encoding='utf-8').splitlines()
    findings = []
    i = 0
    while i < len(lines):
        m = DECL.match(lines[i])
        if not m:
            i += 1
            continue
        # A '=' before the parameter list means this is a field or property
        # initializer such as `= new HashSet<string>(comparer)`, not a method.
        head = lines[i][:lines[i].index('(')]
        if '=' in head:
            i += 1
            continue
        # Gather a possibly multi-line parameter list.
        raw, j, depth = m.group('params'), i, 1
        while depth > 0 and j < len(lines) - 1:
            for ch in raw:
                if ch == '(':
                    depth += 1
                elif ch == ')':
                    depth -= 1
            if depth > 0:
                j += 1
                raw += ' ' + lines[j].strip()
            else:
                break
        sig = raw[:raw.rfind(')')] if ')' in raw else raw
        params = [p for p in (param_name(f) for f in split_params(sig)) if p]
        # Walk back over the contiguous /// block.
        doc, k = [], i - 1
        while k >= 0 and (lines[k].strip().startswith('///')
                          or lines[k].strip().startswith('[')
                          or not lines[k].strip()):
            if lines[k].strip().startswith('///'):
                doc.insert(0, lines[k])
            elif lines[k].strip() and not lines[k].strip().startswith('['):
                break
            k -= 1
        if not doc:
            i = j + 1
            continue   # CS1591's job, not ours
        blob = '\n'.join(doc)
        # <inheritdoc/> inherits the interface member's <param>/<returns>
        # wholesale, so such a member is fully documented by definition.
        if 'inheritdoc' in blob:
            i = j + 1
            continue
        documented = set(re.findall(r'<param\s+name\s*=\s*"([^"]+)"', blob))
        missing = [p for p in params if p not in documented]
        # WHAT: Decide whether this declaration can return anything at all.
        # WHY:  A positional record such as `public sealed record Foo(int Bar)`
        #       slips past the regex's lookahead when a modifier like `sealed`
        #       sits between the access modifier and `record`. Its positional
        #       parameters genuinely need <param> tags, but demanding a
        #       <returns> on a type declaration is a false positive.
        # HOW:  Treat the captured "return type" as a type declaration when any
        #       of its tokens is a type keyword, and skip the <returns> check.
        ret_tokens = m.group('ret').split()
        is_type_decl = any(t in TYPE_KEYWORDS for t in ret_tokens)
        ret = ret_tokens[-1]
        needs_returns = (not is_type_decl
                         and ret != 'void'
                         and m.group('name') != ret)
        no_returns = needs_returns and '<returns' not in blob
        if missing or no_returns:
            findings.append((i + 1, m.group('name'), missing,
                             'no <returns>' if no_returns else ''))
        i = j + 1
    return findings


ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('roots', nargs='*', default=['src'],
                help='Directories to scan recursively for .cs files.')
ap.add_argument('--quiet', action='store_true',
                help='Print only the total, not each finding.')
a = ap.parse_args()

total = 0
for root in a.roots:
    for f in sorted(pathlib.Path(root).rglob('*.cs')):
        if '/obj/' in str(f) or '/bin/' in str(f):
            continue
        res = scan(f)
        if res:
            total += len(res)
            if not a.quiet:
                print('\n%s' % f)
                for ln, name, miss, note in res:
                    bits = []
                    if miss:
                        bits.append('missing <param>: ' + ', '.join(miss))
                    if note:
                        bits.append(note)
                    print('  :%d %s -- %s' % (ln, name, '; '.join(bits)))
print('\nTOTAL incomplete members: %d' % total)
sys.exit(1 if total else 0)
