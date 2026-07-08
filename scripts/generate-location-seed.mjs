import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const backendRoot = path.resolve(scriptDir, '..');
const csvPath = process.argv[2] ?? path.join(backendRoot, 'database/seeds/tinh_xa_phuong_chi_ten_2025.csv');
const outputPath = process.argv[3] ?? path.join(backendRoot, 'database/seeds/seed_vietnam_administrative_units_2025.sql');

const parseCsv = (text) => {
  const rows = [];
  let row = [];
  let field = '';
  let inQuotes = false;

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    const next = text[index + 1];

    if (char === '"') {
      if (inQuotes && next === '"') {
        field += '"';
        index += 1;
      } else {
        inQuotes = !inQuotes;
      }
      continue;
    }

    if (char === ',' && !inQuotes) {
      row.push(field);
      field = '';
      continue;
    }

    if ((char === '\n' || char === '\r') && !inQuotes) {
      if (char === '\r' && next === '\n') index += 1;
      row.push(field);
      if (row.some((value) => value.length > 0)) rows.push(row);
      row = [];
      field = '';
      continue;
    }

    field += char;
  }

  if (field.length > 0 || row.length > 0) {
    row.push(field);
    rows.push(row);
  }

  return rows;
};

const stripPrefix = (value, prefixes) => {
  const trimmed = value.trim();
  const prefix = prefixes.find((item) => trimmed.startsWith(`${item} `));
  return {
    name: prefix ? trimmed.slice(prefix.length + 1).trim() : trimmed,
    fullName: trimmed,
  };
};

const sql = (value) => `N'${value.replaceAll("'", "''")}'`;
const provincePrefixes = ['Th\u00e0nh ph\u1ed1', 'T\u1ec9nh'];
const wardPrefixes = ['Ph\u01b0\u1eddng', 'X\u00e3', 'Th\u1ecb tr\u1ea5n', '\u0110\u1eb7c khu'];
const text = readFileSync(csvPath, 'utf8').replace(/^\uFEFF/, '');
const rows = parseCsv(text);
const [header, ...records] = rows;

if (!header || header[0] !== 'TenTinhThanh' || header[1] !== 'TenXaPhuong') {
  throw new Error('CSV must have TenTinhThanh,TenXaPhuong headers.');
}

const provinces = [];
const provinceByFullName = new Map();
const wards = [];

for (const [provinceFullName, wardFullName] of records) {
  if (!provinceFullName?.trim() || !wardFullName?.trim()) continue;

  let province = provinceByFullName.get(provinceFullName.trim());
  if (!province) {
    province = {
      code: `P${String(provinces.length + 1).padStart(3, '0')}`,
      ...stripPrefix(provinceFullName, provincePrefixes),
      wards: [],
    };
    provinces.push(province);
    provinceByFullName.set(province.fullName, province);
  }

  const ward = {
    code: `${province.code}-W${String(province.wards.length + 1).padStart(3, '0')}`,
    provinceCode: province.code,
    ...stripPrefix(wardFullName, wardPrefixes),
  };
  province.wards.push(ward);
  wards.push(ward);
}

if (provinces.length !== 34) {
  throw new Error(`Expected 34 provinces/cities, found ${provinces.length}.`);
}

if (wards.length !== 3321) {
  throw new Error(`Expected 3,321 ward-level units, found ${wards.length}.`);
}

const provinceRows = provinces.map((province) =>
  `    (${sql(province.code)}, ${sql(province.name)}, ${sql(province.fullName)})`,
);
const wardRows = wards.map((ward) =>
  `    (${sql(ward.code)}, ${sql(ward.provinceCode)}, ${sql(ward.name)}, ${sql(ward.fullName)})`,
);

const chunks = (items, size) => Array.from(
  { length: Math.ceil(items.length / size) },
  (_, index) => items.slice(index * size, index * size + size),
);

const wardInsertSql = chunks(wardRows, 500)
  .map((chunk) => `INSERT INTO dbo.Wards (Code, ProvinceCode, Name, FullName)\nVALUES\n${chunk.join(',\n')};`)
  .join('\n\n');

const output = `-- Vietnam administrative units seed data after 2025 rearrangement
-- Generated from tinh_xa_phuong_chi_ten_2025.csv
-- Counts: 34 provinces/cities, 3,321 wards/communes/special zones
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Provinces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Provinces (
        Code NVARCHAR(10) NOT NULL CONSTRAINT PK_Provinces PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        FullName NVARCHAR(130) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.Wards', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Wards (
        Code NVARCHAR(20) NOT NULL CONSTRAINT PK_Wards PRIMARY KEY,
        ProvinceCode NVARCHAR(10) NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        FullName NVARCHAR(180) NOT NULL,
        CONSTRAINT FK_Wards_Provinces_ProvinceCode FOREIGN KEY (ProvinceCode) REFERENCES dbo.Provinces(Code)
    );
END;

IF COL_LENGTH(N'dbo.Provinces', N'Type') IS NOT NULL ALTER TABLE dbo.Provinces DROP COLUMN [Type];
IF COL_LENGTH(N'dbo.Provinces', N'TaxCode') IS NOT NULL ALTER TABLE dbo.Provinces DROP COLUMN TaxCode;
IF COL_LENGTH(N'dbo.Wards', N'Type') IS NOT NULL ALTER TABLE dbo.Wards DROP COLUMN [Type];
IF COL_LENGTH(N'dbo.Wards', N'OldDistrictTaxCode') IS NOT NULL ALTER TABLE dbo.Wards DROP COLUMN OldDistrictTaxCode;
IF COL_LENGTH(N'dbo.Wards', N'OldDistrictName') IS NOT NULL ALTER TABLE dbo.Wards DROP COLUMN OldDistrictName;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Wards_ProvinceCode' AND object_id = OBJECT_ID(N'dbo.Wards'))
BEGIN
    CREATE INDEX IX_Wards_ProvinceCode ON dbo.Wards (ProvinceCode);
END;

BEGIN TRANSACTION;

DELETE FROM dbo.Wards;
DELETE FROM dbo.Provinces;

INSERT INTO dbo.Provinces (Code, Name, FullName)
VALUES
${provinceRows.join(',\n')};

${wardInsertSql}

COMMIT TRANSACTION;

SELECT COUNT(*) AS ProvinceCount FROM dbo.Provinces;
SELECT COUNT(*) AS WardCount FROM dbo.Wards;
`;

writeFileSync(outputPath, output, 'utf8');
console.log(`Generated ${provinces.length} provinces/cities and ${wards.length} ward-level units.`);