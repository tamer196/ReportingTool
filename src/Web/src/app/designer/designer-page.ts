import {
  CdkDrag,
  CdkDragDrop,
  CdkDragEnd,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { HttpErrorResponse } from '@angular/common/http';
import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

import {
  BandItem,
  BodyComponent,
  ChartComponent,
  ComponentType,
  DatasetSchema,
  FieldSchema,
  Format,
  KpiComponent,
  ReportDefinition,
  TableComponent,
  TemplateSummary,
  TextComponent,
  TextStyle,
} from '../reports/report.model';
import { ReportsApiService } from '../reports/reports-api.service';
import {
  COMPONENT_LABELS,
  PX_PER_MM,
  bindingFor,
  createComponent,
  describe,
  fontStack,
  needsBinding,
  newId,
  nudge,
  pageSizeMm,
  starterDefinition,
} from './designer-ops';

type BandName = 'pageHeader' | 'pageFooter';
type Selection = { area: 'body'; id: string } | { area: BandName; id: string } | null;

const HISTORY_LIMIT = 100;

/**
 * RT-108 PoC report designer: our own canvas on Angular CDK drag and drop (MIT). It edits our
 * layout model directly; the server prints exactly that model, so preview and output match.
 * Every pointer action has a keyboard alternative (#94 AC2): palette buttons add components,
 * arrow keys move header/footer items (Alt resizes), Alt+Up/Down reorders the body, Delete
 * removes, Ctrl+Z / Ctrl+Y undo and redo. Every change is announced in a live region.
 */
@Component({
  selector: 'app-designer-page',
  imports: [CdkDropList, CdkDrag, NgTemplateOutlet],
  templateUrl: './designer-page.html',
  styleUrl: './designer-page.css',
  // Undo/redo shortcuts for the whole page (inputs keep their own text undo).
  host: { '(keydown)': 'onDesignerKey($event)' },
})
export class DesignerPage {
  private readonly api = inject(ReportsApiService);
  private readonly sanitizer = inject(DomSanitizer);
  private undoStack: ReportDefinition[] = [];
  private redoStack: ReportDefinition[] = [];
  private objectUrl: string | null = null;

  protected readonly labels = COMPONENT_LABELS;
  protected readonly paletteTypes: ComponentType[] = ['text', 'table', 'chart', 'kpi', 'pageBreak'];
  protected readonly formats: Format[] = ['text', 'date', 'dateLong', 'amount', 'integer'];
  protected readonly describe = describe;

  protected readonly datasets = signal<DatasetSchema[]>([]);
  protected readonly templates = signal<TemplateSummary[]>([]);
  protected readonly templateId = signal<string | null>(null);
  protected readonly name = signal('Untitled report');
  protected readonly def = signal<ReportDefinition>(starterDefinition('rtl'));
  protected readonly selection = signal<Selection>(null);
  protected readonly zoom = signal(1);
  protected readonly status = signal(
    'Designer ready. Use the palette buttons or drag components onto the page.',
  );
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);
  protected readonly previewUrl = signal<SafeResourceUrl | null>(null);
  protected readonly paramValues = signal<Record<string, string>>({});
  protected readonly canUndo = signal(false);
  protected readonly canRedo = signal(false);

  protected readonly layout = computed(() => this.def().layout);
  protected readonly rtl = computed(() => this.layout().direction === 'rtl');
  protected readonly fonts = computed(() => fontStack(this.layout().direction));
  protected readonly page = computed(() => pageSizeMm(this.layout().page));
  protected readonly innerWidth = computed(
    () => this.page().w - this.layout().page.marginStartMm - this.layout().page.marginEndMm,
  );

  protected readonly selectedComponent = computed(() => {
    const s = this.selection();
    return s?.area === 'body' ? (this.layout().body.find((c) => c.id === s.id) ?? null) : null;
  });

  protected readonly selectedBandItem = computed(() => {
    const s = this.selection();
    return s && s.area !== 'body'
      ? (this.layout()[s.area].items.find((i) => i.id === s.id) ?? null)
      : null;
  });

  protected readonly selectedBand = computed<BandName | null>(() => {
    const s = this.selection();
    return s && s.area !== 'body' ? s.area : null;
  });

  protected readonly selectedBinding = computed(() => {
    const c = this.selectedComponent();
    return c ? (this.def().bindings.find((b) => b.componentId === c.id) ?? null) : null;
  });

  protected readonly selectedSchema = computed(() => {
    const b = this.selectedBinding();
    return b ? (this.datasets().find((d) => d.id === b.datasetId) ?? null) : null;
  });

  constructor() {
    this.api.datasets().subscribe((d) => this.datasets.set(d));
    this.refreshTemplates();
    this.resetParams();
  }

  // ----- templates -----

  protected refreshTemplates(): void {
    this.api.templates().subscribe((t) => this.templates.set(t));
  }

  protected open(id: string): void {
    if (!id) return;
    this.api.template(id).subscribe((t) => {
      this.templateId.set(t.id);
      this.name.set(t.name);
      this.def.set(t.definition);
      this.undoStack = [];
      this.redoStack = [];
      this.syncHistory();
      this.selection.set(null);
      this.resetParams();
      this.announce(`Opened "${t.name}", version ${t.version}.`);
    });
  }

  protected newReport(direction: 'ltr' | 'rtl'): void {
    this.templateId.set(null);
    this.name.set(direction === 'rtl' ? 'تقرير جديد' : 'New report');
    this.def.set(starterDefinition(direction));
    this.selection.set(null);
    this.resetParams();
    this.announce('New blank report.');
  }

  protected save(after?: (id: string) => void): void {
    const id = this.templateId() ?? crypto.randomUUID();
    this.busy.set(true);
    this.errors.set([]);
    this.api.save(id, this.name(), this.def()).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.templateId.set(saved.id);
        this.refreshTemplates();
        this.announce(`Saved "${saved.name}", version ${saved.version}.`);
        after?.(saved.id);
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  protected preview(): void {
    this.save((id) => {
      this.busy.set(true);
      this.announce('Rendering the PDF preview…');
      this.api.run(id, this.paramValues(), ['pdf']).subscribe({
        next: (run) => {
          if (run.status !== 'Succeeded') {
            this.busy.set(false);
            this.errors.set([run.error ?? 'The run failed.']);
            this.announce('The preview failed.');
            return;
          }
          this.api.output(run.id, 'pdf').subscribe((blob) => {
            this.busy.set(false);
            if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
            this.objectUrl = URL.createObjectURL(blob);
            this.previewUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.objectUrl));
            this.announce(`Preview ready: ${run.pages} pages, rendered by ${run.renderedBy}.`);
          });
        },
        error: (e: HttpErrorResponse) => this.fail(e),
      });
    });
  }

  protected exportXlsx(): void {
    this.save((id) => {
      this.busy.set(true);
      this.api.run(id, this.paramValues(), ['xlsx']).subscribe({
        next: (run) => {
          if (run.status !== 'Succeeded') {
            this.busy.set(false);
            this.errors.set([run.error ?? 'The run failed.']);
            return;
          }
          this.api.output(run.id, 'xlsx').subscribe((blob) => {
            this.busy.set(false);
            const link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = `${this.name()}.xlsx`;
            link.click();
            URL.revokeObjectURL(link.href);
            this.announce('Excel file downloaded.');
          });
        },
        error: (e: HttpErrorResponse) => this.fail(e),
      });
    });
  }

  protected closePreview(): void {
    this.previewUrl.set(null);
  }

  // ----- editing with undo/redo -----

  protected edit(change: (d: ReportDefinition) => void, message?: string): void {
    this.undoStack.push(this.def());
    if (this.undoStack.length > HISTORY_LIMIT) this.undoStack.shift();
    this.redoStack = [];
    const next = structuredClone(this.def());
    change(next);
    this.def.set(next);
    this.syncHistory();
    if (message) this.announce(message);
  }

  protected undo(): void {
    const previous = this.undoStack.pop();
    if (!previous) return;
    this.redoStack.push(this.def());
    this.def.set(previous);
    this.syncHistory();
    this.announce('Undone.');
  }

  protected redo(): void {
    const next = this.redoStack.pop();
    if (!next) return;
    this.undoStack.push(this.def());
    this.def.set(next);
    this.syncHistory();
    this.announce('Redone.');
  }

  protected onDesignerKey(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    if (
      ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) ||
      !(event.ctrlKey || event.metaKey)
    )
      return;
    if (event.key === 'z' && !event.shiftKey) {
      event.preventDefault();
      this.undo();
    } else if (event.key === 'y' || (event.key === 'z' && event.shiftKey)) {
      event.preventDefault();
      this.redo();
    }
  }

  // ----- body components -----

  protected add(type: ComponentType, index = this.layout().body.length): void {
    const dataset = this.datasets()[0];
    const component = createComponent(type, dataset, this.layout().direction);
    this.edit(
      (d) => {
        d.layout.body.splice(index, 0, component);
        if (needsBinding(component) && dataset)
          d.bindings.push(bindingFor(d, component.id, dataset.id));
      },
      `${COMPONENT_LABELS[type]} added at position ${index + 1} of ${this.layout().body.length + 1}.`,
    );
    this.selection.set({ area: 'body', id: component.id });
    queueMicrotask(() => document.getElementById(`comp-${component.id}`)?.focus());
  }

  protected dropOnBody(event: CdkDragDrop<BodyComponent[]>): void {
    if (event.previousContainer === event.container) {
      if (event.previousIndex === event.currentIndex) return;
      this.edit(
        (d) => moveItemInArray(d.layout.body, event.previousIndex, event.currentIndex),
        `Moved to position ${event.currentIndex + 1}.`,
      );
    } else {
      this.add(event.item.data as ComponentType, event.currentIndex);
    }
  }

  protected onComponentKey(index: number, component: BodyComponent, event: KeyboardEvent): void {
    const last = this.layout().body.length - 1;
    if (event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault();
      const to = Math.min(Math.max(index + (event.key === 'ArrowUp' ? -1 : 1), 0), last);
      if (to === index) return;
      this.edit(
        (d) => moveItemInArray(d.layout.body, index, to),
        `${describe(component)} moved to position ${to + 1} of ${last + 1}.`,
      );
      queueMicrotask(() => document.getElementById(`comp-${component.id}`)?.focus());
    } else if (event.key === 'Delete') {
      event.preventDefault();
      this.removeComponent(component);
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.selectComponent(component);
    }
  }

  protected selectComponent(component: BodyComponent): void {
    this.selection.set({ area: 'body', id: component.id });
    this.announce(
      `Selected ${describe(component)}. Alt+Up or Alt+Down moves it, Delete removes it.`,
    );
  }

  protected removeComponent(component: BodyComponent): void {
    this.edit(
      (d) => {
        d.layout.body = d.layout.body.filter((c) => c.id !== component.id);
        d.bindings = d.bindings.filter((b) => b.componentId !== component.id);
      },
      `${describe(component)} removed.`,
    );
    this.selection.set(null);
  }

  protected updateComponent<T extends BodyComponent>(
    component: T,
    patch: Partial<T>,
    message?: string,
  ): void {
    this.edit((d) => {
      const i = d.layout.body.findIndex((c) => c.id === component.id);
      d.layout.body[i] = { ...d.layout.body[i], ...patch } as BodyComponent;
    }, message);
  }

  protected setDataset(component: BodyComponent, datasetId: string): void {
    this.edit((d) => {
      d.bindings = d.bindings.filter((b) => b.componentId !== component.id);
      d.bindings.push(bindingFor(d, component.id, datasetId));
    }, 'Data source changed.');
  }

  protected toggleColumn(table: TableComponent, field: FieldSchema, on: boolean): void {
    const columns = on
      ? [
          ...table.columns,
          {
            field: field.key,
            header: this.rtl() ? field.labelAr : field.label,
            widthPct: 15,
            format: this.defaultFormat(field),
          },
        ]
      : table.columns.filter((c) => c.field !== field.key);
    this.updateComponent(
      table,
      { columns },
      on ? `Column ${field.label} added.` : `Column ${field.label} removed.`,
    );
  }

  protected updateColumn(
    table: TableComponent,
    index: number,
    patch: Partial<TableComponent['columns'][number]>,
  ): void {
    const columns = table.columns.map((c, i) => (i === index ? { ...c, ...patch } : c));
    this.updateComponent(table, { columns });
  }

  protected hasColumn(table: TableComponent, key: string): boolean {
    return table.columns.some((c) => c.field === key);
  }

  // ----- header and footer bands (free-form) -----

  protected addBandText(band: BandName): void {
    const item: BandItem = {
      id: newId(band === 'pageHeader' ? 'h' : 'f'),
      kind: 'text',
      xMm: 0,
      yMm: 0,
      widthMm: 60,
      heightMm: 6,
      text: '{Run Date}',
      style: { fontSizePt: 8, bold: false, align: 'start' },
    };
    this.edit(
      (d) => d.layout[band].items.push(item),
      `Text added to the ${band === 'pageHeader' ? 'page header' : 'page footer'}.`,
    );
    this.selection.set({ area: band, id: item.id });
    queueMicrotask(() => document.getElementById(`item-${item.id}`)?.focus());
  }

  protected selectBandItem(band: BandName, item: BandItem): void {
    this.selection.set({ area: band, id: item.id });
    this.announce(
      `Selected ${this.itemLabel(item)}. Arrow keys move it, Alt+arrow keys resize it, Delete removes it.`,
    );
  }

  protected onBandItemKey(band: BandName, item: BandItem, event: KeyboardEvent): void {
    const step = event.shiftKey ? 5 : 1;
    const visual: Record<string, [number, number]> = {
      ArrowLeft: [-step, 0],
      ArrowRight: [step, 0],
      ArrowUp: [0, -step],
      ArrowDown: [0, step],
    };
    if (event.key in visual) {
      event.preventDefault();
      const [vx, dy] = visual[event.key];
      // Positions are logical (from the inline start), so in RTL "right" means a smaller offset.
      this.moveBandItem(band, item, this.rtl() ? -vx : vx, dy, event.altKey);
    } else if (event.key === 'Delete') {
      event.preventDefault();
      this.edit(
        (d) => (d.layout[band].items = d.layout[band].items.filter((i) => i.id !== item.id)),
        `${this.itemLabel(item)} removed.`,
      );
      this.selection.set(null);
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.selectBandItem(band, item);
    }
  }

  protected onBandItemDragEnded(band: BandName, item: BandItem, event: CdkDragEnd): void {
    const scale = PX_PER_MM * this.zoom();
    const dx = event.distance.x / scale;
    event.source.reset();
    this.moveBandItem(band, item, this.rtl() ? -dx : dx, event.distance.y / scale, false);
  }

  protected updateBandItem(band: BandName, item: BandItem, patch: Partial<BandItem>): void {
    this.edit((d) => {
      const items = d.layout[band].items;
      const i = items.findIndex((x) => x.id === item.id);
      items[i] = { ...items[i], ...patch };
    });
  }

  protected updateItemStyle(band: BandName, item: BandItem, patch: Partial<TextStyle>): void {
    this.updateBandItem(band, item, {
      style: { ...(item.style ?? { fontSizePt: 8, bold: false, align: 'start' }), ...patch },
    });
  }

  protected itemLabel(item: BandItem): string {
    return item.kind === 'line' ? 'Line' : `Text "${item.text ?? ''}"`;
  }

  private moveBandItem(
    band: BandName,
    item: BandItem,
    dx: number,
    dy: number,
    resize: boolean,
  ): void {
    const moved = nudge(item, this.layout()[band], this.innerWidth(), dx, dy, resize);
    this.edit(
      (d) => {
        const items = d.layout[band].items;
        items[items.findIndex((x) => x.id === item.id)] = moved;
      },
      resize
        ? `${this.itemLabel(item)} resized to ${moved.widthMm} by ${moved.heightMm} mm.`
        : `${this.itemLabel(item)} moved to ${moved.xMm} mm from the ${this.rtl() ? 'right' : 'left'}, ${moved.yMm} mm from the top.`,
    );
  }

  // ----- report settings and parameters -----

  protected setDirection(direction: 'ltr' | 'rtl'): void {
    this.edit(
      (d) => {
        d.layout.direction = direction;
        d.layout.culture = direction === 'rtl' ? 'ar-AE' : 'en-GB';
      },
      direction === 'rtl'
        ? 'Layout mirrored to right-to-left.'
        : 'Layout mirrored to left-to-right.',
    );
  }

  protected setOrientation(orientation: string): void {
    this.edit(
      (d) => (d.layout.page.orientation = orientation === 'landscape' ? 'landscape' : 'portrait'),
      `Page set to ${orientation}.`,
    );
  }

  protected setNumbering(numbering: string): void {
    this.edit((d) => (d.layout.pageNumbering = numbering === 'perGroup' ? 'perGroup' : 'document'));
  }

  protected setWatermark(text: string): void {
    this.edit((d) => (d.layout.watermark = text || null));
  }

  protected setTextStyle(component: TextComponent, patch: Partial<TextStyle>): void {
    this.updateComponent(component, { style: { ...component.style, ...patch } });
  }

  protected band(name: BandName) {
    return this.layout()[name];
  }

  protected setParam(name: string, value: string): void {
    this.paramValues.update((p) => ({ ...p, [name]: value }));
  }

  // ----- template helpers (typed views of the union) -----

  protected asText(c: BodyComponent): TextComponent {
    return c as TextComponent;
  }

  protected asTable(c: BodyComponent): TableComponent {
    return c as TableComponent;
  }

  protected asChart(c: BodyComponent): ChartComponent {
    return c as ChartComponent;
  }

  protected asKpi(c: BodyComponent): KpiComponent {
    return c as KpiComponent;
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected numberValue(event: Event): number {
    return Number((event.target as HTMLInputElement).value);
  }

  protected fieldLabel(key: string | null | undefined): string {
    const field = this.datasets()
      .flatMap((d) => d.fields)
      .find((f) => f.key === key);
    return field ? (this.rtl() ? field.labelAr : field.label) : (key ?? '');
  }

  private defaultFormat(field: FieldSchema): Format {
    return field.dataType === 'number' ? 'amount' : field.dataType === 'date' ? 'date' : 'text';
  }

  private resetParams(): void {
    this.paramValues.set(
      Object.fromEntries(this.def().parameters.map((p) => [p.name, p.default ?? ''])),
    );
  }

  private syncHistory(): void {
    this.canUndo.set(this.undoStack.length > 0);
    this.canRedo.set(this.redoStack.length > 0);
  }

  private announce(message: string): void {
    this.status.set(message);
  }

  private fail(error: HttpErrorResponse): void {
    this.busy.set(false);
    const problems = (error.error?.errors?.definition as string[] | undefined) ?? [
      error.error?.title ?? error.message,
    ];
    this.errors.set(problems);
    this.announce('The action failed. See the error list.');
  }
}
