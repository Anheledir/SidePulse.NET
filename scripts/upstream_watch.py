"""Track upstream changes without automatically importing code or moving the baseline."""
import json
import os
from pathlib import Path
from urllib.request import Request, urlopen

MARKER = '<!-- sidepulse-upstream-watch -->'


def api(path, data=None, method=None):
    request = Request('https://api.github.com/' + path,
                      data=None if data is None else json.dumps(data).encode(),
                      headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
                               'Accept': 'application/vnd.github+json',
                               'X-GitHub-Api-Version': '2022-11-28'}, method=method)
    with urlopen(request, timeout=30) as response:
        return json.load(response)


def issue_body(config, comparison):
    files = comparison.get('files', [])
    listing = '\n'.join('- `' + f['filename'] + '` (' + f['status'] + ')' for f in files[:100])
    return (MARKER + '\n## Upstream changes need review\n\n'
            + comparison['html_url'] + '\n\n'
            + str(comparison['total_commits']) + ' commit(s) since reviewed baseline `'
            + config['baseline'] + '`.\n\n'
            + listing + '\n\n'
            + 'Review protocol, integrations, tests and licensing; port applicable changes in a PR. '
              'Update `upstream.json` only after the review is complete. '
              'The compare link is authoritative if GitHub truncates the file list.\n')


def main():
    config = json.loads(Path('upstream.json').read_text())
    source = config['repository']
    metadata = api('repos/' + source)
    head = api('repos/' + source + '/commits/' + metadata['default_branch'])['sha']
    if head == config['baseline']:
        print('Upstream matches reviewed baseline.')
        return
    comparison = api('repos/' + source + '/compare/' + config['baseline'] + '...' + head)
    target = 'repos/' + os.environ['GITHUB_REPOSITORY'] + '/issues'
    page = 1
    while True:
        issues = api(target + '?state=open&per_page=100&page=' + str(page))
        existing = next((i for i in issues if not i.get('pull_request') and MARKER in (i.get('body') or '')), None)
        if existing or len(issues) < 100:
            break
        page += 1
    body = issue_body(config, comparison)
    if existing:
        if existing['body'] != body:
            api(target + '/' + str(existing['number']), {'body': body}, 'PATCH')
        print(existing['html_url'])
    else:
        result = api(target, {'title': 'Review upstream SidePulse changes', 'body': body}, 'POST')
        print(result['html_url'])


if __name__ == '__main__':
    main()
