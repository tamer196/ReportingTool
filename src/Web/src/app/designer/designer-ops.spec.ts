import { DatasetSchema } from '../reports/report.model';
import { bindingFor, createComponent, nudge, pageSizeMm, starterDefinition } from './designer-ops';

const schema: DatasetSchema = {
  id: 'client-transactions',
  name: 'Client transactions',
  nameAr: 'حركات العملاء',
  fields: [
    { key: 'ClientId', label: 'Client ID', labelAr: 'رقم العميل', dataType: 'text', masked: false },
    { key: 'Iban', label: 'IBAN', labelAr: 'رقم الآيبان', dataType: 'text', masked: true },
    {
      key: 'TradeDate',
      label: 'Trade date',
      labelAr: 'تاريخ الصفقة',
      dataType: 'date',
      masked: false,
    },
    { key: 'Amount', label: 'Amount', labelAr: 'المبلغ', dataType: 'number', masked: false },
  ],
};

describe('designer-ops', () => {
  it('keeps a moved band item inside its band', () => {
    const item = { id: 'a', kind: 'text' as const, xMm: 10, yMm: 2, widthMm: 50, heightMm: 6 };
    expect(nudge(item, { heightMm: 10 }, 186, -20, 0, false).xMm).toBe(0);
    expect(nudge(item, { heightMm: 10 }, 186, 500, 0, false).xMm).toBe(136);
    expect(nudge(item, { heightMm: 10 }, 186, 0, 9, false).yMm).toBe(4);
  });

  it('resizes without leaving the band', () => {
    const item = { id: 'a', kind: 'text' as const, xMm: 150, yMm: 2, widthMm: 30, heightMm: 6 };
    const resized = nudge(item, { heightMm: 10 }, 186, 20, 5, true);
    expect(resized.widthMm).toBe(36);
    expect(resized.heightMm).toBe(8);
  });

  it('creates tables from visible, unmasked fields with Arabic headers in RTL', () => {
    const table = createComponent('table', schema, 'rtl');
    expect(table.type).toBe('table');
    if (table.type !== 'table') return;
    expect(table.columns.map((c) => c.field)).toEqual(['ClientId', 'TradeDate', 'Amount']);
    expect(table.columns[0].header).toBe('رقم العميل');
    expect(table.columns.find((c) => c.field === 'Amount')?.format).toBe('amount');
  });

  it('gives a new binding the filters of an existing binding to the same dataset', () => {
    const definition = starterDefinition('rtl');
    definition.bindings.push({
      componentId: 'x',
      datasetId: schema.id,
      filters: [{ field: 'ClientId', op: 'lte', parameter: 'ToClient' }],
      sort: ['ClientId'],
    });
    const binding = bindingFor(definition, 'y', schema.id);
    expect(binding.filters).toEqual([{ field: 'ClientId', op: 'lte', parameter: 'ToClient' }]);
    expect(binding.componentId).toBe('y');
  });

  it('swaps width and height in landscape', () => {
    expect(
      pageSizeMm({
        size: 'A4',
        orientation: 'landscape',
        marginTopMm: 0,
        marginBottomMm: 0,
        marginStartMm: 0,
        marginEndMm: 0,
      }),
    ).toEqual({ w: 297, h: 210 });
  });
});
