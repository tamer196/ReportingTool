import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, first, switchMap, timer } from 'rxjs';

import {
  DatasetSchema,
  ReportDefinition,
  RunResponse,
  TemplateResponse,
  TemplateSummary,
} from './report.model';

/** HTTP calls of the report designer. The OIDC interceptor adds the bearer token to /api/reports/. */
@Injectable({ providedIn: 'root' })
export class ReportsApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/reports';

  datasets(): Observable<DatasetSchema[]> {
    return this.http.get<DatasetSchema[]>(`${this.base}/catalog/datasets`);
  }

  templates(): Observable<TemplateSummary[]> {
    return this.http.get<TemplateSummary[]>(`${this.base}/templates`);
  }

  template(id: string): Observable<TemplateResponse> {
    return this.http.get<TemplateResponse>(`${this.base}/templates/${id}`);
  }

  save(id: string, name: string, definition: ReportDefinition): Observable<TemplateResponse> {
    return this.http.put<TemplateResponse>(`${this.base}/templates/${id}`, { name, definition });
  }

  /** Starts a run and emits once, when it has finished (succeeded or failed). */
  run(
    templateId: string,
    parameters: Record<string, string>,
    formats: string[],
  ): Observable<RunResponse> {
    return this.http
      .post<RunResponse>(`${this.base}/runs`, { templateId, parameters, formats })
      .pipe(
        switchMap((started) =>
          timer(500, 1000).pipe(
            switchMap(() => this.http.get<RunResponse>(`${this.base}/runs/${started.id}`)),
            filter((r) => r.status === 'Succeeded' || r.status === 'Failed'),
            first(),
          ),
        ),
      );
  }

  output(runId: string, format: 'pdf' | 'xlsx'): Observable<Blob> {
    return this.http.get(`${this.base}/runs/${runId}/${format}`, { responseType: 'blob' });
  }
}
