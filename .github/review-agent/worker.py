#!/usr/bin/env python3
"""Bounded, read-only PR review; Python 3.12 standard library only."""
import argparse
import base64
import html
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

HERE = Path(__file__).resolve().parent
MARKER = "<!-- azure-pr-review-agent:v1 -->"
MAX_FILES = 80
PATCH_BUDGET = 80000
MAX_FILE_BYTES = 200000
MAX_TOOL_CALLS = 12
SHA = re.compile(r"[0-9a-f]{40}")
REPO = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+")


class ReviewError(Exception):
    """Messages must never contain request bodies, tokens or provider responses."""


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ReviewError("HTTP redirect refused")


def request_json(url, headers, payload=None, method=None):
    data = None if payload is None else json.dumps(payload).encode()
    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    for attempt in range(3):
        try:
            with urllib.request.build_opener(NoRedirect()).open(request, timeout=90) as response:
                body = response.read(6_000_001)
                if len(body) > 6_000_000:
                    raise ReviewError("HTTP response exceeded size limit")
                return json.loads(body)
        except urllib.error.HTTPError as error:
            if error.code in (429, 500, 502, 503, 504) and attempt < 2:
                # Bounded retry; never log headers or response bodies.
                time.sleep(2 ** attempt)
                continue
            raise ReviewError(f"Remote request failed (HTTP {error.code})") from None
        except (urllib.error.URLError, TimeoutError):
            raise ReviewError("Remote request timed out or could not connect") from None
        except (ValueError, UnicodeError):
            raise ReviewError("Remote service returned invalid JSON") from None


def safe_path(path):
    if not isinstance(path, str) or len(path) > 500 or not path:
        return False
    parts = PurePosixPath(path).parts
    return (not path.startswith("/") and "\\" not in path and
            not any(ord(c) < 32 for c in path) and
            all(part not in ("..", ".git") for part in parts))


def permitted_source(path):
    if not safe_path(path):
        return False
    name = PurePosixPath(path).name.lower()
    if name.startswith(".env") or any(s in name for s in ("credential", "secret", "id_rsa", "id_ed25519")):
        return False
    if PurePosixPath(path).suffix.lower() in (".pem", ".key", ".pfx", ".p12", ".dll", ".exe", ".png", ".jpg", ".pdf", ".zip", ".woff", ".woff2"):
        return False
    return True


def redact(text):
    # Defense in depth, not a secret scanner. Deployment secrets never enter input.
    text = re.sub(r"(?i)((?:password|api[_-]?key|access[_-]?token|client[_-]?secret)\s*[\"']?\s*[:=]\s*[\"']?)[^\s\"';,}]+", r"\1[REDACTED]", text)
    return re.sub(r"\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]+)\b", "[REDACTED]", text)


class GitHub:
    def __init__(self, repository, token):
        if not REPO.fullmatch(repository):
            raise ReviewError("Invalid repository")
        self.repository = repository
        self.root = "https://api.github.com/repos/" + repository
        self.headers = {"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                        "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "azure-pr-review-agent",
                        "Content-Type": "application/json"}

    def api(self, path, payload=None, method=None):
        if not path.startswith("/") or path.startswith("//"):
            raise ReviewError("Invalid API path")
        return request_json(self.root + path, self.headers, payload, method)

    def pages(self, path, limit=30):
        result = []
        for page in range(1, limit + 1):
            sep = "&" if "?" in path else "?"
            batch = self.api(f"{path}{sep}per_page=100&page={page}")
            if not isinstance(batch, list):
                raise ReviewError("Invalid paginated response")
            result.extend(batch)
            if len(batch) < 100:
                return result
        raise ReviewError("Pagination limit reached; refusing an incomplete result")


def require_sha(value):
    if not isinstance(value, str) or not SHA.fullmatch(value):
        raise ReviewError("Invalid commit SHA")
    return value


def eligible(pr, repository):
    return (pr.get("state") == "open" and not pr.get("draft") and
            pr.get("base", {}).get("repo", {}).get("full_name") == repository and
            pr.get("head", {}).get("repo", {}).get("full_name") == repository and
            pr.get("author_association") in ("OWNER", "MEMBER", "COLLABORATOR"))


