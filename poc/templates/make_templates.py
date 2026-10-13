"""Writes the RT-108 PoC sample templates (our own layout JSON) next to this script."""
import copy
import json
import pathlib


def style(pt=9, bold=False, align="start"):
    return {"fontSizePt": pt, "bold": bold, "align": align}


PAGE = {"size": "A4", "orientation": "portrait", "marginTopMm": 10, "marginBottomMm": 10, "marginStartMm": 12, "marginEndMm": 12}
ENGINE = "rt-html-chromium/0.1-poc"

HEADER_AR = {"heightMm": 18, "items": [
    {"id": "h1", "kind": "text", "xMm": 0, "yMm": 0, "widthMm": 110, "heightMm": 8, "text": "{Report Name}", "style": style(13, True)},
    {"id": "h2", "kind": "text", "xMm": 0, "yMm": 8, "widthMm": 110, "heightMm": 6, "text": "شركة الوساطة التجريبية — Demo Brokerage Ltd.", "style": style(8.5)},
    {"id": "h3", "kind": "text", "xMm": 110, "yMm": 0, "widthMm": 76, "heightMm": 6, "text": "تاريخ التشغيل: {Run Date}", "style": style(8, False, "end")},
    {"id": "h4", "kind": "text", "xMm": 110, "yMm": 6, "widthMm": 76, "heightMm": 6, "text": "أُعدّ بواسطة: {Run By}", "style": style(8, False, "end")},
    {"id": "h5", "kind": "line", "xMm": 0, "yMm": 16, "widthMm": 186, "heightMm": 1},
]}

FOOTER_AR_GROUP = {"heightMm": 10, "items": [
    {"id": "f0", "kind": "line", "xMm": 0, "yMm": 0, "widthMm": 186, "heightMm": 1},
    {"id": "f1", "kind": "text", "xMm": 0, "yMm": 2, "widthMm": 120, "heightMm": 6, "text": "{Group}", "style": style(8)},
    {"id": "f2", "kind": "text", "xMm": 120, "yMm": 2, "widthMm": 66, "heightMm": 6, "text": "صفحة {Page} من {Total Pages}", "style": style(8, True, "end")},
]}

FOOTER_AR_DOC = {"heightMm": 10, "items": [
    {"id": "f0", "kind": "line", "xMm": 0, "yMm": 0, "widthMm": 186, "heightMm": 1},
    {"id": "f1", "kind": "text", "xMm": 0, "yMm": 2, "widthMm": 120, "heightMm": 6, "text": "سري — للاستخدام الداخلي فقط · {Parameter: ToClient}", "style": style(8)},
    {"id": "f2", "kind": "text", "xMm": 120, "yMm": 2, "widthMm": 66, "heightMm": 6, "text": "صفحة {Page} من {Total Pages}", "style": style(8, True, "end")},
]}


def binding(component_id, sort, extra_filters=()):
    filters = [{"field": "ClientId", "op": "lte", "parameter": "ToClient"}, *extra_filters]
    return {"componentId": component_id, "datasetId": "client-transactions", "filters": filters, "sort": sort}


statement = {
    "schemaVersion": 1, "engine": ENGINE,
    "layout": {
        "direction": "rtl", "culture": "ar-AE", "page": PAGE, "pageHeader": HEADER_AR, "pageFooter": FOOTER_AR_GROUP,
        "pageNumbering": "perGroup", "watermark": "سري — Confidential",
        "body": [{
            "type": "table", "id": "stmt", "groupBy": "ClientId", "pageBreakPerGroup": True, "grandTotal": False,
            "groupHeader": "{Field: ClientNameAr} — {Field: ClientNameEn} — {Field: ClientId} — IBAN {Field: Iban}",
            "columns": [
                {"field": "TradeDate", "header": "التاريخ", "widthPct": 13, "format": "date"},
                {"field": "Instrument", "header": "الأداة", "widthPct": 17, "format": "text"},
                {"field": "Side", "header": "الاتجاه", "widthPct": 8, "format": "text"},
                {"field": "Notes", "header": "البيان", "widthPct": 44, "format": "text"},
                {"field": "Amount", "header": "المبلغ (USD)", "widthPct": 18, "format": "amount", "total": True},
            ],
        }],
    },
    "bindings": [binding("stmt", ["ClientId", "TradeDate"], [{"field": "Branch", "op": "eq", "parameter": "Branch"}])],
    "parameters": [
        {"name": "ToClient", "label": "حتى العميل", "type": "text", "default": "CL-00003", "required": True},
        {"name": "Branch", "label": "الفرع", "type": "text", "required": False},
    ],
}

