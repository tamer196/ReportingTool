/**
 * Our own report layout model (RT-108, revised ADR-001 option 1). It mirrors
 * ReportingTool.Reports.Models.Layout on the server: the designer edits it, the renderer prints it.
 * Positions are logical (from the inline start), so switching direction mirrors the layout.
 */
export type Direction = 'ltr' | 'rtl';
export type Align = 'start' | 'center' | 'end';
export type Format = 'text' | 'date' | 'dateLong' | 'amount' | 'integer';

export interface TextStyle {
  fontSizePt: number;
  bold: boolean;
  align: Align;
}

export interface BandItem {
  id: string;
  kind: 'text' | 'line';
  xMm: number;
  yMm: number;
  widthMm: number;
  heightMm: number;
  text?: string | null;
  style?: TextStyle | null;
}

export interface Band {
  heightMm: number;
  items: BandItem[];
}

export interface PageSetup {
  size: 'A4' | 'Letter';
  orientation: 'portrait' | 'landscape';
  marginTopMm: number;
  marginBottomMm: number;
  marginStartMm: number;
  marginEndMm: number;
}

export interface TableColumn {
  field: string;
  header: string;
  widthPct: number;
  format: Format;
  total?: boolean;
}

export interface TextComponent {
  type: 'text';
  id: string;
  text: string;
  style: TextStyle;
  heading?: boolean;
}

export interface TableComponent {
  type: 'table';
  id: string;
  title?: string | null;
  columns: TableColumn[];
  groupBy?: string | null;
  groupHeader?: string | null;
  pageBreakPerGroup?: boolean;
  grandTotal?: boolean;
}

export interface ChartComponent {
  type: 'chart';
  id: string;
  title: string;
  chartType: 'bar' | 'line' | 'pie';
  categoryField: string;
  valueField: string;
  heightMm: number;
}

export interface KpiComponent {
  type: 'kpi';
  id: string;
  label: string;
  valueField: string;
  aggregate: 'sum' | 'count' | 'avg';
  format: Format;
}

export interface PageBreakComponent {
  type: 'pageBreak';
  id: string;
}

export type BodyComponent =
  TextComponent | TableComponent | ChartComponent | KpiComponent | PageBreakComponent;
export type ComponentType = BodyComponent['type'];

export interface ReportLayout {
  direction: Direction;
  culture: string;
  page: PageSetup;
  pageHeader: Band;
  pageFooter: Band;
  pageNumbering: 'document' | 'perGroup';
  watermark?: string | null;
  body: BodyComponent[];
}

export interface BindingFilter {
  field: string;
  op: 'eq' | 'gte' | 'lte';
  parameter: string;
}

export interface ComponentBinding {
  componentId: string;
  datasetId: string;
  filters: BindingFilter[];
  sort: string[];
}

export interface ReportParameter {
  name: string;
  label: string;
  type: 'text' | 'date' | 'int';
  default?: string | null;
  required?: boolean;
}

export interface ReportDefinition {
  schemaVersion: 1;
  engine: string;
  layout: ReportLayout;
  bindings: ComponentBinding[];
  parameters: ReportParameter[];
}

/** A dataset as the signed-in author may see it: hidden fields never arrive here (G3). */
export interface DatasetSchema {
  id: string;
  name: string;
  nameAr: string;
  fields: FieldSchema[];
}

export interface FieldSchema {
  key: string;
  label: string;
  labelAr: string;
  dataType: 'text' | 'number' | 'date';
  masked: boolean;
}

export interface TemplateSummary {
  id: string;
  name: string;
  version: number;
  updatedAtUtc: string;
}

export interface TemplateResponse extends TemplateSummary {
  definition: ReportDefinition;
}

export interface RunResponse {
  id: string;
  templateId: string;
  templateVersion: number;
  status: 'Queued' | 'Running' | 'Succeeded' | 'Failed';
  error: string | null;
  pages: number | null;
  rows: number | null;
  hasPdf: boolean;
  hasXlsx: boolean;
  renderedBy: string | null;
}
