# Filter Expression Syntax Guide

## Overview

The filter expression system allows Campaign Operators to segment recipients visually without writing SQL. Filters are defined as an Abstract Syntax Tree (AST) serialized to JSON and stored alongside campaign definitions. The system translates the AST into parameterized SQL WHERE clauses — no raw SQL from operators is ever executed.

This guide covers:
- The AST structure (leaf and composite nodes)
- Supported operators
- Field types and compatible operators
- Relative date filters
- The IN operator
- JSON format for storage and API calls
- The preview API endpoint

---

## AST Node Types

Every filter expression is a tree of nodes. There are two node types:

### 1. Leaf Node (`type: "leaf"`)

A single field comparison condition. This is the atomic unit of a filter.

| Property     | Type                | Description                                      |
|-------------|---------------------|--------------------------------------------------|
| `type`       | `"leaf"` (string)   | Discriminator — always `"leaf"` for leaf nodes.  |
| `fieldName`  | string              | Data source field name (must be in schema).      |
| `operator`   | integer             | Comparison operator (see table below).           |
| `value`      | any                 | Value to compare against (see notes per operator).|

**Example:**
```json
{
  "type": "leaf",
  "fieldName": "Age",
  "operator": 3,
  "value": 18
}
```
Translates to: `[Age] > @p0` (with `@p0 = 18`)

---

### 2. Composite Node (`type: "composite"`)

A logical grouping of child expressions combined with AND or OR.

| Property          | Type                       | Description                                    |
|------------------|----------------------------|------------------------------------------------|
| `type`            | `"composite"` (string)     | Discriminator — always `"composite"`.          |
| `logicalOperator` | `1` (AND) or `2` (OR)      | How child expressions are combined.            |
| `children`        | array of FilterExpression  | Child nodes (at least 1, max nesting depth 5). |

**Example:**
```json
{
  "type": "composite",
  "logicalOperator": 2,
  "children": [
    { "type": "leaf", "fieldName": "Country", "operator": 1, "value": "FR" },
    { "type": "leaf", "fieldName": "Country", "operator": 1, "value": "DE" }
  ]
}
```
Translates to: `([Country] = @p0 OR [Country] = @p1)`

---

## Supported Operators

| Enum Value | Integer | SQL       | Value Required? | Notes                                   |
|-----------|---------|-----------|-----------------|------------------------------------------|
| Equals    | 1       | `=`       | Yes             | Exact match.                             |
| NotEquals | 2       | `<>`      | Yes             | Excludes matching rows.                  |
| GreaterThan | 3     | `>`       | Yes             | Numbers and dates.                       |
| LessThan  | 4       | `<`       | Yes             | Numbers and dates.                       |
| GreaterThanOrEquals | 5 | `>=`  | Yes             | Numbers and dates.                       |
| LessThanOrEquals | 6 | `<=`    | Yes             | Numbers and dates.                       |
| Like      | 7       | `LIKE`    | Yes             | Pattern match. Use `%` for wildcards.    |
| In        | 8       | `IN`      | Yes (list)      | Set membership. Up to 1000 values.       |
| IsNull    | 9       | `IS NULL` | No              | Checks for NULL value in field.          |
| IsNotNull | 10      | `IS NOT NULL` | No          | Checks for non-NULL value.               |

---

## Operator and Field Type Compatibility

| Field Type        | Compatible Operators                              |
|------------------|---------------------------------------------------|
| Text (nvarchar)   | `=`, `!=`, `LIKE`, `IN`                          |
| Number (int, decimal, float) | `=`, `!=`, `>`, `<`, `>=`, `<=`, `IN` |
| Date (datetime, date) | `=`, `!=`, `>`, `<`, `>=`, `<=`          |
| Boolean (bit)     | `=`, `!=`                                        |

---

## Relative Date Filters

For fields with a date-compatible type (`datetime`, `date`), string values can use relative date keywords instead of literal ISO dates. The system resolves these to concrete UTC DateTime values at query time.

| Keyword       | Resolves to                           |
|--------------|----------------------------------------|
| `today`       | Start of today (UTC date only)        |
| `yesterday`   | Start of yesterday                    |
| `last7days`   | Today minus 7 days                    |
| `last30days`  | Today minus 30 days                   |
| `last90days`  | Today minus 90 days                   |
| `last365days` | Today minus 365 days                  |
| `thisweek`    | Start of the current calendar week    |
| `thismonth`   | First day of the current month        |
| `thisyear`    | January 1st of the current year       |

**Example — filter contacts created in the last 30 days:**
```json
{
  "type": "leaf",
  "fieldName": "CreatedAt",
  "operator": 5,
  "value": "last30days"
}
```
At runtime this becomes: `[CreatedAt] >= @p0` (with `@p0 = DateTime.UtcNow.AddDays(-30).Date`)

