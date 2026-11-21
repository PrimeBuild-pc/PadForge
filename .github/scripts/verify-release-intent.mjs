import assert from 'node:assert/strict';

const versionPattern = /^(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){2}(?:-beta\.(?:0|[1-9][0-9]*))?$/u;

export async function verifyReleaseIntent(get, merge, files, version) {
  assert.match(version, versionPattern, 'Enter the exact stable or beta release version.');
  assert.ok(files.some(file => file.filename === 'release-intent.json' && file.status !== 'removed'),
    'A release PR must add or update release-intent.json.');
  const file = await get(`contents/release-intent.json?ref=${merge}`);
  assert.equal(file.encoding, 'base64', 'The release intent must be a repository file.');
  assert.ok(file.size > 0 && file.size <= 4096, 'The release intent is empty or too large.');
  const plan = JSON.parse(Buffer.from(file.content, 'base64').toString('utf8'));
  assert.equal(plan.version, version, 'The confirmed release version differs from the PR intent.');
  assert.equal(plan.prerelease, version.includes('-beta.'), 'The chosen release type differs from the PR intent.');
  assert.deepEqual(plan.channels, ['github'], 'PadForge publishes through GitHub Releases.');
  if (files.some(file => [file.filename, file.previous_filename].some(path => path?.startsWith('src/')))) {
    assert.ok(typeof plan.vmEvidence === 'string' && plan.vmEvidence.trim().length > 0,
      'Record the VM test outcome and tested revision before releasing changes in src.');
  }
  return { version, prerelease: plan.prerelease };
}