def resolve_pr(github, event, event_name, number):
    if event_name == "workflow_run":
        run = event["workflow_run"]
        if run.get("event") != "pull_request" or run.get("head_repository", {}).get("full_name") != github.repository:
            return None
        prs = run.get("pull_requests", [])
        if not prs:
            prs = github.api(f"/commits/{require_sha(run['head_sha'])}/pulls")
        matches = [p for p in prs if p.get("state", "open") == "open"]
        # Avoid guessing when one commit belongs to more than one PR.
        if len(matches) != 1:
            return None
        number = matches[0]["number"]
    elif event_name == "pull_request_target":
        number = event["pull_request"]["number"]
    if not str(number).isdigit() or int(number) < 1:
        raise ReviewError("A positive PR number is required")
    pr = github.api(f"/pulls/{int(number)}")
    if not eligible(pr, github.repository):
        return None
    if event_name == "workflow_run" and pr["head"]["sha"] != event["workflow_run"]["head_sha"]:
        return None
    if event_name == "pull_request_target" and pr["head"]["sha"] != event["pull_request"]["head"]["sha"]:
        return None
    require_sha(pr["head"]["sha"])
    require_sha(pr["base"]["sha"])
    return pr


class RepositoryContext:
    def __init__(self, github, pr):
        self.github = github
        self.pr = pr
        self.head = require_sha(pr["head"]["sha"])
        self.target_base = require_sha(pr["base"]["sha"])
        comparison = github.api(f"/compare/{self.target_base}...{self.head}")
        self.base = require_sha(comparison["merge_base_commit"]["sha"])
        self.files = github.pages(f"/pulls/{pr['number']}/files")
        if len(self.files) != pr["changed_files"]:
            raise ReviewError("PR file list changed or was incomplete; rerun review")
        current = github.api(f"/pulls/{pr['number']}")
        if current["head"]["sha"] != self.head or current["base"]["sha"] != self.target_base:
            raise ReviewError("PR changed while collecting patches; rerun review")
        self.by_path = {f["filename"]: f for f in self.files}
        self.cache = {}
        self.trees = {}
        self.trace = []
        self.notes = []
        self.tool_count = 0
        self.source_budget = 60000

    def read_file(self, path, version="head", start_line=1):
        if version not in ("base", "head") or not permitted_source(path):
            return {"error": "File path or version is not permitted"}
        if type(start_line) is not int or start_line < 1:
            return {"error": "Invalid start line"}
        key = (path, version)
        if key not in self.cache:
            if len(self.cache) >= 40:
                return {"error": "File fetch budget exhausted"}
            sha = self.base if version == "base" else self.head
            try:
                data = self.github.api(f"/contents/{urllib.parse.quote(path, safe='/')}?ref={sha}")
                if (not isinstance(data, dict) or data.get("type") != "file" or
                        data.get("encoding") != "base64" or data.get("size", MAX_FILE_BYTES + 1) > MAX_FILE_BYTES):
                    return {"error": "Not a small regular text file"}
                raw = base64.b64decode(data["content"], validate=False)
                if len(raw) > MAX_FILE_BYTES or b"\x00" in raw:
                    return {"error": "Binary or oversized file"}
                self.cache[key] = redact(raw.decode("utf-8")).splitlines()
            except (ReviewError, ValueError, UnicodeError, KeyError):
                return {"error": "File unavailable at the pinned commit"}
        lines = self.cache[key]
        excerpt = "\n".join(f"{i + 1}: {line}" for i, line in enumerate(lines[start_line-1:start_line+119], start_line-1))
        excerpt = excerpt[:min(12000, max(0, self.source_budget))]
        self.source_budget -= len(excerpt)
        return {"path": path, "version": version, "total_lines": len(lines), "text": excerpt,
                "limited": start_line + 119 < len(lines) or len(excerpt) >= 12000 or self.source_budget == 0}

    def list_files(self, query):
        if not isinstance(query, str) or len(query) > 100:
            return {"error": "Invalid path search"}
        if "head" not in self.trees:
            self.trees["head"] = self.github.api(f"/git/trees/{self.head}?recursive=1")
        tree = self.trees["head"]
        hits = [f["path"] for f in tree.get("tree", []) if f.get("type") == "blob" and
                query.casefold() in f["path"].casefold() and permitted_source(f["path"])]
        return {"paths": hits[:60], "limited": bool(tree.get("truncated")) or len(hits) > 60}

    def ci_checks(self):
        checks = []
        refs = [("head", self.head)]
        merge = self.pr.get("merge_commit_sha")
        if isinstance(merge, str) and SHA.fullmatch(merge) and merge != self.head:
            refs.append(("merge", merge))
        for source, sha in refs:
            try:
                data = self.github.api(f"/commits/{sha}/check-runs?per_page=100")
                if data.get("total_count", 0) > 100:
                    self.notes.append("CI snapshot limited to 100 check runs per SHA")
                for check in data.get("check_runs", []):
                    if check.get("name", "").startswith("AI PR review"):
                        continue
                    checks.append({"name": str(check.get("name", ""))[:160], "source": source,
                                   "sha": sha, "status": check.get("status", "unknown"),
                                   "conclusion": check.get("conclusion") or "pending"})
            except ReviewError:
                self.notes.append(f"CI checks unavailable for {source}; no passing result inferred")
        return checks

    def tool(self, name, args):
        self.tool_count += 1
        if self.tool_count > MAX_TOOL_CALLS:
            return {"error": "Tool budget exhausted"}
        self.trace.append({"tool": name, "arguments": args})
        if name == "read_file" and set(args) == {"path", "version", "start_line"}:
            return self.read_file(**args)
        if name == "list_files" and set(args) == {"query"}:
            return self.list_files(**args)
        return {"error": "Unknown or invalid tool; only repository reads are allowed"}

    def packet(self, checks):
        patches, remaining = [], PATCH_BUDGET
        for f in self.files:
            path = f["filename"]
            patch = f.get("patch")
            if not permitted_source(path) or not isinstance(patch, str) or not patch:
                self.notes.append(f"Patch unavailable/excluded: {path}")
                continue
            if len(patches) >= MAX_FILES or remaining <= 0:
                self.notes.append(f"Patch outside input budget: {path}")
                continue
            patch = redact(patch)
            n = min(len(patch), 16000, remaining)
            patches.append({"path": path, "previous_path": f.get("previous_filename"),
                            "status": f["status"], "patch": patch[:n]})
            if n < len(patch):
                self.notes.append(f"Patch truncated: {path}")
            remaining -= n
        return {"repository": self.github.repository, "pr": self.pr["number"],
                "title": redact(self.pr["title"][:500]), "base_sha": self.base, "head_sha": self.head,
                "patches": patches, "ci_checks": checks, "coverage_notes": self.notes[:100],
                "total_changed_files": len(self.files)}


