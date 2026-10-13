import {
  BandItem,
  BodyComponent,
  ComponentBinding,
  ComponentType,
  DatasetSchema,
  Direction,
  PageSetup,
  ReportDefinition,
} from '../reports/report.model';

/** CSS pixels per millimetre at 100 % zoom. */
export const PX_PER_MM = 96 / 25.4;

export const ENGINE = 'rt-html-chromium/0.1-poc';

export const COMPONENT_LABELS: Record<ComponentType, string> = {
  text: 'Text box',
  table: 'Table',
  chart: 'Chart',
  kpi: 'KPI tile',
  pageBreak: 'Page break',
};

export function newId(prefix: string): string {
  return `${prefix}-${crypto.randomUUID().slice(0, 8)}`;
}

export function pageSizeMm(page: PageSetup): { w: number; h: number } {
  const [w, h] = page.size === 'Letter' ? [215.9, 279.4] : [210, 297];
  return page.orientation === 'landscape' ? { w: h, h: w } : { w, h };
}

/** Same font order as the renderer, so the canvas and the PDF pick the same glyphs. */
export function fontStack(direction: Direction): string {
  return direction === 'rtl'
    ? '"Noto Sans Arabic", "Noto Sans", sans-serif'
    : '"Noto Sans", "Noto Sans Arabic", sans-serif';
}

/** A blank report: a title, page number footer, nothing bound yet. */
export function starterDefinition(direction: Direction): ReportDefinition {
  const rtl = direction === 'rtl';
  return {
    schemaVersion: 1,
    engine: ENGINE,
    layout: {
      direction,
      culture: rtl ? 'ar-AE' : 'en-GB',
      page: {
        size: 'A4',
        orientation: 'portrait',
        marginTopMm: 10,
        marginBottomMm: 10,
        marginStartMm: 12,
        marginEndMm: 12,
      },
      pageHeader: {
        heightMm: 14,
        items: [
          {
            id: newId('h'),
            kind: 'text',
            xMm: 0,
            yMm: 0,
            widthMm: 120,
            heightMm: 8,
            text: '{Report Name}',
            style: { fontSizePt: 13, bold: true, align: 'start' },
          },
          { id: newId('h'), kind: 'line', xMm: 0, yMm: 12, widthMm: 186, heightMm: 1 },
        ],
      },
      pageFooter: {
        heightMm: 8,
        items: [
          {
            id: newId('f'),
            kind: 'text',
            xMm: 120,
            yMm: 1,
            widthMm: 66,
            heightMm: 6,
            text: rtl ? 'صفحة {Page} من {Total Pages}' : 'Page {Page} of {Total Pages}',
            style: { fontSizePt: 8, bold: false, align: 'end' },
          },
        ],
      },
      pageNumbering: 'document',
      body: [],
    },
    bindings: [],
    parameters: [
      {
        name: 'ToClient',
        label: rtl ? 'حتى العميل' : 'Up to client',
        type: 'text',
        default: 'CL-00020',
        required: true,
      },
    ],
  };
}

/** A new body component with sensible defaults, bound to the first granted dataset. */
export function createComponent(
  type: ComponentType,
  schema: DatasetSchema | undefined,
  direction: Direction,
): BodyComponent {
  const rtl = direction === 'rtl';
  const fields = schema?.fields ?? [];
  const numeric = fields.find((f) => f.dataType === 'number')?.key ?? '';
  const category = fields.find((f) => f.dataType === 'text' && !f.masked)?.key ?? '';
  switch (type) {
    case 'text':
      return {
        type,
        id: newId('txt'),
        text: rtl ? 'نص جديد' : 'New text',
        style: { fontSizePt: 10, bold: false, align: 'start' },
      };
    case 'table':
      return {
        type,
        id: newId('tbl'),
        title: null,
        columns: fields
          .filter((f) => !f.masked)
          .slice(0, 4)
          .map((f) => ({
            field: f.key,
            header: rtl ? f.labelAr : f.label,
            widthPct: 25,
            format: f.dataType === 'number' ? 'amount' : f.dataType === 'date' ? 'date' : 'text',
          })),
        groupBy: null,
        groupHeader: null,
        pageBreakPerGroup: false,
        grandTotal: false,
      };
    case 'chart':
      return {
        type,
        id: newId('cht'),
        title: rtl ? 'مخطط' : 'Chart',
        chartType: 'bar',
        categoryField: category,
        valueField: numeric,
        heightMm: 60,
      };
    case 'kpi':
      return {
        type,
        id: newId('kpi'),
        label: rtl ? 'المجموع' : 'Total',
        valueField: numeric,
        aggregate: 'sum',
        format: 'amount',
      };
    case 'pageBreak':
      return { type, id: newId('pb') };
  }
}

/** Data components need a binding. A new one reuses the filters of an existing binding to the same dataset, so it follows the report parameters. */
export function bindingFor(
  definition: ReportDefinition,
  componentId: string,
  datasetId: string,
): ComponentBinding {
  const sibling = definition.bindings.find((b) => b.datasetId === datasetId);
  return {
    componentId,
    datasetId,
    filters: sibling ? structuredClone(sibling.filters) : [],
    sort: sibling ? [...sibling.sort] : [],
  };
}

export function needsBinding(component: BodyComponent): boolean {
  return component.type === 'table' || component.type === 'chart' || component.type === 'kpi';
}

/** Moves or resizes a band item, keeping it inside its band. */
export function nudge(
  item: BandItem,
  band: { heightMm: number },
  innerWidthMm: number,
  dx: number,
  dy: number,
  resize: boolean,
): BandItem {
  const clamp = (v: number, min: number, max: number) =>
    Math.round(Math.min(Math.max(v, min), max) * 10) / 10;
  if (resize) {
    return {
      ...item,
      widthMm: clamp(item.widthMm + dx, 5, innerWidthMm - item.xMm),
      heightMm: clamp(item.heightMm + dy, 1, band.heightMm - item.yMm),
    };
  }
  return {
    ...item,
    xMm: clamp(item.xMm + dx, 0, innerWidthMm - item.widthMm),
    yMm: clamp(item.yMm + dy, 0, band.heightMm - item.heightMm),
  };
}

export function describe(component: BodyComponent): string {
  switch (component.type) {
    case 'text':
      return `${COMPONENT_LABELS.text}: ${component.text}`;
    case 'table':
      return `${COMPONENT_LABELS.table}${component.title ? ` "${component.title}"` : ''}, ${component.columns.length} columns${component.groupBy ? `, grouped by ${component.groupBy}` : ''}`;
    case 'chart':
      return `${COMPONENT_LABELS.chart} "${component.title}"`;
    case 'kpi':
      return `${COMPONENT_LABELS.kpi} "${component.label}"`;
    case 'pageBreak':
      return COMPONENT_LABELS.pageBreak;
  }
}
