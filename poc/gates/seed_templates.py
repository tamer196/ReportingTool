"""Uploads the sample templates as author.ar (and the English listing as author.en)."""
import json

import rtpoc

NAMES = {
    "statement-ar": "كشف حساب العميل",
    "grouped-summary-ar": "ملخص الحركات حسب مدير الحساب",
    "grouped-summary-en": "Transactions by account manager",
    "listing-en": "Transaction listing",
}

if __name__ == "__main__":
    ids = {}
    tok = rtpoc.token("author.ar")
    for key, name in NAMES.items():
        status, headers, body = rtpoc.save_template(tok, name, rtpoc.load_template(key), template_id=None)
        print(key, status, headers.get("X-Served-By"), body if status != 200 else f"v{body['version']} {body['id']}")
        if status == 200:
            ids[key] = body["id"]
    (rtpoc.EVIDENCE / "template-ids.json").write_text(json.dumps(ids, indent=1), encoding="utf-8")
