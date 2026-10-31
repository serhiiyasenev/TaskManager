import base64
import copy
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("review_worker", Path(__file__).with_name("worker.py"))
w = importlib.util.module_from_spec(spec)
spec.loader.exec_module(w)

HEAD, BASE, MERGE_BASE, MERGE = "a" * 40, "b" * 40, "c" * 40, "d" * 40
REPOSITORY = "owner/project"
PR = {"number": 7, "state": "open", "draft": False, "author_association": "OWNER",
      "title": "Fix amount mapping", "changed_files": 1,
      "base": {"sha": BASE, "repo": {"full_name": REPOSITORY}},
      "head": {"sha": HEAD, "repo": {"full_name": REPOSITORY}}, "merge_commit_sha": MERGE}
FILE = {"filename": "src/Mapper.cs", "status": "modified", "additions": 1, "deletions": 1,
        "patch": "@@ -1 +1 @@\n-return amount;\n+return 0;"}
FINDING = {"title": "Amount is lost", "severity": "high", "path": "src/Mapper.cs", "version": "head",
           "line": 1, "evidence": "return 0;", "reason": "Every input maps to zero", "status": "candidate"}
PRESENTATION = {"summary": "Зміна суми", "changes": [{"before": "Сума зберігалась", "after": "Сума стає нулем",
                "files": ["src/Mapper.cs"], "kind": "mapping"}], "suggested_tests": ["Перевірити ненульову суму"]}


class FakeGitHub:
    repository = REPOSITORY

    def __init__(self):
        self.pr = copy.deepcopy(PR)
        self.files = [copy.deepcopy(FILE)]
        self.comments = []
        self.requests = []
        self.writes = []
        self.source = "return 0;\n"
        self.before_pr_read = None

    def pages(self, path, limit=30):
        self.requests.append(path)
        if path.endswith("/files"):
            return copy.deepcopy(self.files)
        return copy.deepcopy(self.comments)

    def api(self, path, payload=None, method=None):
        self.requests.append(path)
        if payload is not None:
            self.writes.append((path, payload, method))
            return {"id": 100}
        if path == "/pulls/7":
            if self.before_pr_read:
                self.before_pr_read()
            return copy.deepcopy(self.pr)
        if path.startswith("/compare/"):
            return {"merge_base_commit": {"sha": MERGE_BASE}}
        if "/check-runs" in path:
            return {"total_count": 1, "check_runs": [{"name": "Unit tests", "status": "in_progress", "conclusion": None}]}
        if path.startswith("/contents/"):
            return {"type": "file", "size": len(self.source), "encoding": "base64",
                    "content": base64.b64encode(self.source.encode()).decode()}
        if path.startswith("/git/trees/"):
            return {"tree": [{"path": "src/Mapper.cs", "type": "blob"}, {"path": ".env", "type": "blob"}], "truncated": False}
        if path.endswith("/pulls"):
            return [copy.deepcopy(self.pr)]
        raise AssertionError("Unexpected fake route " + path)


class FakeModel:
    usage = {"input_tokens": 10, "output_tokens": 20}

    def __init__(self, fail_role=None, empty_verifier=False):
        self.roles = []
        self.fail_role = fail_role
        self.empty_verifier = empty_verifier

    def run(self, role, data, schema, context=None):
        self.roles.append(role)
        if role == self.fail_role:
            raise w.ReviewError("Remote request failed (HTTP 429)")
        if role == "presenter":
            return copy.deepcopy(PRESENTATION)
        if role == "verifier":
            context.tool("read_file", {"path": "src/Mapper.cs", "version": "head", "start_line": 1})
            return {"findings": [] if self.empty_verifier else [dict(FINDING, status="supported")]}
        return {"findings": [copy.deepcopy(FINDING)]}