def obj(properties):
    return {"type": "object", "properties": properties, "required": list(properties), "additionalProperties": False}


STR = {"type": "string"}
FINDING = obj({"title": STR, "severity": {"type": "string", "enum": ["high", "medium", "low"]},
               "path": STR, "version": {"type": "string", "enum": ["base", "head"]},
               "line": {"type": "integer"}, "evidence": STR, "reason": STR,
               "status": {"type": "string", "enum": ["candidate", "supported", "uncertain", "dismissed"]}})
REVIEW_SCHEMA = obj({"findings": {"type": "array", "items": FINDING}})
CHANGE = obj({"before": STR, "after": STR, "files": {"type": "array", "items": STR},
              "kind": {"type": "string", "enum": ["behavior", "mechanical", "mapping", "formatting"]}})
PRESENT_SCHEMA = obj({"summary": STR, "changes": {"type": "array", "items": CHANGE},
                      "suggested_tests": {"type": "array", "items": STR}})
TOOLS = [
    {"type": "function", "name": "read_file", "description": "Read up to 120 numbered lines at an immutable base/head commit.",
     "parameters": obj({"path": STR, "version": {"type": "string", "enum": ["base", "head"]}, "start_line": {"type": "integer"}}), "strict": True},
    {"type": "function", "name": "list_files", "description": "Find repository paths containing a substring (not content search).",
     "parameters": obj({"query": STR}), "strict": True},
]


def validate_schema(value, schema):
    kind = schema["type"]
    valid = {"object": isinstance(value, dict), "array": isinstance(value, list),
             "string": isinstance(value, str), "integer": type(value) is int}[kind]
    if not valid:
        raise ReviewError("Model output type mismatch")
    if "enum" in schema and value not in schema["enum"]:
        raise ReviewError("Model output contains an invalid enum")
    if kind == "object":
        if set(value) != set(schema["properties"]):
            raise ReviewError("Model output fields mismatch")
        for key, item in value.items():
            validate_schema(item, schema["properties"][key])
    elif kind == "array":
        if len(value) > 30:
            raise ReviewError("Model output list exceeded limit")
        for item in value:
            validate_schema(item, schema["items"])
    elif kind == "string" and len(value) > 2500:
        raise ReviewError("Model output text exceeded limit")


