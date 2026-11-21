import assert from 'node:assert/strict';
import { appendFile } from 'node:fs/promises';
import { verifyConfirmation } from './confirm-pr-pilot.mjs';

const repository = 'PrimeBuild-pc/PadForge';
const env = process.env;
assert.equal(env.GITHUB_REPOSITORY, repository);
assert.equal(env.GITHUB_EVENT_NAME, 'workflow_dispatch');
assert.equal(env.GITHUB_REF, 'refs/heads/main');
assert.equal(env.GITHUB_ACTOR_ID, '162145141');
assert.equal(env.GITHUB_TRIGGERING_ACTOR, 'PrimeBuild-pc');
assert.equal(env.GITHUB_RUN_ATTEMPT, '1');

const input = {
  number: env.PR_NUMBER,
  head: env.EXPECTED_HEAD,
  base: env.EXPECTED_BASE,
  workflowCommit: env.GITHUB_SHA,
  releaseVersion: env.RELEASE_VERSION,
};

async function request(endpoint, { token = env.GH_TOKEN, method = 'GET', body } = {}) {
  assert.ok(token, 'The requested GitHub token is unavailable.');
  const response = await fetch(`https://api.github.com/repos/${repository}/${endpoint}`, {
    method,
    body: body && JSON.stringify(body),
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/vnd.github+json',
      'Content-Type': 'application/json',
    },
    signal: AbortSignal.timeout(20_000),
  });
  if (!response.ok) {
    throw new Error(`GitHub API returned HTTP ${response.status} for ${method} ${endpoint}.`);
  }
  return response.json();
}

assert.match(env.MERGE_RULESET_ID ?? '', /^[1-9][0-9]*$/u, 'The exclusive merge rule must be configured.');
const activeRules = await request('rules/branches/main');
assert.ok(activeRules.some(rule => rule.type === 'update' && rule.ruleset_id === Number(env.MERGE_RULESET_ID)
  && rule.parameters?.update_allows_fetch_and_merge !== true), 'The exclusive merge rule is inactive.');
assert.ok(activeRules.some(rule => rule.type === 'pull_request'), 'A pull request rule is required.');
assert.ok(activeRules.some(rule => rule.type === 'required_status_checks'
  && rule.parameters?.strict_required_status_checks_policy
  && rule.parameters.required_status_checks.some(check => check.context === 'build' && check.integration_id === 15368)),
'The strict Actions build rule is required.');

const run = await request(`actions/runs/${env.GITHUB_RUN_ID}`);
const age = Date.now() - Date.parse(run.created_at);
assert.ok(age >= 0 && age <= 30 * 60 * 1000, 'Confirmation expired. Review the PR again before starting a new run.');

const verified = await verifyConfirmation(request, input);
if (verified.release) {
  const { version } = verified.release;
  const project = await request(`contents/src/PadForge/PadForge.csproj?ref=${verified.merge}`);
  assert.equal(project.encoding, 'base64', 'The version file is not available.');
  const xml = Buffer.from(project.content, 'base64').toString('utf8');
  const projectVersion = xml.match(/<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*<\/Version>/u)?.[1];
  assert.equal(projectVersion, version.split('-')[0], 'The release version does not match the reviewed project.');
  const tags = await request(`git/matching-refs/tags/v${version}?per_page=100`);
  assert.ok(!tags.some(tag => tag.ref === `refs/tags/v${version}`), 'The selected release tag already exists.');
}
const tested = await request(`git/commits/${verified.merge}`);
const result = await request(`pulls/${verified.number}/merge`, {
  token: env.MERGE_TOKEN,
  method: 'PUT',
  body: { sha: verified.head, merge_method: 'merge' },
});
assert.equal(result.merged, true, 'GitHub did not merge the PR.');
const commit = await request(`git/commits/${result.sha}`);
assert.deepEqual(commit.parents.map(parent => parent.sha), [verified.base, verified.head],
  'The merged commit did not have the approved parents.');
assert.equal(commit.tree.sha, tested.tree.sha, 'The merged tree differs from the tested tree.');
await appendFile(env.GITHUB_OUTPUT, [
  `source=${result.sha}`,
  `release_version=${verified.release?.version ?? ''}`,
  `prerelease=${verified.release?.prerelease ?? false}`,
  '',
].join('\n'));
await appendFile(env.GITHUB_STEP_SUMMARY, [
  '### Verified merge',
  '',
  `- Approval: ${env.GITHUB_ACTOR}`,
  `- PR: #${verified.number}`,
  `- Approved head: ${verified.head}`,
  `- Approved base: ${verified.base}`,
  `- Tested merge: ${verified.merge}`,
  `- Merged commit: ${result.sha}`,
  `- Release intent: ${verified.release?.version ?? "ordinary PR"}`,
  '',
].join('\n'));
