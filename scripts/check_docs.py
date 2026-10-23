"""Check documentation links, JSON and whitespace without third-party dependencies."""
from pathlib import Path
import json
import re
import sys
from urllib.parse import unquote, urlsplit


def check(root):
    errors = []
    count = 0
    for path in sorted(root.rglob('*')):
        if not path.is_file() or any(p in {'.git', '__pycache__', 'artifacts', 'bin', 'obj', '.venv'} for p in path.relative_to(root).parts):
            continue
        if path.suffix not in {'.md', '.json', '.yml', '.yaml', '.py', '.ps1'} and path.name not in {'LICENSE', '.editorconfig', '.gitignore', '.gitattributes'}:
            continue
        count += 1
        text = path.read_text(encoding='utf-8')
        name = str(path.relative_to(root))
        if not text.endswith('\n'):
            errors.append(f'{name}: missing final newline')
        for number, line in enumerate(text.splitlines(), 1):
            if line.rstrip() != line:
                errors.append(f'{name}:{number}: trailing whitespace')
        if path.suffix == '.json':
            try:
                json.loads(text)
            except ValueError as exc:
                errors.append(f'{name}: {exc}')
        if path.suffix != '.md':
            continue
        if len(re.findall(r'^```', text, re.MULTILINE)) % 2:
            errors.append(f'{name}: unbalanced code fences')
        for target in re.findall(r'\[[^\]]*\]\(([^)]+)\)', text):
            parsed = urlsplit(target)
            if parsed.scheme or parsed.netloc or not parsed.path:
                continue
            destination = (path.parent / unquote(parsed.path)).resolve()
            if not destination.exists():
                errors.append(f'{name}: missing local link {target}')
        for target in re.findall(r'\[\[([^\]]+)\]\]', text):
            page = target.split('|')[-1].split('#')[0]
            if page and not (root / 'docs/wiki' / (page + '.md')).is_file():
                errors.append(f'{name}: missing wiki page {page}')
    return count, errors


if __name__ == '__main__':
    total, errors = check(Path(__file__).resolve().parents[1])
    print('\n'.join(errors) if errors else f'Checked {total} documentation/configuration files.')
    sys.exit(bool(errors))
