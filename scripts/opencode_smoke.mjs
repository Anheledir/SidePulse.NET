import { readFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import assert from 'node:assert/strict';
const [path, exe] = process.argv.slice(2);
const source = await readFile(path, 'utf8');
const { SidePulsePlugin } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const plugin = await SidePulsePlugin();
const snapshot = () => JSON.parse(execFileSync(exe, ['host', 'status', '--json'], { encoding: 'utf8' }));
const send = (type, properties = {}) => plugin.event({ event: { type, properties: { sessionID: 'opencode-test', ...properties, prompt: 'DO-NOT-FORWARD' } } });
async function expect(state) {
  for (let i = 0; i < 30; i++) {
    if (snapshot().State === state) return;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  assert.equal(snapshot().State, state);
}
await plugin.event({ event: { type: 'session.status', properties: { status: { type: 'busy' } } } });
assert.equal(snapshot().Sessions.length, 0);
await plugin.event({ event: { type: 'session.created', properties: { info: { id: 'opencode-test' } } } });
await expect('Idle');
await send('session.status', { status: { type: 'busy' } });
await expect('Working');
await send('permission.asked', { id: 'permission-1' });
await expect('Waiting');
await send('question.asked', { id: 'question-1' });
await send('session.status', { status: { type: 'busy' } });
await send('permission.replied', { requestID: 'permission-1' });
await expect('Waiting');
await send('question.rejected', { requestID: 'question-1' });
await expect('Working');
await send('session.error', { error: { message: 'DO-NOT-FORWARD' } });
await expect('Error');
await send('session.status', { status: { type: 'idle' } });
await expect('Error');
await send('session.status', { status: { type: 'retry' } });
await expect('Working');
await send('session.status', { sessionID: 'other-session', status: { type: 'busy' } });
await plugin.event({ event: { type: 'session.deleted', properties: { info: { id: 'opencode-test' } } } });
await expect('Working');
assert.equal(snapshot().Sessions.length, 2);
await send('session.status', { sessionID: 'other-session', status: { type: 'idle' } });
await expect('Idle');
await send('session.error');
await expect('Error');
await send('question.asked', { id: 'recovery-question' });
await expect('Waiting');
await send('question.replied', { requestID: 'recovery-question' });
await expect('Working');
await send('session.status', { status: { type: 'idle' } });
await expect('Idle');
await send('unrecognized', { status: { type: 'busy' } });
await expect('Idle');
// Host absence is silent, and does not reject the OpenCode event hook.
execFileSync(exe, ['host', 'stop', '--json']);
await send('session.status', { status: { type: 'busy' } });