def azure_url(endpoint):
    u = urllib.parse.urlsplit(endpoint)
    host = u.hostname or ""
    if (u.scheme != "https" or u.username or u.password or u.port or u.query or u.fragment or
            not host.endswith((".openai.azure.com", ".services.ai.azure.com")) or
            u.path.rstrip("/") not in ("", "/openai/v1")):
        raise ReviewError("Use an HTTPS Azure OpenAI resource endpoint without query parameters")
    return f"https://{host}/openai/v1/responses"


class AzureModel:
    def __init__(self, endpoint, deployment, key):
        self.url = azure_url(endpoint)
        self.deployment = deployment
        self.headers = {"api-key": key, "Content-Type": "application/json"}
        self.calls = 0
        self.usage = {"input_tokens": 0, "output_tokens": 0}

    def run(self, role, data, schema, context=None):
        prompt = (HERE / (role + ".md")).read_text(encoding="utf-8")
        conversation = [{"role": "user", "content": json.dumps(data, ensure_ascii=False)}]
        for turn in range(4 if context else 1):
            self.calls += 1
            if self.calls > 6:
                raise ReviewError("Model request budget exhausted")
            payload = {"model": self.deployment, "instructions": prompt, "input": conversation,
                       "store": False, "include": ["reasoning.encrypted_content"], "max_output_tokens": 6000,
                       "text": {"format": {"type": "json_schema", "name": role, "strict": True, "schema": schema}}}
            if context:
                payload["tools"] = TOOLS
                payload["tool_choice"] = "none" if turn == 3 or context.tool_count >= MAX_TOOL_CALLS else "auto"
                payload["parallel_tool_calls"] = False
            response = request_json(self.url, self.headers, payload)
            usage = response.get("usage") or {}
            for key in self.usage:
                self.usage[key] += usage.get(key, 0) or 0
            if response.get("status") != "completed":
                raise ReviewError("Model response incomplete; no successful review inferred")
            output = response.get("output", [])
            calls = [item for item in output if item.get("type") == "function_call"]
            if calls:
                if not context or turn == 3 or len(calls) > 4:
                    raise ReviewError("Model tool-call limit exceeded")
                # Retain opaque reasoning items for stateless Responses tool calling.
                conversation.extend(output)
                for call in calls:
                    try:
                        args = json.loads(call["arguments"])
                        result = context.tool(call["name"], args) if isinstance(args, dict) else {"error": "Invalid arguments"}
                    except (ValueError, TypeError):
                        result = {"error": "Invalid tool arguments"}
                    conversation.append({"type": "function_call_output", "call_id": call["call_id"],
                                         "output": json.dumps(result, ensure_ascii=False)})
                continue
            text = "".join(part.get("text", "") for item in output if item.get("type") == "message"
                           for part in item.get("content", []) if part.get("type") == "output_text")
            try:
                value = json.loads(text)
            except ValueError:
                raise ReviewError("Model returned no valid structured result") from None
            validate_schema(value, schema)
            return value
        raise ReviewError("Model did not finish within the request budget")


def validate_findings(value, context, verification=False):
    validate_schema(value, REVIEW_SCHEMA)
    result, seen = [], set()
    for f in value["findings"]:
        path = f["path"]
        if path not in context.by_path or not permitted_source(path) or f["line"] < 1:
            raise ReviewError("Finding references an invalid location")
        if verification and f["status"] == "candidate":
            raise ReviewError("Verifier left an unverified candidate")
        key = (path, f["line"], f["title"].casefold())
        if key in seen:
            continue
        seen.add(key)
        f = dict(f)
        if verification and f["status"] == "supported":
            context.read_file(path, f["version"], f["line"])
            lines = context.cache.get((path, f["version"]), [])
            cited = "\n".join(lines[f["line"]-1:f["line"]+7])
            if not f["evidence"].strip() or f["evidence"] not in cited:
                f["status"] = "uncertain"
                f["reason"] = "Source evidence could not be matched at the cited line. " + f["reason"][:1500]
        result.append(f)
    return result


