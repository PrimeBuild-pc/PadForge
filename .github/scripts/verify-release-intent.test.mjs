import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyReleaseIntent } from './verify-release-intent.mjs';

const merge = 'a'.repeat(40);
const changedIntent = [{ filename: 'release-intent.json', status: 'added' }];
const plan = { version: '0.2.1-beta.2', prerelease: true, channels: ['github'] };

function appContents(value) {
  return async (endpoint) => {
    assert.equal(endpoint, `contents/release-intent.json?ref=${merge}`);
    const content = Buffer.from(JSON.stringify(value));
    return { encoding: 'base64', size: content.byteLength, content: content.toString('base64') };
  };
}

test('accepts an explicit beta for GitHub Releases', async () => {
  assert.deepEqual(await verifyReleaseIntent(appContents(plan), merge, changedIntent, plan.version),
    { version: plan.version, prerelease: true });
});

test('accepts stable only when the dispatch and PR both specify a stable version', async () => {
  const stable = { ...plan, version: '0.2.1', prerelease: false };
  assert.equal((await verifyReleaseIntent(appContents(stable), merge, changedIntent, stable.version)).prerelease, false);
  await assert.rejects(verifyReleaseIntent(appContents(plan), merge, changedIntent, stable.version), /version differs/);
});

test('rejects an unchanged intent and a channel not included in PadForge', async () => {
  await assert.rejects(verifyReleaseIntent(appContents(plan), merge, [], plan.version), /must add or update/);
  await assert.rejects(verifyReleaseIntent(appContents({ ...plan, channels: ['github', 'winget'] }),
    merge, changedIntent, plan.version), /publishes through GitHub Releases/);
});

test('requires a VM report for application-source changes', async () => {
  const sourceFiles = [...changedIntent, { filename: 'src/PadForge/Output/VigemGamepad.cs', status: 'modified' }];
  await assert.rejects(verifyReleaseIntent(appContents(plan), merge, sourceFiles, plan.version), /Record the VM test/);
  const testedPlan = { ...plan, vmEvidence: 'Virtual controller smoke test passed on Windows, commit 1234567.' };
  assert.equal((await verifyReleaseIntent(appContents(testedPlan), merge, sourceFiles, plan.version)).version, plan.version);
});
