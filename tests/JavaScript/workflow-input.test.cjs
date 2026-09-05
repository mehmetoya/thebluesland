const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const yaml = fs.readFileSync(path.join(__dirname, '../../.github/workflows/suggest-curator-note.yml'), 'utf8');
const step = yaml.split('      - name: Suggest curator note\n')[1].split('\n      - name:')[0];
const script = step.split('        run: |\n')[1].split('\n').map(line => line.replace(/^          /, '')).join('\n');

for (const input of ['$(printf INJECTION_RAN >&2)', '"; printf INJECTION_RAN >&2; #', 'invalid']) {
  test(`workflow rejects untrusted input as data: ${JSON.stringify(input)}`, () => {
    assert.match(step, /SPOTIFY_PLAYLIST_ID: \$\{\{ inputs\.spotifyPlaylistId \}\}/);
    assert.doesNotMatch(script, /\$\{\{ inputs\./);
    const result = spawnSync('bash', ['-c', script], {
      env: { PATH: '/usr/bin:/bin', SPOTIFY_PLAYLIST_ID: input }, encoding: 'utf8', timeout: 3000,
    });
    assert.equal(result.status, 1);
    assert.match(result.stdout, /Playlist ID must contain exactly 22/);
    assert.doesNotMatch(result.stdout + result.stderr, /INJECTION_RAN/);
  });
}

test('workflow passes a valid ID as one argument to the tool', () => {
  const id = '0iJt9LMebhOY0KSHSJw3cS';
  const result = spawnSync('bash', ['-c', 'dotnet() { printf "TOOL:%s\\n" "$@"; }\n' + script], {
    env: { PATH: '/usr/bin:/bin', SPOTIFY_PLAYLIST_ID: id, GITHUB_STEP_SUMMARY: '/dev/null' },
    encoding: 'utf8', timeout: 3000,
  });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, new RegExp(`TOOL:${id}\\n`));
});