grouped = {
    "schemaVersion": 1, "engine": ENGINE,
    "layout": {
        "direction": "rtl", "culture": "ar-AE", "page": PAGE, "pageHeader": HEADER_AR, "pageFooter": FOOTER_AR_DOC,
        "pageNumbering": "document",
        "body": [
            {"type": "text", "id": "t1", "heading": True, "text": "ملخص الحركات حسب مدير الحساب — Transactions by account manager", "style": style(14, True)},
            {"type": "text", "id": "t2", "style": style(9.5),
             "text": "يعرض هذا التقرير حركات العملاء حتى العميل {Parameter: ToClient}، مجمّعة حسب مدير الحساب مع المجاميع الفرعية. "
                     "الأرقام بالدولار الأمريكي (USD) مثل ‎-1,234.50‎، والتواريخ بصيغة ISO 8601 مثل 2026-10-13، "
                     "وأرقام عربية هندية مثل ١٢٣٤٥. تاريخ الإعداد: {Run Date}."},
            {"type": "kpi", "id": "k1", "label": "إجمالي المبالغ (USD)", "valueField": "Amount", "aggregate": "sum", "format": "amount"},
            {"type": "kpi", "id": "k2", "label": "عدد الحركات", "valueField": "Amount", "aggregate": "count", "format": "integer"},
            {"type": "chart", "id": "c1", "title": "المبالغ حسب الأداة", "chartType": "bar", "categoryField": "Instrument", "valueField": "Amount", "heightMm": 62},
            {"type": "table", "id": "tbl", "title": "تفاصيل الحركات", "groupBy": "AccountManager", "pageBreakPerGroup": False, "grandTotal": True,
             "groupHeader": "مدير الحساب: {Field: AccountManager}",
             "columns": [
                 {"field": "ClientId", "header": "رقم العميل", "widthPct": 11, "format": "text"},
                 {"field": "ClientNameAr", "header": "العميل", "widthPct": 25, "format": "text"},
                 {"field": "TradeDate", "header": "التاريخ", "widthPct": 12, "format": "date"},
                 {"field": "Instrument", "header": "الأداة", "widthPct": 17, "format": "text"},
                 {"field": "Side", "header": "الاتجاه", "widthPct": 8, "format": "text"},
                 {"field": "Quantity", "header": "الكمية", "widthPct": 10, "format": "amount"},
                 {"field": "Amount", "header": "المبلغ (USD)", "widthPct": 17, "format": "amount", "total": True},
             ]},
        ],
    },
    "bindings": [binding("k1", []), binding("k2", []), binding("c1", []), binding("tbl", ["AccountManager", "ClientId", "TradeDate"])],
    "parameters": [{"name": "ToClient", "label": "حتى العميل", "type": "text", "default": "CL-00070", "required": True}],
}

# The same layout with direction ltr and English labels: shows that positions mirror (G1).
grouped_en = copy.deepcopy(grouped)
layout = grouped_en["layout"]
layout["direction"], layout["culture"] = "ltr", "en-GB"
body = layout["body"]
body[0]["text"] = "Transactions by account manager — ملخص الحركات حسب مدير الحساب"
body[1]["text"] = ("Client transactions up to client {Parameter: ToClient}, grouped by account manager with subtotals. "
                   "Amounts in USD such as -1,234.50; dates in ISO 8601 such as 2026-10-13. Prepared {Run Date}.")
body[2]["label"], body[3]["label"], body[4]["title"], body[5]["title"] = "Total amount (USD)", "Transactions", "Amount by instrument", "Transaction details"
body[5]["groupHeader"] = "Account manager: {Field: AccountManager}"
for column, header in zip(body[5]["columns"], ["Client ID", "Client", "Date", "Instrument", "Side", "Quantity", "Amount (USD)"]):
    column["header"] = header
layout["pageHeader"]["items"][2]["text"] = "Run: {Run Date}"
layout["pageHeader"]["items"][3]["text"] = "Prepared by: {Run By}"
layout["pageFooter"]["items"][1]["text"] = "Confidential — internal use only · {Parameter: ToClient}"
layout["pageFooter"]["items"][2]["text"] = "Page {Page} of {Total Pages}"
grouped_en["parameters"][0]["label"] = "Up to client"

listing = {
    "schemaVersion": 1, "engine": ENGINE,
    "layout": {
        "direction": "ltr", "culture": "en-GB", "page": PAGE, "pageNumbering": "document",
        "pageHeader": {"heightMm": 10, "items": [
            {"id": "h1", "kind": "text", "xMm": 0, "yMm": 0, "widthMm": 186, "heightMm": 7, "text": "{Report Name} — up to {Parameter: ToClient}", "style": style(11, True)},
            {"id": "h2", "kind": "line", "xMm": 0, "yMm": 8, "widthMm": 186, "heightMm": 1}]},
        "pageFooter": {"heightMm": 8, "items": [
            {"id": "f1", "kind": "text", "xMm": 0, "yMm": 1, "widthMm": 186, "heightMm": 6, "text": "Page {Page} of {Total Pages} · {Run Date}", "style": style(8, False, "end")}]},
        "body": [{"type": "table", "id": "lst", "pageBreakPerGroup": False, "grandTotal": False, "columns": [
            {"field": "ClientId", "header": "Client ID", "widthPct": 10, "format": "text"},
            {"field": "ClientNameEn", "header": "Client", "widthPct": 24, "format": "text"},
            {"field": "Branch", "header": "Branch", "widthPct": 8, "format": "text"},
            {"field": "TradeDate", "header": "Date", "widthPct": 11, "format": "date"},
            {"field": "Instrument", "header": "Instrument", "widthPct": 17, "format": "text"},
            {"field": "Side", "header": "Side", "widthPct": 7, "format": "text"},
            {"field": "Iban", "header": "IBAN (masked)", "widthPct": 23, "format": "text"},
        ]}],
    },
    "bindings": [binding("lst", ["ClientId", "TradeDate"])],
    "parameters": [{"name": "ToClient", "label": "Up to client", "type": "text", "default": "CL-00100", "required": True}],
}

here = pathlib.Path(__file__).parent
for name, definition in [("statement-ar", statement), ("grouped-summary-ar", grouped), ("grouped-summary-en", grouped_en), ("listing-en", listing)]:
    (here / f"{name}.json").write_text(json.dumps(definition, ensure_ascii=False, indent=1), encoding="utf-8")
print("templates written")