class ReviewTests(unittest.TestCase):
    def setUp(self):
        self.github = FakeGitHub()

    def test_full_review_keeps_risky_mapping_visible_and_records_pending_ci(self):
        model = FakeModel()
        report = w.review(self.github, PR, model)
        self.assertEqual(model.roles, ["reviewer", "verifier", "presenter"])
        self.assertEqual(report["state"], "reviewed")
        self.assertEqual(report["presentation"]["changes"][0]["kind"], "behavior")
        self.assertEqual(report["findings"][0]["status"], "supported")
        self.assertEqual(report["ci_checks"][0]["conclusion"], "pending")
        self.assertEqual(report["merge_base_sha"], MERGE_BASE)
        self.assertEqual(report["target_base_sha"], BASE)
        self.assertFalse(self.github.writes)

    def test_missing_configuration_is_explicit_and_still_produces_artifacts(self):
        report = w.review(self.github, PR, None)
        self.assertEqual(report["state"], "configuration_required")
        with tempfile.TemporaryDirectory() as directory:
            w.save_report(report, Path(directory))
            self.assertEqual(len(list(Path(directory).iterdir())), 3)
            self.assertIn("Потрібне налаштування", (Path(directory) / "report.md").read_text())

    def test_model_failure_never_becomes_a_clean_review(self):
        report = w.review(self.github, PR, FakeModel(fail_role="verifier"))
        self.assertEqual(report["state"], "failed")
        self.assertIn("не завершена", report["presentation"]["summary"])

    def test_unresolved_candidates_are_preserved(self):
        report = w.review(self.github, PR, FakeModel(empty_verifier=True))
        self.assertEqual(report["findings"][0]["status"], "uncertain")

    def test_hallucinated_quote_becomes_uncertain(self):
        self.github.source = "return amount;"
        report = w.review(self.github, PR, FakeModel())
        self.assertEqual(report["findings"][0]["status"], "uncertain")

    def test_partial_diff_never_claims_full_coverage(self):
        self.github.files[0].pop("patch")
        report = w.review(self.github, PR, FakeModel())
        self.assertEqual(report["state"], "partial")
        self.assertTrue(report["coverage_notes"])

    def test_changed_file_count_race_fails(self):
        pr = dict(PR, changed_files=2)
        with self.assertRaises(w.ReviewError):
            w.review(self.github, pr, None)

    def test_same_file_count_with_a_new_commit_still_fails_collection(self):
        self.github.pr["head"]["sha"] = "e" * 40
        with self.assertRaises(w.ReviewError):
            w.review(self.github, PR, None)

    def test_patch_budget_does_not_silently_hide_files(self):
        self.github.files[0]["patch"] = "x" * 20000
        context = w.RepositoryContext(self.github, PR)
        packet = context.packet([])
        self.assertEqual(len(packet["patches"][0]["patch"]), 16000)
        self.assertTrue(any("truncated" in n for n in context.notes))

    def test_tools_cannot_read_local_files_or_make_arbitrary_requests(self):
        context = w.RepositoryContext(self.github, PR)
        before = len(self.github.requests)
        for name in ("shell", "fetch_url", "publish", "read_environment"):
            self.assertIn("error", context.tool(name, {"url": "https://evil.invalid"}))
        for path in ("../../etc/passwd", "/etc/passwd", ".env", "cert.pfx", ".git/config", "id_rsa", "secrets.json"):
            self.assertIn("error", context.read_file(path))
        self.assertEqual(len(self.github.requests), before)

    def test_file_tools_are_pinned_and_bounded(self):
        context = w.RepositoryContext(self.github, PR)
        context.read_file("src/Mapper.cs", "base", 1)
        self.assertIn(f"/contents/src/Mapper.cs?ref={MERGE_BASE}", self.github.requests)
        for _ in range(w.MAX_TOOL_CALLS):
            context.tool("list_files", {"query": ""})
        self.assertIn("error", context.tool("list_files", {"query": ""}))
        self.assertNotIn(".env", context.list_files("")["paths"])

    def test_binary_and_oversized_source_is_not_sent_to_model(self):
        context = w.RepositoryContext(self.github, PR)
        self.github.source = "abc\x00def"
        self.assertIn("error", context.read_file("src/Mapper.cs"))
        self.github.source = "x" * (w.MAX_FILE_BYTES + 1)
        self.assertIn("error", context.read_file("src/Mapper.cs"))

    def test_unknown_file_in_model_findings_or_presenter_fails(self):
        context = w.RepositoryContext(self.github, PR)
        with self.assertRaises(w.ReviewError):
            w.validate_findings({"findings": [dict(FINDING, path="other.cs")]}, context)
        presentation = copy.deepcopy(PRESENTATION)
        presentation["changes"][0]["files"] = ["other.cs"]
        with self.assertRaises(w.ReviewError):
            w.validate_presentation(presentation, context, [])

    def test_safe_mechanical_mapping_is_collapsed(self):
        context = w.RepositoryContext(self.github, PR)
        p = w.validate_presentation(copy.deepcopy(PRESENTATION), context, [])
        report = w.review(self.github, PR, None)
        report["presentation"] = p
        rendered = w.markdown(report)
        detail_position = rendered.index("<details>")
        self.assertGreater(rendered.index("mapping:"), detail_position)

    def test_model_html_markdown_and_mentions_are_not_executable(self):
        report = w.review(self.github, PR, None)
        report["presentation"]["summary"] = '<script>alert(1)</script> ![x](https://evil.invalid) @someone'
        rendered = w.html_report(report)
        self.assertNotIn("<script>", rendered)
        self.assertIn("&lt;script&gt;", rendered)
        self.assertIn("default-src 'none'", rendered)
        markdown = w.markdown(report)
        self.assertNotIn("![x](https://evil.invalid)", markdown)
        self.assertNotIn("@someone", markdown)

    def test_obvious_secrets_are_redacted(self):
        text = 'api_key = "very-secret"; Password=private; ghp_' + 'x' * 30
        redacted = w.redact(text)
        for secret in ("very-secret", "private", 'x' * 30):
            self.assertNotIn(secret, redacted)

    def test_schema_refuses_extra_fields_bad_enums_and_large_values(self):
        for finding in (dict(FINDING, shell="rm"), dict(FINDING, severity="critical"), dict(FINDING, line=True), dict(FINDING, reason="x" * 2501)):
            with self.assertRaises(w.ReviewError):
                w.validate_schema({"findings": [finding]}, w.REVIEW_SCHEMA)


