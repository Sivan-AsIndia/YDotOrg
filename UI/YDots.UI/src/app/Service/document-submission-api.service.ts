import { HttpClient, HttpEvent, HttpEventType, HttpRequest } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse } from '../Shared/models/api-response.model';

/** What the server will accept. Read from the API so the screen and the rule agree. */
export interface DocumentUploadPolicy {
  maximumFileSizeMegabytes: number;
  maximumFileSizeBytes: number;
  maximumFilesPerSubmission: number;
  allowedContentTypes: string[];
  allowedExtensions: string[];
  downloadLinkExpirySeconds: number;
}

export interface SubmissionFile {
  id: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  contentHash?: string | null;
  status: string;
  uploadedAtUtc: string;
  uploadedByName?: string | null;
  isPreviewable: boolean;
  supersededByDocumentId?: string | null;
}

export interface DocumentSubmission {
  id: string;
  tenantId: string;
  organisationName?: string | null;
  organisationCode?: string | null;
  documentType: string;
  documentTypeDisplay: string;
  title?: string | null;
  notes?: string | null;
  status: string;
  statusDisplay: string;
  submittedAtUtc?: string | null;
  submittedByName?: string | null;
  reviewStartedAtUtc?: string | null;
  decidedAtUtc?: string | null;
  reviewedByName?: string | null;
  decisionNotes?: string | null;
  reuploadCount: number;
  fileCount: number;
  totalSizeBytes: number;
  fileKinds: string[];
  files: SubmissionFile[];
  /** What THIS caller may do. The screen draws its buttons from this, never from the status. */
  permittedActions: string[];
  version: number;
}

export interface DocumentDownloadLink {
  documentId: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  url: string;
  expiresAtUtc: string;
  isPreviewable: boolean;
}

/** Progress while a file is going up, so the bar reflects bytes rather than a guess. */
export interface UploadProgress {
  kind: 'progress';
  percent: number;
}

export interface UploadDone {
  kind: 'done';
  submission: DocumentSubmission;
}

export type UploadEvent = UploadProgress | UploadDone;

export type DocumentDecision = 'Approve' | 'Reject' | 'RequestReupload';

/**
 * Grouped document submissions, both sides of them.
 *
 * TWO SETS OF URLS, AND THE DIFFERENCE IS THE POINT. `/organisations/mine/...` carries no id, so
 * an Organisation cannot ask for somebody else's paperwork by editing one. `/organisations/{id}/...`
 * is the reviewer's, and the server gates it on the platform review permission. The component
 * picks a set by which audience it is drawn for; neither can reach the other's data by accident.
 *
 * THE UPLOAD USES multipart AND REPORTS PROGRESS. `reportProgress` with an `HttpRequest` is what
 * turns an upload into a stream of events instead of one promise that resolves when it is over —
 * without it a 4 MB scan is a frozen screen with no indication anything is happening.
 */
