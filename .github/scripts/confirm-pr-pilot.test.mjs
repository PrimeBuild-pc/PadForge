import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyConfirmation } from './confirm-pr-pilot.mjs';

const head = 'a'.repeat(40);
const base = 'b'.repeat(40);
const merge = 'c'.repeat(40);
const receipt = `padforge-ci-${merge}-1`;
const input = { number: '17', head, base, workflowCommit: base };

function scenario({ fork = false } = {}) {
  const pr = {
    state: 'open', draft: false, mergeable: true, merge_commit_sha: merge, changed_files: 1,
    head: { sha: head },
    base: { ref: 'main', sha: base, repo: { full_name: 'PrimeBuild-pc/PadForge' } },
  };
  const branch = { commit: { sha: base } };
  const run = {
    id: 42, head_sha: head, path: '.github/workflows/test.yml', conclusion: 'success', run_attempt: 1,
    pull_requests: fork ? [] : [{ number: 17 }],
  };
  const files = [{ filename: 'README.md' }];
  const artifacts = [{ name: receipt, expired: false }];
  const responses = new Map([
    ['pulls/17', pr], ['branches/main', branch], [`git/commits/${merge}`, { sha: merge, parents: [{ sha: base }, { sha: head }] }],
    ['pulls/17/files?per_page=100&page=1', files],
    [`actions/workflows/test.yml/runs?event=pull_request&head_sha=${head}&per_page=1`, { workflow_runs: [run] }],
    [`actions/runs/42/artifacts?name=${receipt}`, { artifacts }],
  ]);
  const get = async (route) => {
    assert.ok(responses.has(route), `Unexpected request ${route}`);
    return responses.get(route);
  };
  return { get, pr, branch, run, files, artifacts };
}

test('accepts the successful run for the reviewed head and main', async () => {
  const { get } = scenario();
  assert.deepEqual(await verifyConfirmation(get, input), { number: '17', head, base, merge, run: 42 });
});

test('accepts a fork run with an exact merge receipt and no PR in the Actions response', async () => {
  const { get } = scenario({ fork: true });
  assert.equal((await verifyConfirmation(get, input)).merge, merge);
});

test('rejects a new head after confirmation', async () => {
  const { get, pr } = scenario();
  pr.head.sha = 'd'.repeat(40);
  await assert.rejects(verifyConfirmation(get, input), /head changed/);
});

test('rejects a base change while verification is in progress', async () => {
  const { get, branch } = scenario();
  const moveBase = async (route) => {
    if (route.startsWith('actions/runs/42/')) branch.commit.sha = 'd'.repeat(40);
    return get(route);
  };
  await assert.rejects(verifyConfirmation(moveBase, input), /Main changed/);
});

test('rejects a completed run with a failing build', async () => {
  const { get, run } = scenario();
  run.conclusion = 'failure';
  await assert.rejects(verifyConfirmation(get, input), /must have succeeded/);
});

test('rejects green CI without a fresh receipt for the exact merge', async () => {
  const { get, artifacts } = scenario();
  artifacts[0].expired = true;
  await assert.rejects(verifyConfirmation(get, input), /exact test merge/);
});

test('rejects a PR that moved a protected workflow file', async () => {
  const { get, files } = scenario({ fork: true });
  files[0].previous_filename = '.github/workflows/test.yml';
  await assert.rejects(verifyConfirmation(get, input), /separate review/);
});

test('rejects a dispatch from a different workflow commit before looking up the PR', async () => {
  const get = () => assert.fail('Unexpected request');
  await assert.rejects(verifyConfirmation(get, { ...input, workflowCommit: head }), /reviewed version of main/);
});