def validate_presentation(value, context, findings):
    validate_schema(value, PRESENT_SCHEMA)
    important = {f["path"] for f in findings if f["status"] in ("supported", "uncertain")}
    for change in value["changes"]:
        if not change["files"] or any(path not in context.by_path for path in change["files"]):
            raise ReviewError("Presenter references files outside this PR")
        if important.intersection(change["files"]):
            change["kind"] = "behavior"
    return value


def review(github, pr, model):
    context = RepositoryContext(github, pr)
    checks = context.ci_checks()
    packet = context.packet(checks)
    report = {"schema_version": 1, "repository": github.repository, "pr": pr["number"],
              "head_sha": context.head, "target_base_sha": context.target_base, "merge_base_sha": context.base,
              "state": "configuration_required", "findings": [], "presentation": {"summary": "AI-перевірку не виконано: налаштуйте Azure OpenAI за інструкцією docs/ai-pr-review.md.", "changes": [], "suggested_tests": []},
              "files": [{"path": f["filename"], "status": f["status"], "additions": f["additions"], "deletions": f["deletions"]} for f in context.files],
              "ci_checks": checks, "coverage_notes": context.notes, "tool_trace": [], "usage": {}}
    if model is None:
        return report
    try:
        first = model.run("reviewer", packet, REVIEW_SCHEMA)
        candidates = validate_findings(first, context)
        second = model.run("verifier", {"pr_context": packet, "candidates": candidates}, REVIEW_SCHEMA, context)
        report["findings"] = validate_findings(second, context, verification=True)
        # Preserve candidates omitted by the verifier as uncertain, never silently discard.
        answered = {(f["path"], f["line"], f["title"]) for f in report["findings"]}
        for candidate in candidates:
            if (candidate["path"], candidate["line"], candidate["title"]) not in answered:
                report["findings"].append(dict(candidate, status="uncertain", reason="Verifier did not resolve this candidate. " + candidate["reason"][:1500]))
        third = model.run("presenter", {"pr_context": packet, "findings": report["findings"]}, PRESENT_SCHEMA)
        report["presentation"] = validate_presentation(third, context, report["findings"])
        report["state"] = "partial" if context.notes else "reviewed"
    except ReviewError as error:
        report["state"] = "failed"
        report["presentation"] = {"summary": "AI-перевірка не завершена. " + str(error), "changes": [], "suggested_tests": []}
    report["tool_trace"] = context.trace
    report["usage"] = model.usage
    return report


def clean(value):
    # Escape all model text; suppress HTML, Markdown injection and user mentions.
    value = redact(str(value))
    value = re.sub(r"[\x00-\x1f]", " ", value).replace("@", "＠")
    return html.escape(value, quote=True)


def md(value):
    value = clean(value)
    return re.sub(r"([\\`*_{}\[\]()#+.!|>~-])", r"\\\1", value)


def location(report, finding):
    sha = report["head_sha"] if finding["version"] == "head" else report["merge_base_sha"]
    path = urllib.parse.quote(finding["path"], safe="/")
    return f"https://github.com/{report['repository']}/blob/{sha}/{path}#L{finding['line']}"