class PublishAndTriggerTests(unittest.TestCase):
    def setUp(self):
        self.github = FakeGitHub()
        self.report = w.review(self.github, PR, None)

    def test_sticky_comment_only_updates_own_bot_marker(self):
        self.github.comments = [
            {"id": 1, "body": w.MARKER, "user": {"login": "someone", "type": "User"}},
            {"id": 2, "body": w.MARKER, "user": {"login": "github-actions[bot]", "type": "Bot"}},
        ]
        self.assertTrue(w.publish(self.github, self.report))
        self.assertEqual(self.github.writes[0][0], "/issues/comments/2")
        self.assertEqual(self.github.writes[0][2], "PATCH")

    def test_human_marker_does_not_get_overwritten(self):
        self.github.comments = [{"id": 1, "body": w.MARKER, "user": {"login": "someone", "type": "User"}}]
        self.assertTrue(w.publish(self.github, self.report))
        self.assertEqual(self.github.writes[0][0], "/issues/7/comments")

    def test_new_head_or_base_cancels_publication(self):
        for branch in ("head", "base"):
            github = FakeGitHub()
            github.pr[branch]["sha"] = "e" * 40
            self.assertFalse(w.publish(github, self.report))
            self.assertFalse(github.writes)

    def test_head_change_during_comment_pagination_cancels_publication(self):
        reads = 0
        def change():
            nonlocal reads
            reads += 1
            if reads == 2:
                self.github.pr["head"]["sha"] = "e" * 40
        self.github.before_pr_read = change
        self.assertFalse(w.publish(self.github, self.report))
        self.assertFalse(self.github.writes)

    def test_closed_draft_fork_or_unknown_author_is_ineligible(self):
        for key, value in (("state", "closed"), ("draft", True), ("author_association", "NONE")):
            self.assertFalse(w.eligible(dict(PR, **{key: value}), REPOSITORY))
        fork = copy.deepcopy(PR)
        fork["head"]["repo"]["full_name"] = "attacker/fork"
        self.assertFalse(w.eligible(fork, REPOSITORY))

    def test_workflow_run_only_reviews_current_same_repo_pr_commit(self):
        event = {"workflow_run": {"event": "pull_request", "head_repository": {"full_name": REPOSITORY},
                                  "head_sha": HEAD, "pull_requests": [{"number": 7}]}}
        self.assertIsNotNone(w.resolve_pr(self.github, event, "workflow_run", ""))
        event["workflow_run"]["head_sha"] = "e" * 40
        self.assertIsNone(w.resolve_pr(self.github, event, "workflow_run", ""))
        event["workflow_run"]["event"] = "push"
        self.assertIsNone(w.resolve_pr(self.github, event, "workflow_run", ""))

    def test_old_pull_request_event_is_ignored(self):
        event = {"pull_request": copy.deepcopy(PR)}
        event["pull_request"]["head"]["sha"] = "e" * 40
        self.assertIsNone(w.resolve_pr(self.github, event, "pull_request_target", ""))

    def test_invalid_manual_number_is_not_an_api_path(self):
        with self.assertRaises(w.ReviewError):
            w.resolve_pr(self.github, {}, "workflow_dispatch", "../secrets")


