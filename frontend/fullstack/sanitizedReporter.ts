import { existsSync, lstatSync, mkdirSync, readdirSync, realpathSync, unlinkSync, writeFileSync } from 'node:fs';
import { basename, isAbsolute, relative, resolve } from 'node:path';
import type { Reporter, TestCase, TestResult } from '@playwright/test/reporter';
import { fullStackRunId, fullStackState } from './state';

const cases = new Map([
  ['real source review, two partial commits, private live inbox and durable relogin', 1],
  ['owner removal revokes a real member draft and task; test clock expires the real cookie', 2],
  ['AI unavailable and invalid output create no tasks; manual work and real password recovery remain usable', 3],
]);
const projects = new Set(['desktop-1440x900', 'mobile-390x844']);
interface FailedCase {
  case: number; project: string; status: 'failed' | 'timedOut' | 'interrupted'; sourceLocation: string;
}

function assertionLocation(result: TestResult): string {
  const file = resolve('fullstack/journeys.spec.ts').replaceAll('\\', '/');
  // Inspect only bounded stack-frame lines, never print or persist the stack.
  // Locator messages/call logs may contain account links or filled passwords.
  for (const error of result.errors.slice(0, 8)) {
    for (const line of (error.stack ?? '').split('\n').slice(0, 80)) {
      const frame = line.trim().replaceAll('\\', '/');
      if (!frame.startsWith('at ') || frame.length > 8_192) continue;
      const index = frame.lastIndexOf(file + ':');
      if (index < 0) continue;
      const suffix = frame.slice(index + file.length + 1);
      const match = /^(\d{1,6}):(\d{1,5})\)?$/.exec(suffix);
      if (!match || Number(match[1]) < 1 || Number(match[2]) < 1) continue;
      return `journeys.spec.ts:${Number(match[1])}:${Number(match[2])}`;
    }
  }
  return 'assertion-location-unavailable';
}

export default class SanitizedFullStackReporter implements Reporter {
  private statuses: TestResult['status'][] = [];
  private errors = 0;
  private flaky = 0;
  private failedCases: FailedCase[] = [];
  onBegin() {
    mkdirSync(resolve('test-results'), { recursive: true });
    this.removePrivateContexts();
    // A previous passing report must never survive a failed new invocation.
    this.write();
  }
  onTestEnd(test: TestCase, result: TestResult) {
    this.statuses.push(result.status);
    if (result.retry > 0) this.flaky++;
    if (result.status === 'failed' || result.status === 'timedOut' || result.status === 'interrupted') {
      const project = test.parent.project()?.name ?? '';
      const diagnostic: FailedCase = { case: cases.get(test.title) ?? 0,
        project: projects.has(project) ? project : 'unknown', status: result.status,
        sourceLocation: assertionLocation(result) };
      if (this.failedCases.length < 16) this.failedCases.push(diagnostic);
      // Case/project are finite identifiers; never echo arbitrary test titles,
      // error messages, attachment paths or values from the application.
      console.error(`Full-stack case ${diagnostic.case}; project=${diagnostic.project}; status=${diagnostic.status}; ${diagnostic.sourceLocation}`);
    }
    // Playwright can still create error-context.md from raw error messages
    // with DOM capture disabled. Remove the exact owned attachment immediately.
    for (const attachment of result.attachments)
      if (attachment.name === 'error-context' && attachment.path) this.removePrivateContext(attachment.path);
    this.write();
  }
  onError() {
    this.errors++;
    console.error('Full-stack framework failure; private error details omitted.');
  }
  onEnd() { this.removePrivateContexts(); this.write(); }
  // Prevent Playwright from adding a default reporter that prints raw errors.
  printsToStdio() { return true; }
  private removePrivateContext(path: string) {
    try {
      const directory = resolve('test-results/fullstack-private');
      const target = resolve(path);
      const contained = relative(directory, target);
      if (!contained || contained.startsWith('..') || isAbsolute(contained) || basename(target) !== 'error-context.md')
        throw new Error();
      if (!existsSync(target)) return;
      if (lstatSync(target).isSymbolicLink() || realpathSync(target) !== target) throw new Error();
      unlinkSync(target);
    } catch {
      this.errors++;
      console.error('Owned private error-context cleanup could not be verified.');
    }
  }
  private removePrivateContexts() {
    const directory = resolve('test-results/fullstack-private');
    if (!existsSync(directory)) return;
    try {
      // Walk only bounded normal directories below this reporter's exact output
      // root. Delete individual native contexts; never recursively delete a tree.
      const walk = (path: string, depth: number) => {
        if (depth > 4 || lstatSync(path).isSymbolicLink() || realpathSync(path) !== path) throw new Error();
        const entries = readdirSync(path, { withFileTypes: true });
        if (entries.length > 1000) throw new Error();
        for (const entry of entries) {
          const target = resolve(path, entry.name);
          if (entry.isDirectory()) walk(target, depth + 1);
          else if (entry.isFile() && entry.name === 'error-context.md') this.removePrivateContext(target);
        }
      };
      walk(directory, 0);
    } catch {
      this.errors++;
      console.error('Owned private output inspection could not be verified.');
    }
  }
  private write() {
    const summary = {
      schemaVersion: 1, kind: 'controlled-full-stack-browser', runId: fullStackRunId(),
      provider: 'fake', transport: fullStackState().transport,
      total: this.statuses.length + this.errors,
      passed: this.statuses.filter(status => status === 'passed').length,
      failed: this.statuses.filter(status => status !== 'passed' && status !== 'skipped').length + this.errors,
      skipped: this.statuses.filter(status => status === 'skipped').length, flaky: this.flaky,
      failedCases: this.failedCases,
    };
    // No error messages, URLs, accounts, cookies, mail or control state are serialized.
    writeFileSync(resolve('test-results/fullstack-summary.json'), JSON.stringify(summary, null, 2) + '\n');
  }
}