def markdown(report):
    states = {"reviewed": "AI-перевірку завершено", "partial": "Часткова AI-перевірка — є обмеження покриття",
              "configuration_required": "Потрібне налаштування Azure OpenAI", "failed": "AI-перевірка не завершена"}
    parts = [MARKER, "## AI PR review", f"**{states[report['state']]}**", f"Commit: `{report['head_sha']}`",
             md(report["presentation"]["summary"]), "### Зміни поведінки", "| Було | Стало | Файли |", "| --- | --- | --- |"]
    changes = report["presentation"]["changes"]
    for change in changes:
        if change["kind"] == "behavior":
            parts.append(f"| {md(change['before'])} | {md(change['after'])} | {md(', '.join(change['files']))} |")
    if not any(c["kind"] == "behavior" for c in changes):
        parts.append("| — | Немає сформованого опису змін поведінки | — |")
    parts.extend(["", "### Зауваження та ризики"])
    active = [f for f in report["findings"] if f["status"] != "dismissed"]
    for f in active:
        parts.append(f"- **{md(f['severity'])} / {md(f['status'])}: {md(f['title'])}** — [{md(f['path'])}:{f['line']}]({location(report, f)}). {md(f['reason'])}")
    if not active:
        parts.append("Зауважень не сформовано. Це не гарантія відсутності дефектів; врахуйте статус перевірки вище.")
    parts.extend(["", "### CI — знімок стану, а не новий запуск тестів", "| Перевірка | Commit | Стан |", "| --- | --- | --- |"])
    for c in report["ci_checks"][:60]:
        parts.append(f"| {md(c['name'])} | {md(c['source'])} | {md(c['status'])} / {md(c['conclusion'])} |")
    if not report["ci_checks"]:
        parts.append("| Дані CI недоступні або перевірки ще не створені | — | Невідомо |")
    parts.extend(["", "### Що перевірити додатково"] + ["- " + md(s) for s in report["presentation"]["suggested_tests"]])
    parts.extend(["", "<details><summary>Технічні деталі та відхилені зауваження</summary>", ""])
    for c in changes:
        if c["kind"] != "behavior":
            parts.append(f"- {md(c['kind'])}: {md(c['before'])} → {md(c['after'])} ({md(', '.join(c['files']))})")
    for f in report["findings"]:
        if f["status"] == "dismissed":
            parts.append(f"- Відхилено: {md(f['title'])}. {md(f['reason'])}")
    parts.extend(["", "</details>", "", "<details><summary>Покриття та обмеження</summary>", "",
                  f"Змінено файлів: {len(report['files'])}. Викликів інструментів: {len(report['tool_trace'])}.",
                  "Supported означає висновок агента з перевіреним посиланням на код, а не відтворений тестом дефект.",
                  "Тести виконує наявний CI. Агент не запускає код PR і не змінює вихідні файли."])
    parts += ["- " + md(n) for n in report["coverage_notes"][:50]]
    if len(report["coverage_notes"]) > 50:
        parts.append("Повний перелік обмежень — у report.json артефакту.")
    parts.extend(["", "</details>", "", "HTML-звіт і JSON доступні в артефакті `ai-pr-review` цього запуску."])
    return "\n\n".join(parts[:5]) + "\n\n" + "\n".join(parts[5:])


def html_report(report):
    p = report["presentation"]
    cards, quiet = [], []
    for c in p["changes"]:
        card = f'<article><small>{clean(", ".join(c["files"]))}</small><div class="pair"><section><b>Було</b><p>{clean(c["before"])}</p></section><section><b>Стало</b><p>{clean(c["after"])}</p></section></div></article>'
        (cards if c["kind"] == "behavior" else quiet).append(card)
    findings = "".join(f'<li><strong>{clean(f["severity"])} · {clean(f["status"])} · {clean(f["title"])}</strong><p>{clean(f["reason"])}</p><a href="{html.escape(location(report, f), quote=True)}">{clean(f["path"])}:{f["line"]}</a></li>' for f in report["findings"] if f["status"] != "dismissed")
    checks = "".join(f'<tr><td>{clean(c["name"])}</td><td>{clean(c["source"])}</td><td>{clean(c["status"])} / {clean(c["conclusion"])}</td></tr>' for c in report["ci_checks"])
    notes = "".join(f"<li>{clean(n)}</li>" for n in report["coverage_notes"])
    return f'''<!doctype html><html lang="uk"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'"><title>PR review #{report['pr']}</title><style>
body{{margin:0;background:#10151d;color:#edf1f7;font:16px/1.6 system-ui,sans-serif}}main{{max-width:1050px;margin:auto;padding:32px 20px}}h1{{font-size:36px}}small,.muted{{color:#aebacf}}article,li{{background:#1a2330;border:1px solid #344253;border-radius:12px;padding:20px;margin:16px 0;overflow-wrap:anywhere}}.pair{{display:grid;grid-template-columns:1fr 1fr;gap:24px}}.pair section:last-child{{border-left:3px solid #65d8ad;padding-left:20px}}b,a{{color:#8ee5c1}}code{{overflow-wrap:anywhere}}table{{width:100%;border-collapse:collapse}}td,th{{text-align:left;padding:12px;border-bottom:1px solid #344253;overflow-wrap:anywhere}}ul{{padding:0;list-style:none}}summary{{cursor:pointer;padding:18px 0;color:#f8d178}}@media(max-width:650px){{.pair{{grid-template-columns:1fr}}h1{{font-size:28px}}}}
</style></head><body><main><p class="muted">{clean(report['repository'])} · PR #{report['pr']}</p><h1>Що змінилося</h1><p><b>Статус: {clean(report['state'])}</b></p><p>{clean(p['summary'])}</p><p class="muted">Commit <code>{report['head_sha']}</code></p>{''.join(cards)}<h2>Ризики й зауваження</h2><ul>{findings or '<li>Зауважень не сформовано. Врахуйте статус і обмеження перевірки.</li>'}</ul><h2>Стан CI</h2><p class="muted">Знімок наявних перевірок. Цей агент не запускає тести.</p><table><thead><tr><th>Перевірка</th><th>Commit</th><th>Стан</th></tr></thead><tbody>{checks}</tbody></table><details><summary>Другорядні зміни</summary>{''.join(quiet) or '<p>Немає.</p>'}</details><details><summary>Покриття та обмеження</summary><p>Файлів: {len(report['files'])}. Supported — висновок агента з перевіреним посиланням, не доказ виконаного тесту.</p><ul>{notes}</ul></details></main></body></html>'''