class TransportTests(unittest.TestCase):
    def test_github_paginates_all_files_and_rejects_overflow(self):
        github = w.GitHub(REPOSITORY, "test-token")
        with patch.object(github, "api", side_effect=[[{}] * 100, [{}] * 20]) as api:
            self.assertEqual(len(github.pages("/pulls/7/files")), 120)
            self.assertIn("page=2", api.call_args.args[0])
        with patch.object(github, "api", return_value=[{}] * 100):
            with self.assertRaises(w.ReviewError):
                github.pages("/pulls/7/files", limit=1)

    def test_endpoint_cannot_redirect_key_to_arbitrary_host(self):
        for endpoint in ("https://evil.invalid", "http://demo.openai.azure.com", "https://demo.openai.azure.com@evil.invalid", "https://demo.openai.azure.com/?key=x", "https://demo.openai.azure.com:443", "https://demo.openai.azure.com/other"):
            with self.assertRaises(w.ReviewError):
                w.azure_url(endpoint)
        self.assertEqual(w.azure_url("https://demo.openai.azure.com/openai/v1/"), "https://demo.openai.azure.com/openai/v1/responses")
        with self.assertRaises(w.ReviewError):
            w.NoRedirect().redirect_request(None, None, 302, "", {}, "https://evil.invalid")

    def test_responses_tool_loop_replays_reasoning_and_returns_json(self):
        model = w.AzureModel("https://demo.openai.azure.com", "deployment", "fake-key")
        context = w.RepositoryContext(FakeGitHub(), PR)
        first = {"status": "completed", "output": [
            {"type": "reasoning", "id": "r1", "encrypted_content": "opaque"},
            {"type": "function_call", "id": "f1", "call_id": "c1", "name": "read_file",
             "arguments": json.dumps({"path": "src/Mapper.cs", "version": "head", "start_line": 1})}]}
        last = {"status": "completed", "output": [{"type": "message", "content": [{"type": "output_text", "text": '{"findings": []}'}]}]}
        with patch.object(w, "request_json", side_effect=[first, last]) as send:
            result = model.run("verifier", {}, w.REVIEW_SCHEMA, context)
            self.assertEqual(result, {"findings": []})
            payload = send.call_args.args[2]
            self.assertFalse(payload["store"])
            self.assertIn(first["output"][0], payload["input"])
            self.assertTrue(any(i.get("type") == "function_call_output" for i in payload["input"]))
            self.assertEqual(len(context.trace), 1)

    def test_incomplete_or_refused_response_is_not_success(self):
        model = w.AzureModel("https://demo.openai.azure.com", "deployment", "fake-key")
        for response in ({"status": "incomplete"}, {"status": "completed", "output": []}):
            with patch.object(w, "request_json", return_value=response):
                with self.assertRaises(w.ReviewError):
                    model.run("reviewer", {}, w.REVIEW_SCHEMA)

    def test_main_missing_configuration_exits_nonzero_with_honest_report(self):
        with tempfile.TemporaryDirectory() as directory:
            event = Path(directory) / "event.json"
            event.write_text(json.dumps({"pull_request": PR}))
            output = str(Path(directory) / "out")
            env = {"GITHUB_EVENT_PATH": str(event), "GITHUB_EVENT_NAME": "pull_request_target",
                   "GITHUB_REPOSITORY": REPOSITORY, "GH_TOKEN": "fake"}
            with patch.dict(os.environ, env, clear=True), patch.object(w, "GitHub", return_value=FakeGitHub()), patch.object(w.sys, "argv", ["worker.py", "review", "--output", output]):
                self.assertEqual(w.main(), 1)
            self.assertEqual(json.loads((Path(output) / "report.json").read_text())["state"], "configuration_required")


if __name__ == "__main__":
    unittest.main()
