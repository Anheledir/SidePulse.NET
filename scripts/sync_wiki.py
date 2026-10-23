"""Stage versioned wiki pages while preserving pages this project does not own."""
from pathlib import Path
import argparse
import json
import shutil

MANIFEST = '.sidepulse-net-wiki-pages.json'


def stage(source, destination):
    if not source.is_dir() or not destination.is_dir():
        raise ValueError('Source and cloned wiki directories must exist')
    pages = sorted(path.name for path in source.glob('*.md'))
    if 'Home.md' not in pages:
        raise ValueError('Wiki source must contain Home.md')
    manifest = destination / MANIFEST
    previous = json.loads(manifest.read_text(encoding='utf-8')) if manifest.exists() else []
    if not isinstance(previous, list) or any(
        not isinstance(name, str) or '/' in name or '\\' in name or
        Path(name).name != name or not name.endswith('.md') for name in previous
    ):
        raise ValueError('Invalid wiki ownership manifest')
    for name in set(previous) - set(pages):
        (destination / name).unlink(missing_ok=True)
    for name in pages:
        shutil.copyfile(source / name, destination / name)
    manifest.write_text(json.dumps(pages, indent=2) + '\n', encoding='utf-8')
    return len(pages)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    print(f'Staged {stage(args.source, args.destination)} wiki pages.')