def save_report(report, output):
    output.mkdir(parents=True, exist_ok=True)
    (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    (output / "report.md").write_text(markdown(report), encoding="utf-8")
    (output / "report.html").write_text(html_report(report), encoding="utf-8")


def publish(github, report):
    if report.get("repository") != github.repository or type(report.get("pr")) is not int:
        raise ReviewError("Report repository/PR mismatch")
    for key in ("head_sha", "target_base_sha", "merge_base_sha"):
        require_sha(report.get(key))
    pr = github.api(f"/pulls/{report['pr']}")
    if not eligible(pr, github.repository) or pr["head"]["sha"] != report["head_sha"] or pr["base"]["sha"] != report["target_base_sha"]:
        return False
    body = markdown(report)
    if len(body) > 60000:
        body = body[:56000] + "\n\nЗвіт скорочено. Повний результат — у report.html / report.json артефакту."
    comments = github.pages(f"/issues/{report['pr']}/comments")
    own = [c for c in comments if c.get("user", {}).get("login") == "github-actions[bot]" and
           c.get("user", {}).get("type") == "Bot" and c.get("body", "").startswith(MARKER)]
    # Check the head again after pagination; a push can invalidate the report.
    latest = github.api(f"/pulls/{report['pr']}")
    if not eligible(latest, github.repository) or latest["head"]["sha"] != report["head_sha"] or latest["base"]["sha"] != report["target_base_sha"]:
        return False
    if own:
        github.api(f"/issues/comments/{own[-1]['id']}", {"body": body}, "PATCH")
    else:
        github.api(f"/issues/{report['pr']}/comments", {"body": body}, "POST")
    return True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("review", "publish"))
    parser.add_argument("--output", default="review-output")
    args = parser.parse_args()
    github = GitHub(os.environ.get("GITHUB_REPOSITORY", ""), os.environ.get("GH_TOKEN", ""))
    output = Path(args.output)
    if args.command == "publish":
        file = output / "report.json"
        if not file.exists():
            print("No eligible PR report to publish.")
            return 0
        if file.stat().st_size > 3_000_000:
            raise ReviewError("Report exceeded size limit")
        report = json.loads(file.read_text(encoding="utf-8"))
        print("Report published." if publish(github, report) else "Stale/closed PR: report was not published.")
        return 0
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    pr = resolve_pr(github, event, os.environ.get("GITHUB_EVENT_NAME", ""), os.environ.get("PR_NUMBER", ""))
    if not pr:
        print("No eligible current PR. Drafts, forks and untrusted authors are skipped.")
        return 0
    values = [os.environ.get(k, "") for k in ("AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_DEPLOYMENT", "AZURE_OPENAI_API_KEY")]
    model = AzureModel(*values) if all(values) else None
    report = review(github, pr, model)
    save_report(report, output)
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
            f.write(f"pr_number={pr['number']}\n")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as f:
            f.write(markdown(report))
    print("Review state: " + report["state"])
    return 1 if report["state"] in ("failed", "configuration_required") else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ReviewError, KeyError, ValueError, OSError, TypeError):
        # Do not dump payloads or tracebacks that could contain model/source data.
        print("Review failed safely. Check configuration, API availability and schema; no successful review is claimed.", file=sys.stderr)
        sys.exit(1)
