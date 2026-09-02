/**
 * ZIP and XLSX, without a dependency.
 *
 * WHY THIS EXISTS RATHER THAN `npm i xlsx`
 * ----------------------------------------
 * Two things are needed and both are small: write a .zip holding a CSV and an XLSX so the bulk
 * upload template can be downloaded as one file, and read the sheet back out of an .xlsx the
 * person uploads. A general spreadsheet library brings styles, formulae, dates, pivot tables and
 * a megabyte of parser for a job that is nine columns of text.
 *
 * A .xlsx IS A .zip, so the same forty lines do both ends of it.
 *
 * WHAT IS DELIBERATELY NOT SUPPORTED: formulae (the cached value is read instead), styles, and
 * number formats — a cell holding a date comes back as the serial number Excel stores, because
 * guessing between "45000" the day and "45000" the amount is exactly the kind of silent wrong
 * answer a lead import must not make. None of the nine template columns is a date.
 */

// =====================================================================================
// CRC-32 — the ZIP checksum. Nothing will open the file without it.
// =====================================================================================

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);

  for (let i = 0; i < 256; i++) {
    let c = i;

    for (let bit = 0; bit < 8; bit++) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }

    table[i] = c >>> 0;
  }

  return table;
})();

function crc32(bytes: Uint8Array): number {
  let crc = 0xffffffff;

  for (let i = 0; i < bytes.length; i++) {
    crc = CRC_TABLE[(crc ^ bytes[i]) & 0xff] ^ (crc >>> 8);
  }

  return (crc ^ 0xffffffff) >>> 0;
}

// =====================================================================================
// Writing
// =====================================================================================

export interface ZipEntry {
  /** Path inside the archive. Forward slashes, no leading slash. */
  readonly name: string;
  readonly data: Uint8Array;
}

/**
 * Builds a ZIP with every entry STORED rather than deflated.
 *
 * Compression is skipped on purpose: a template is a few hundred bytes, `CompressionStream` would
 * make the whole builder asynchronous, and a stored entry is as standard as a deflated one —
 * Windows Explorer, macOS Archive Utility and Excel all open it.
 */
export function createZipBytes(entries: readonly ZipEntry[]): Uint8Array {
  const encoder = new TextEncoder();
  const parts: Uint8Array[] = [];
  const central: Uint8Array[] = [];

  let offset = 0;

  for (const entry of entries) {
    const nameBytes = encoder.encode(entry.name);
    const crc = crc32(entry.data);

    // ---- Local file header ------------------------------------------------------------
    const local = new Uint8Array(30 + nameBytes.length);
    const localView = new DataView(local.buffer);

    localView.setUint32(0, 0x04034b50, true); // signature
    localView.setUint16(4, 20, true); // version needed
    localView.setUint16(6, 0x0800, true); // UTF-8 names
    localView.setUint16(8, 0, true); // stored
    localView.setUint16(10, 0, true); // time
    localView.setUint16(12, 0x21, true); // date — 1 Jan 1980, the ZIP epoch
    localView.setUint32(14, crc, true);
    localView.setUint32(18, entry.data.length, true);
    localView.setUint32(22, entry.data.length, true);
    localView.setUint16(26, nameBytes.length, true);
    localView.setUint16(28, 0, true); // extra field length
    local.set(nameBytes, 30);

    parts.push(local, entry.data);

    // ---- Central directory record -----------------------------------------------------
    const record = new Uint8Array(46 + nameBytes.length);
    const recordView = new DataView(record.buffer);

    recordView.setUint32(0, 0x02014b50, true);
    recordView.setUint16(4, 20, true); // version made by
    recordView.setUint16(6, 20, true); // version needed
    recordView.setUint16(8, 0x0800, true);
    recordView.setUint16(10, 0, true);
    recordView.setUint16(12, 0, true);
    recordView.setUint16(14, 0x21, true);
    recordView.setUint32(16, crc, true);
    recordView.setUint32(20, entry.data.length, true);
    recordView.setUint32(24, entry.data.length, true);
    recordView.setUint16(28, nameBytes.length, true);
    recordView.setUint32(42, offset, true); // offset of the local header
    record.set(nameBytes, 46);

    central.push(record);
    offset += local.length + entry.data.length;
  }

  const centralSize = central.reduce((total, record) => total + record.length, 0);
  const end = new Uint8Array(22);
  const endView = new DataView(end.buffer);

  endView.setUint32(0, 0x06054b50, true);
  endView.setUint16(8, entries.length, true);
  endView.setUint16(10, entries.length, true);
  endView.setUint32(12, centralSize, true);
  endView.setUint32(16, offset, true);

  parts.push(...central, end);

  const total = parts.reduce((size, part) => size + part.length, 0);
  const archive = new Uint8Array(total);
  let cursor = 0;

  for (const part of parts) {
    archive.set(part, cursor);
    cursor += part.length;
  }

  return archive;
}