@Injectable({ providedIn: 'root' })
export class DocumentSubmissionApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/organisations`;

  // ---- Shared ----------------------------------------------------------------------------

  getPolicy(): Observable<DocumentUploadPolicy> {
    return this.http
      .get<ApiResponse<DocumentUploadPolicy>>(`${this.base}/document-upload-policy`)
      .pipe(map((response) => response.data!));
  }

  // ---- The Organisation's own -------------------------------------------------------------

  getMine(): Observable<DocumentSubmission[]> {
    return this.http
      .get<ApiResponse<DocumentSubmission[]>>(`${this.base}/mine/document-submissions`)
      .pipe(map((response) => response.data ?? []));
  }

  createMine(request: {
    documentType: string;
    title?: string | null;
    notes?: string | null;
  }): Observable<DocumentSubmission> {
    return this.http
      .post<ApiResponse<DocumentSubmission>>(`${this.base}/mine/document-submissions`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Sends one file, reporting how far it has got.
   *
   * The response is the WHOLE submission rather than just the new file, so the screen redraws
   * from one authoritative object instead of splicing a row into a list it maintains itself —
   * which is how a count and a list drift apart.
   */
  uploadFile(submissionId: string, file: File): Observable<UploadEvent> {
    const form = new FormData();
    form.append('file', file, file.name);

    const request = new HttpRequest(
      'POST',
      `${this.base}/mine/document-submissions/${submissionId}/files`,
      form,
      { reportProgress: true },
    );

    return this.http.request<ApiResponse<DocumentSubmission>>(request).pipe(
      map((event: HttpEvent<ApiResponse<DocumentSubmission>>): UploadEvent | null => {
        if (event.type === HttpEventType.UploadProgress) {
          // `total` is absent on some proxies. Holding at 0 is better than dividing by
          // undefined and rendering NaN% into the bar.
          const percent = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
          return { kind: 'progress', percent };
        }

        if (event.type === HttpEventType.Response) {
          return { kind: 'done', submission: event.body!.data! };
        }

        return null;
      }),
      // The stream also carries Sent and ResponseHeader events, which mean nothing here.
      map((value) => value as UploadEvent),
    );
  }

  removeFile(submissionId: string, documentId: string): Observable<DocumentSubmission> {
    return this.http
      .delete<ApiResponse<DocumentSubmission>>(
        `${this.base}/mine/document-submissions/${submissionId}/files/${documentId}`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Withdraws a draft the organisation has decided against.
   *
   * IT EXISTS BECAUSE THERE WAS NO WAY OUT. A draft card offered an upload control and nothing
   * else, so a submission opened by mistake was permanent - and an empty one appeared on the
   * platform reviewer's screen beside the real evidence.
   *
   * ONLY A DRAFT. The server refuses anything already sent, whatever this client asks.
   */
  discardMine(submissionId: string, expectedVersion: number): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(
        `${this.base}/mine/document-submissions/${submissionId}`,
        { params: { expectedVersion } })
      .pipe(map((response) => response.data!));
  }

  submitMine(submissionId: string, expectedVersion: number, notes?: string | null):
    Observable<DocumentSubmission> {
    return this.http
      .post<ApiResponse<DocumentSubmission>>(
        `${this.base}/mine/document-submissions/${submissionId}/submit`,
        { expectedVersion, notes })
      .pipe(map((response) => response.data!));
  }

  getMyFileLink(submissionId: string, documentId: string, inline: boolean):
    Observable<DocumentDownloadLink> {
    return this.http
      .get<ApiResponse<DocumentDownloadLink>>(
        `${this.base}/mine/document-submissions/${submissionId}/files/${documentId}/link`,
        { params: { inline } })
      .pipe(map((response) => response.data!));
  }

  // ---- The reviewer's --------------------------------------------------------------------

  getForOrganisation(tenantId: string): Observable<DocumentSubmission[]> {
    return this.http
      .get<ApiResponse<DocumentSubmission[]>>(`${this.base}/${tenantId}/document-submissions`)
      .pipe(map((response) => response.data ?? []));
  }

  getReviewFileLink(tenantId: string, submissionId: string, documentId: string, inline: boolean):
    Observable<DocumentDownloadLink> {
    return this.http
      .get<ApiResponse<DocumentDownloadLink>>(
        `${this.base}/${tenantId}/document-submissions/${submissionId}/files/${documentId}/link`,
        { params: { inline } })
      .pipe(map((response) => response.data!));
  }

  startReview(tenantId: string, submissionId: string): Observable<DocumentSubmission> {
    return this.http
      .post<ApiResponse<DocumentSubmission>>(
        `${this.base}/${tenantId}/document-submissions/${submissionId}/start-review`, {})
      .pipe(map((response) => response.data!));
  }

  decide(
    tenantId: string,
    submissionId: string,
    decision: DocumentDecision,
    expectedVersion: number,
    notes?: string | null,
  ): Observable<DocumentSubmission> {
    return this.http
      .post<ApiResponse<DocumentSubmission>>(
        `${this.base}/${tenantId}/document-submissions/${submissionId}/decide`,
        { decision, expectedVersion, notes })
      .pipe(map((response) => response.data!));
  }
}
