"""Tiny client for the RT-108 PoC stack (stdlib only). Talks to nginx exactly like the SPA does."""
import json
import os
import pathlib
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

# Same origin as the SPA: nginx on the workspace's RT_WEB_PORT also serves Keycloak under /auth.
BASE = os.environ.get("RT_BASE", f"http://localhost:{os.environ.get('RT_WEB_PORT', '8080')}")
KEYCLOAK = os.environ.get("RT_KEYCLOAK", f"{BASE}/auth/realms/reporting-tool")
PASSWORD = "local-poc-only"  # local realm test users only (poc/keycloak)
TEMPLATES = pathlib.Path(__file__).resolve().parent.parent / "templates"
EVIDENCE = pathlib.Path(os.environ.get("RT_EVIDENCE", pathlib.Path(__file__).resolve().parent.parent / "evidence"))
EVIDENCE.mkdir(parents=True, exist_ok=True)


def token(user):
    body = urllib.parse.urlencode({"grant_type": "password", "client_id": "rt-gate-tests", "username": user, "password": PASSWORD}).encode()
    with urllib.request.urlopen(urllib.request.Request(f"{KEYCLOAK}/protocol/openid-connect/token", data=body), timeout=30) as r:
        return json.load(r)["access_token"]


def call(method, path, tok=None, body=None, raw=False):
    """Returns (status, headers, parsed JSON or bytes)."""
    data = json.dumps(body, ensure_ascii=False).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    if tok:
        req.add_header("Authorization", f"Bearer {tok}")
    if data is not None:
        req.add_header("Content-Type", "application/json; charset=utf-8")
    try:
        with urllib.request.urlopen(req, timeout=600) as r:
            payload = r.read()
            return r.status, dict(r.headers), payload if raw else (json.loads(payload) if payload else None)
    except urllib.error.HTTPError as e:
        payload = e.read()
        try:
            parsed = json.loads(payload) if payload else None
        except ValueError:
            parsed = payload.decode(errors="replace")
        return e.code, dict(e.headers), parsed


def load_template(name):
    return json.loads((TEMPLATES / f"{name}.json").read_text(encoding="utf-8"))


def save_template(tok, name, definition, template_id=None):
    template_id = template_id or str(uuid.uuid5(uuid.NAMESPACE_URL, f"rt108/{name}"))
    return call("PUT", f"/api/reports/templates/{template_id}", tok, {"name": name, "definition": definition})


def run(tok, template_id, parameters, formats=("pdf",), timeout=900):
    status, headers, started = call("POST", "/api/reports/runs", tok, {"templateId": template_id, "parameters": parameters, "formats": list(formats)})
    if status != 202:
        raise RuntimeError(f"run not accepted: {status} {started}")
    deadline = time.time() + timeout
    while time.time() < deadline:
        _, _, current = call("GET", f"/api/reports/runs/{started['id']}", tok)
        if current["status"] in ("Succeeded", "Failed"):
            return current
        time.sleep(1)
    raise TimeoutError(f"run {started['id']} did not finish in {timeout}s")


def download(tok, run_id, fmt, target):
    status, _, payload = call("GET", f"/api/reports/runs/{run_id}/{fmt}", tok, raw=True)
    if status != 200:
        raise RuntimeError(f"download failed: {status}")
    pathlib.Path(target).write_bytes(payload)
    return target