/** The same archive, ready to hand to a download link. */
export function createZip(entries: readonly ZipEntry[]): Blob {
  return new Blob([createZipBytes(entries) as BlobPart], { type: 'application/zip' });
}

function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/** 0 -> A, 25 -> Z, 26 -> AA. */
function columnName(index: number): string {
  let name = '';
  let n = index;

  do {
    name = String.fromCharCode(65 + (n % 26)) + name;
    n = Math.floor(n / 26) - 1;
  } while (n >= 0);

  return name;
}

/**
 * Builds a single-sheet .xlsx from a grid of strings.
 *
 * EVERY CELL IS AN INLINE STRING. The alternative is a shared-strings table, which is smaller for
 * a real workbook and pointless for a header plus one example row — and inline strings mean a
 * mobile number keeps its leading zero instead of arriving as a number.
 */
export function createXlsx(rows: readonly (readonly string[])[], sheetName = 'Sheet1'): Uint8Array {
  const encoder = new TextEncoder();

  const sheetRows = rows
    .map((row, rowIndex) => {
      const cells = row
        .map((value, columnIndex) =>
          value === ''
            ? ''
            : `<c r="${columnName(columnIndex)}${rowIndex + 1}" t="inlineStr">`
              + `<is><t xml:space="preserve">${escapeXml(value)}</t></is></c>`,
        )
        .join('');

      return `<row r="${rowIndex + 1}">${cells}</row>`;
    })
    .join('');

  const files: Record<string, string> = {
    '[Content_Types].xml':
      '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
      + '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
      + '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
      + '<Default Extension="xml" ContentType="application/xml"/>'
      + '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
      + '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
      + '</Types>',

    '_rels/.rels':
      '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
      + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
      + '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
      + '</Relationships>',

    'xl/workbook.xml':
      '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
      + '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
      + 'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
      + `<sheets><sheet name="${escapeXml(sheetName)}" sheetId="1" r:id="rId1"/></sheets>`
      + '</workbook>',

    'xl/_rels/workbook.xml.rels':
      '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
      + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
      + '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
      + '</Relationships>',

    'xl/worksheets/sheet1.xml':
      '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
      + '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
      + `<sheetData>${sheetRows}</sheetData>`
      + '</worksheet>',
  };

  // Bytes rather than a Blob, because a workbook built here goes straight inside another zip.
  return createZipBytes(
    Object.entries(files).map(([name, xml]) => ({ name, data: encoder.encode(xml) })),
  );
}

// =====================================================================================
// Reading
// =====================================================================================

/**
 * Reads a ZIP's entries.
 *
 * THE CENTRAL DIRECTORY IS THE INDEX, and it is read from the end backwards — that is how the
 * format is meant to be walked, and it is the only way that survives the extra data some writers
 * put in front of the first local header.
 *
 * Deflated entries go through `DecompressionStream('deflate-raw')`, which every browser this app
 * supports has. A browser without it gets a named error rather than a corrupt import.
 */
export async function readZipEntries(buffer: ArrayBuffer): Promise<Map<string, Uint8Array>> {
  const bytes = new Uint8Array(buffer);
  const view = new DataView(buffer);
  const entries = new Map<string, Uint8Array>();

  // The End Of Central Directory record is the last 22 bytes, unless there is a zip comment —
  // so scan back over the largest comment the format allows.
  let end = -1;
  const scanFrom = Math.max(0, bytes.length - 22 - 0xffff);

  for (let i = bytes.length - 22; i >= scanFrom; i--) {
    if (view.getUint32(i, true) === 0x06054b50) {
      end = i;
      break;
    }
  }

  if (end < 0) {
    throw new Error('This file is not a valid .xlsx workbook.');
  }

  const count = view.getUint16(end + 10, true);
  let pointer = view.getUint32(end + 16, true);

  const decoder = new TextDecoder();

  for (let i = 0; i < count; i++) {
    if (view.getUint32(pointer, true) !== 0x02014b50) {
      break;
    }

    const method = view.getUint16(pointer + 10, true);
    const compressedSize = view.getUint32(pointer + 20, true);
    const nameLength = view.getUint16(pointer + 28, true);
    const extraLength = view.getUint16(pointer + 30, true);
    const commentLength = view.getUint16(pointer + 32, true);
    const localOffset = view.getUint32(pointer + 42, true);
    const name = decoder.decode(bytes.subarray(pointer + 46, pointer + 46 + nameLength));

    // The local header repeats the name and extra-field lengths, and the LOCAL ones are the
    // ones that locate the data — writers are allowed to differ between the two.
    const localNameLength = view.getUint16(localOffset + 26, true);
    const localExtraLength = view.getUint16(localOffset + 28, true);
    const dataStart = localOffset + 30 + localNameLength + localExtraLength;
    const raw = bytes.subarray(dataStart, dataStart + compressedSize);

    if (method === 0) {
      entries.set(name, raw);
    } else if (method === 8) {
      entries.set(name, await inflateRaw(raw));
    }
    // Any other method is left out rather than guessed at; the caller reports the missing part.

    pointer += 46 + nameLength + extraLength + commentLength;
  }

  return entries;
}

