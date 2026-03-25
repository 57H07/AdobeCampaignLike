$bytes = [System.IO.File]::ReadAllBytes('.userstories/BACKLOG.md')
$text = [System.Text.Encoding]::UTF8.GetString($bytes)

# Replace task checkboxes for 01, 02, 03
$text = $text -replace '- \[ \] `TASK-010-01` - \*\*\[API\]\*\* POST /api/templates/\{id\}/preview endpoint', '- [x] `TASK-010-01` - **[API]** POST /api/templates/{id}/preview endpoint ✅ 2026-03-25'
$text = $text -replace '- \[ \] `TASK-010-02` - \*\*\[Service\]\*\* Sample data fetcher from data source', '- [x] `TASK-010-02` - **[Service]** Sample data fetcher from data source ✅ 2026-03-25'
$text = $text -replace '- \[ \] `TASK-010-03` - \*\*\[Service\]\*\* Template resolution with sample data', '- [x] `TASK-010-03` - **[Service]** Template resolution with sample data ✅ 2026-03-25'

[System.IO.File]::WriteAllBytes('.userstories/BACKLOG.md', [System.Text.Encoding]::UTF8.GetBytes($text))
Write-Host "Done"