---

## The IN Operator

The IN operator tests whether a field value appears in a provided list.

- **Maximum values**: 1000 (enforced by validation before SQL generation)
- **Empty list**: An empty IN list is a safe no-op — translated to `1=0` (matches zero rows)
- **Single value**: Treated as a list of one

**Example — filter contacts in France, Germany, or Spain:**
```json
{
  "type": "leaf",
  "fieldName": "Country",
  "operator": 8,
  "value": ["FR", "DE", "ES"]
}
```
Translates to: `[Country] IN (@p0_0, @p0_1, @p0_2)`

---

## Complex AND/OR Logic

Top-level filter nodes (as passed in the `filters` array) are implicitly combined with AND. Use composite nodes to create OR groups or nested AND/OR logic.

### Example — Age > 18 AND (Country = FR OR Country = DE)

```json
[
  {
    "type": "leaf",
    "fieldName": "Age",
    "operator": 3,
    "value": 18
  },
  {
    "type": "composite",
    "logicalOperator": 2,
    "children": [
      { "type": "leaf", "fieldName": "Country", "operator": 1, "value": "FR" },
      { "type": "leaf", "fieldName": "Country", "operator": 1, "value": "DE" }
    ]
  }
]
```
Translates to: `[Age] > @p0 AND ([Country] = @p1 OR [Country] = @p2)`

### Example — (IsActive = true AND Role = 'admin') OR (IsActive = true AND Role = 'manager')

```json
{
  "type": "composite",
  "logicalOperator": 2,
  "children": [
    {
      "type": "composite",
      "logicalOperator": 1,
      "children": [
        { "type": "leaf", "fieldName": "IsActive", "operator": 1, "value": true },
        { "type": "leaf", "fieldName": "Role", "operator": 1, "value": "admin" }
      ]
    },
    {
      "type": "composite",
      "logicalOperator": 1,
      "children": [
        { "type": "leaf", "fieldName": "IsActive", "operator": 1, "value": true },
        { "type": "leaf", "fieldName": "Role", "operator": 1, "value": "manager" }
      ]
    }
  ]
}
```

---

## Nesting Depth Limit

Composite expressions support a maximum nesting depth of **5 levels**. Expressions exceeding this depth are rejected with a validation error before any SQL is executed.

---

## Preview API Endpoint

Use the preview endpoint to test a filter expression against a live data source and inspect the results before saving.

**Endpoint:** `POST /api/datasources/{id}/preview`

**Authorization:** Operator or Admin role required.

**Request body:**
```json
{
  "filters": [
    {
      "type": "leaf",
      "fieldName": "Age",
      "operator": 3,
      "value": 18
    }
  ],
  "maxRows": 50
}
```

| Property   | Type    | Description                                        |
|-----------|---------|-----------------------------------------------------|
| `filters`  | array   | Top-level filter expressions. Null = no filter.    |
| `maxRows`  | integer | Row limit (1–100). Defaults to 100.                |

**Response:**
```json
{
  "dataSourceId": "...",
  "rowCount": 42,
  "totalCount": 42,
  "hasFilters": true,
  "appliedWhereClause": "[Age] > @p0",
  "rows": [
    { "Id": 1, "Email": "user@example.com", "Age": 25 }
  ],
  "validationErrors": []
}
```

| Property             | Description                                                  |
|---------------------|--------------------------------------------------------------|
| `rowCount`           | Number of rows in this preview (max 100).                   |
| `totalCount`         | Total rows matching the filter (before cap).                |
| `appliedWhereClause` | The SQL WHERE clause generated from the AST (informational).|
| `validationErrors`   | Non-empty if filters were invalid; rows will be empty.      |

---

## Security

All filter values are passed as **named SQL parameters** — never interpolated into SQL text. This prevents SQL injection regardless of the value content. The security contract is:

1. **Values**: Always parameterized via Dapper `DynamicParameters`
2. **Field names**: Validated against the declared schema; bracket-quoted in SQL
3. **Operators**: Whitelisted via the `FilterOperator` enum — no arbitrary SQL operators accepted
4. **Table names**: Come from Admin-configured data sources only; never from filter input

---

## Filter Builder UI

The visual filter builder is accessible from the Data Source details page:

1. Navigate to **Admin > Data Sources**
2. Open a data source
3. Click **Filter Builder**
4. Add conditions using the field/operator/value dropdowns
5. Group conditions with AND/OR using **Add Group**
6. Click **Preview Results** to execute and see up to 100 matching rows
7. Use **Copy AST JSON** to copy the filter JSON for use in campaign configuration

The filter builder respects the declared field schema — only filterable fields appear in the dropdown, and operator options adapt to the field type.