async function inflateRaw(data: Uint8Array): Promise<Uint8Array> {
  const DecompressionStreamCtor = (globalThis as { DecompressionStream?: typeof DecompressionStream })
    .DecompressionStream;

  if (!DecompressionStreamCtor) {
    throw new Error('This browser cannot read .xlsx files. Please upload the CSV instead.');
  }

  const stream = new Blob([data as BlobPart])
    .stream()
    .pipeThrough(new DecompressionStreamCtor('deflate-raw'));

  return new Uint8Array(await new Response(stream).arrayBuffer());
}

/** "BC12" -> 54. The column letters of a cell reference. */
function columnIndex(reference: string): number {
  let index = 0;

  for (const character of reference) {
    const code = character.charCodeAt(0);

    if (code < 65 || code > 90) {
      break;
    }

    index = index * 26 + (code - 64);
  }

  return index - 1;
}

/**
 * Reads the first worksheet of an .xlsx into a grid of strings.
 *
 * CELLS ARE PLACED BY THEIR REFERENCE, not by the order they appear. Excel omits empty cells
 * entirely, so a row whose middle column is blank writes A, C, D — and reading them in sequence
 * would slide the e-mail address into the city column, which is precisely the failure the CSV
 * parser was already fixed for.
 */
export async function readXlsxRows(file: File): Promise<string[][]> {
  const entries = await readZipEntries(await file.arrayBuffer());
  const decoder = new TextDecoder();

  const sheetName = [...entries.keys()]
    .filter((name) => name.startsWith('xl/worksheets/') && name.endsWith('.xml'))
    .sort()[0];

  if (!sheetName) {
    throw new Error('That workbook has no worksheet in it.');
  }

  const parser = new DOMParser();

  // Shared strings are how Excel itself writes text: the cell holds an index into this table.
  const sharedBytes = entries.get('xl/sharedStrings.xml');
  const shared: string[] = [];

  if (sharedBytes) {
    const document_ = parser.parseFromString(decoder.decode(sharedBytes), 'application/xml');

    for (const item of Array.from(document_.getElementsByTagName('si'))) {
      // <si> can be a single <t>, or a run of <r><t>..</t></r> when part of the text is styled.
      shared.push(
        Array.from(item.getElementsByTagName('t'))
          .map((node) => node.textContent ?? '')
          .join(''),
      );
    }
  }

  const sheet = parser.parseFromString(decoder.decode(entries.get(sheetName)!), 'application/xml');
  const rows: string[][] = [];

  for (const rowNode of Array.from(sheet.getElementsByTagName('row'))) {
    const row: string[] = [];

    for (const cell of Array.from(rowNode.getElementsByTagName('c'))) {
      const type = cell.getAttribute('t');
      const reference = cell.getAttribute('r') ?? '';
      const parsed = reference ? columnIndex(reference) : -1;
      const index = parsed >= 0 ? parsed : row.length;

      let text: string;

      if (type === 's') {
        const at = Number(cell.getElementsByTagName('v')[0]?.textContent ?? '-1');
        text = shared[at] ?? '';
      } else if (type === 'inlineStr') {
        text = Array.from(cell.getElementsByTagName('t'))
          .map((node) => node.textContent ?? '')
          .join('');
      } else {
        // Numbers, booleans and the cached result of a formula all live in <v>.
        text = cell.getElementsByTagName('v')[0]?.textContent ?? '';
      }

      while (row.length < index) {
        row.push('');
      }

      row[index] = text;
    }

    rows.push(row);
  }

  // Trailing rows that Excel keeps because they were once formatted are not data.
  while (rows.length > 0 && rows[rows.length - 1].every((value) => value.trim() === '')) {
    rows.pop();
  }

  return rows;
}
