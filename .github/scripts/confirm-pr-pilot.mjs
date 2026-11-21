import assert from 'node:assert/strict';
import { appendFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { verifyReleaseIntent } from './verify-release-intent.mjs';

const repository = 'PrimeBuild-pc/PadForge';
const shaPattern = /^[a-f0-9]{40}$/u;

export async function verifyConfirmation(get, { number, head, base, workflowCommit, releaseVersion }) {
  assert.match(number, /^[1-9][0-9]*$/u, 'Enter a valid PR number.');
  assert.match(head, shaPattern, 'Enter the full PR head SHA.');
  assert.match(base, shaPattern, 'Enter the full main SHA.');
  assert.equal(workflowCommit, base, 'Dispatch must use the reviewed version of main.');

  const pull = `pulls/${number}`;
  async function readSnapshot() {
    const [pr, branch] = await Promise.all([get(pull), get('branches/main')]);
    assert.equal(pr.state, 'open', 'PR must be open.');
    assert.equal(pr.draft, false, 'PR must be ready for review.');
    assert.equal(pr.base.ref, 'main', 'PR must target main.');
    assert.equal(pr.base.repo.full_name, repository, 'Unexpected base repository.');
    assert.equal(pr.head.sha, head, 'PR head changed after review.');
    assert.equal(branch.commit.sha, base, 'Main changed after review.');
    assert.equal(pr.base.sha, base, 'PR base changed after review.');
    assert.equal(pr.mergeable, true, 'GitHub has not confirmed that this PR is mergeable.');
    assert.match(pr.merge_commit_sha, shaPattern, 'GitHub has not generated a test merge.');
    return pr;
  }

  const pr = await readSnapshot();
  const merge = await get(`git/commits/${pr.merge_commit_sha}`);
  assert.deepEqual(merge.parents.map(parent => parent.sha), [base, head], 'Wrong test merge parents.');
  assert.ok(pr.changed_files > 0 && pr.changed_files <= 3000, 'The complete PR file list is required.');
  const files = [];
  for (let page = 1; files.length < pr.changed_files; page++) {
    const batch = await get(`${pull}/files?per_page=100&page=${page}`);
    assert.ok(batch.length > 0, 'The PR file list was truncated.');
    files.push(...batch);
  }
  assert.equal(files.length, pr.changed_files, 'The PR file list changed.');
  assert.ok(!files.some(file => [file.filename, file.previous_filename].some(
    path => path?.toLowerCase().startsWith('.github/'),
  )), 'Changes under .github require separate review and are outside this pilot.');

  const release = releaseVersion && await verifyReleaseIntent(get, merge.sha, files, releaseVersion);

  const { workflow_runs: [run] } = await get(
    `actions/workflows/test.yml/runs?event=pull_request&head_sha=${head}&per_page=1`,
  );
  assert.equal(run?.conclusion, 'success', 'The latest PR build must have succeeded.');
  assert.equal(run.head_sha, head, 'The workflow run was for a different head.');
  assert.equal(run.path, '.github/workflows/test.yml', 'Unexpected workflow path.');
  const receipt = `padforge-ci-${merge.sha}-${run.run_attempt}`;
  const { artifacts } = await get(`actions/runs/${run.id}/artifacts?name=${receipt}`);
  assert.ok(artifacts.some(artifact => !artifact.expired && artifact.name === receipt),
    'The successful build did not record this exact test merge in its latest attempt.');

  const latest = await readSnapshot();
  assert.equal(latest.merge_commit_sha, merge.sha, 'The test merge changed during verification.');
  return { number, head, base, merge: merge.sha, run: run.id, ...(release && { release }) };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const env = process.env;
  assert.equal(env.GITHUB_REPOSITORY, repository);
  assert.equal(env.GITHUB_EVENT_NAME, 'workflow_dispatch');
  assert.equal(env.GITHUB_REF, 'refs/heads/main');
  assert.equal(env.GITHUB_ACTOR_ID, '162145141');
  assert.equal(env.GITHUB_TRIGGERING_ACTOR, 'PrimeBuild-pc');
  assert.equal(env.GITHUB_RUN_ATTEMPT, '1');
  const get = async (endpoint) => {
    const response = await fetch(`https://api.github.com/repos/${repository}/${endpoint}`, {
      headers: { Authorization: `Bearer ${env.GH_TOKEN}`, Accept: 'application/vnd.github+json' },
      signal: AbortSignal.timeout(15_000),
    });
    assert.ok(response.ok, `GitHub API returned HTTP ${response.status} for ${endpoint}.`);
    return response.json();
  };
  const result = await verifyConfirmation(get, {
    number: env.PR_NUMBER, head: env.EXPECTED_HEAD, base: env.EXPECTED_BASE, workflowCommit: env.GITHUB_SHA,
  });
  await appendFile(env.GITHUB_STEP_SUMMARY, [
    '### Ordinary PR confirmed in the read-only pilot',
    '',
    `- Actor: ${env.GITHUB_ACTOR} (${env.GITHUB_ACTOR_ID})`,
    `- PR: #${result.number}`,
    `- Reviewed head: ${result.head}`,
    `- Reviewed main: ${result.base}`,
    `- Tested merge: ${result.merge}`,
    `- CI run: ${result.run}`,
    '',
  ].join('\n'));
}
