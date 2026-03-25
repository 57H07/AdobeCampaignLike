#!/usr/bin/env node

/**
 * Backlog Export Tool
 * Exports the BACKLOG.md to various formats (HTML, JSON, CSV, MD)
 */

const fs = require('fs');
const path = require('path');

// Parse command line arguments
const args = process.argv.slice(2);
const format = args[0] || 'html';
const filters = args.slice(1);

// Configuration
const BACKLOG_PATH = path.join(__dirname, 'BACKLOG.md');
const EXPORT_DIR = path.join(__dirname, 'exports');
const timestamp = new Date().toISOString().replace(/[:.]/g, '-').split('T')[0] + '-' + new Date().toTimeString().split(' ')[0].replace(/:/g, '');

// Ensure export directory exists
if (!fs.existsSync(EXPORT_DIR)) {
  fs.mkdirSync(EXPORT_DIR, { recursive: true });
}

// Parse filters
const filterConfig = {
  status: null,
  priority: null,
  epic: null,
  detailed: filters.includes('detailed'),
  summary: !filters.includes('detailed')
};

filters.forEach(filter => {
  if (filter.startsWith('status:')) filterConfig.status = filter.split(':')[1];
  if (filter.startsWith('priority:')) filterConfig.priority = filter.split(':')[1];
  if (filter.startsWith('epic:')) filterConfig.epic = filter.split(':')[1];
});

console.log('📖 Reading backlog...');
const backlogContent = fs.readFileSync(BACKLOG_PATH, 'utf-8');

// Parse backlog
const data = parseBacklog(backlogContent);
console.log(`✅ ${data.userStories.length} user stories found\n`);

// Apply filters
const filtered = applyFilters(data, filterConfig);
console.log(`🔍 Applied filters: ${Object.entries(filterConfig).filter(([k, v]) => v && k !== 'detailed' && k !== 'summary').map(([k, v]) => `${k}=${v}`).join(', ') || 'none'}`);
console.log(`✅ ${filtered.userStories.length} user stories match criteria\n`);

// Generate export
switch (format.toLowerCase()) {
  case 'html':
    generateHTML(filtered, filterConfig);
    break;
  case 'json':
    generateJSON(filtered, filterConfig);
    break;
  case 'csv':
    generateCSV(filtered, filterConfig);
    break;
  case 'md':
  case 'markdown':
    generateMarkdown(filtered, filterConfig);
    break;
  case 'pdf':
    generateHTML(filtered, filterConfig);
    console.log('\n💡 Open the HTML file in a browser and use Print > Save as PDF');
    break;
  default:
    console.error(`❌ Unknown format: ${format}`);
    process.exit(1);
}

/**
 * Parse BACKLOG.md into structured data
 */
function parseBacklog(content) {
  const lines = content.split('\n');
  const data = {
    projectName: '',
    metadata: {},
    statistics: {},
    personas: [],
    epics: [],
    userStories: []
  };

  let currentSection = null;
  let currentEpic = null;
  let currentUS = null;
  let currentPersona = null;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();

    // Project name
    if (line.match(/^#\s+.*Backlog\s+-\s+(.+)/)) {
      data.projectName = line.match(/Backlog\s+-\s+(.+)/)[1];
    }

    // Metadata
    if (line.startsWith('**Total User Stories:**')) {
      data.metadata.totalUserStories = parseInt(line.match(/\d+/)[0]);
    }

    // Personas
    if (line.match(/^###\s+\[([A-Z]+)\]\s+-\s+(.+)/)) {
      const match = line.match(/\[([A-Z]+)\]\s+-\s+(.+)/);
      currentPersona = {
        role: match[1],
        name: match[2],
        description: '',
        needs: []
      };
      data.personas.push(currentPersona);
    }

    // Epics (using ### format)
    if (line.match(/^###\s+Epic\s+(\d+):\s+(.+)/)) {
      const match = line.match(/Epic\s+(\d+):\s+(.+)/);
      currentEpic = {
        id: `epic-${match[1]}`,
        number: parseInt(match[1]),
        name: match[2],
        description: '',
        userStories: []
      };
      data.epics.push(currentEpic);
      currentUS = null;
    }

    // User Stories (using #### format)
    if (line.match(/^####\s+\[US-(\d+)\]\s+-\s+(.+)/)) {
      const match = line.match(/\[US-(\d+)\]\s+-\s+(.+)/);
      currentUS = {
        id: `US-${match[1].padStart(3, '0')}`,
        number: parseInt(match[1]),
        title: match[2],
        epic: currentEpic ? currentEpic.id : null,
        status: 'TODO',
        priority: 'Medium',
        complexity: 'M',
        narrative: { as_a: '', i_want: '', so_that: '' },
        acceptanceCriteria: [],
        tasks: [],
        dependencies: [],
        estimation: '',
        technicalNotes: '',
        startDate: null,
        endDate: null
      };
      data.userStories.push(currentUS);
      if (currentEpic) {
        currentEpic.userStories.push(currentUS.id);
      }
    }

    // User Story Details
    if (currentUS) {
      if (line.startsWith('**Status:**')) {
        const status = line.match(/\*\*Status:\*\*\s*(.+)/);
        if (status) {
          currentUS.status = status[1].replace(/[✅🔵🟡ðŸŸ¡]/g, '').trim().toUpperCase().replace(/ /g, '_');
        }
      }
      if (line.startsWith('**Priority:**')) {
        const priority = line.match(/Priority:\*\*\s*(.+)/);
        if (priority) {
          currentUS.priority = priority[1].replace(/[🔴🟠🟢]/g, '').trim();
        }
      }
      if (line.startsWith('**Complexity:**')) {
        const complexity = line.match(/Complexity:\*\*\s*(.+)/);
        if (complexity) {
          currentUS.complexity = complexity[1].trim().split(' ')[0];
        }
      }
      if (line.startsWith('**As a**')) {
        currentUS.narrative.as_a = line.replace('**As a**', '').trim();
      }
      if (line.startsWith('**I want**')) {
        currentUS.narrative.i_want = line.replace('**I want**', '').trim();
      }
      if (line.startsWith('**So that**')) {
        currentUS.narrative.so_that = line.replace('**So that**', '').trim();
      }
      if (line.match(/^-\s+\[[ x]\]/)) {
        const isCompleted = line.includes('[x]');
        const text = line.replace(/^-\s+\[[ x]\]\s*/, '');

        // Check if it's in acceptance criteria section or tasks section
        const prevLines = lines.slice(Math.max(0, i - 10), i).join('\n');
        if (prevLines.includes('**Acceptance criteria:**')) {
          currentUS.acceptanceCriteria.push({ text, completed: isCompleted });
        } else if (prevLines.includes('**Tasks:**') || prevLines.includes('**TASK-')) {
          currentUS.tasks.push({ text, completed: isCompleted });
        }
      }
      if (line.startsWith('**Estimation:**')) {
        currentUS.estimation = line.replace('**Estimation:**', '').trim();
      }
      if (line.startsWith('**Dependencies:**')) {
        const deps = line.replace('**Dependencies:**', '').trim();
        if (deps && deps !== 'None') {
          currentUS.dependencies = deps.split(',').map(d => d.trim());
        }
      }
    }
  }

  // Calculate statistics
  data.statistics = {
    byStatus: {
      done: data.userStories.filter(us => us.status === 'DONE').length,
      in_progress: data.userStories.filter(us => us.status === 'IN_PROGRESS').length,
      todo: data.userStories.filter(us => us.status === 'TODO' || us.status === '🟡_PLANNING').length
    },
    byPriority: {
      high: data.userStories.filter(us => us.priority === 'High').length,
      medium: data.userStories.filter(us => us.priority === 'Medium').length,
      low: data.userStories.filter(us => us.priority === 'Low').length
    },
    byComplexity: {
      S: data.userStories.filter(us => us.complexity === 'S').length,
      M: data.userStories.filter(us => us.complexity === 'M').length,
      L: data.userStories.filter(us => us.complexity === 'L').length,
      XL: data.userStories.filter(us => us.complexity === 'XL').length
    }
  };

  return data;
}

/**
 * Apply filters to data
 */
function applyFilters(data, filterConfig) {
  let filtered = { ...data };
  let userStories = [...data.userStories];

  if (filterConfig.status) {
    userStories = userStories.filter(us => us.status.toLowerCase().includes(filterConfig.status.toLowerCase()));
  }
  if (filterConfig.priority) {
    userStories = userStories.filter(us => us.priority.toLowerCase() === filterConfig.priority.toLowerCase());
  }
  if (filterConfig.epic) {
    userStories = userStories.filter(us => us.epic === `epic-${filterConfig.epic}`);
  }

  filtered.userStories = userStories;
  return filtered;
}

/**
 * Generate standalone HTML export
 */
function generateHTML(data, filterConfig) {
  const outputPath = path.join(EXPORT_DIR, `backlog-${timestamp}.html`);

  const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Backlog - ${data.projectName}</title>
  <style>
    * {
      margin: 0;
      padding: 0;
      box-sizing: border-box;
    }

    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
      line-height: 1.6;
      color: #333;
      background: #f5f5f5;
      padding: 20px;
    }

    .container {
      max-width: 1200px;
      margin: 0 auto;
      background: white;
      padding: 40px;
      box-shadow: 0 2px 10px rgba(0,0,0,0.1);
    }

    header {
      border-bottom: 3px solid #2563eb;
      padding-bottom: 20px;
      margin-bottom: 30px;
    }

    h1 {
      color: #1e293b;
      font-size: 2.5em;
      margin-bottom: 10px;
    }

    .metadata {
      color: #64748b;
      font-size: 0.9em;
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
      gap: 20px;
      margin: 30px 0;
    }

    .stat-card {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: white;
      padding: 20px;
      border-radius: 8px;
      box-shadow: 0 4px 6px rgba(0,0,0,0.1);
    }

    .stat-card.status { background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%); }
    .stat-card.priority { background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%); }
    .stat-card.complexity { background: linear-gradient(135deg, #43e97b 0%, #38f9d7 100%); }

    .stat-card h3 {
      font-size: 0.9em;
      opacity: 0.9;
      margin-bottom: 10px;
    }

    .stat-card .value {
      font-size: 2em;
      font-weight: bold;
    }

    .stat-list {
      margin-top: 10px;
      font-size: 0.85em;
      opacity: 0.95;
    }

    .epic {
      margin: 40px 0;
      border-left: 4px solid #2563eb;
      padding-left: 20px;
    }

    .epic-header {
      background: #eff6ff;
      padding: 15px;
      margin-bottom: 20px;
      border-radius: 4px;
    }

    .epic-title {
      color: #1e40af;
      font-size: 1.5em;
      margin-bottom: 5px;
    }

    .user-story {
      background: #fafafa;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      padding: 20px;
      margin: 20px 0;
      page-break-inside: avoid;
    }

    .us-header {
      display: flex;
      justify-content: space-between;
      align-items: start;
      margin-bottom: 15px;
      flex-wrap: wrap;
      gap: 10px;
    }

    .us-title {
      font-size: 1.3em;
      color: #1e293b;
      flex: 1;
    }

    .badges {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }

    .badge {
      padding: 4px 12px;
      border-radius: 12px;
      font-size: 0.75em;
      font-weight: 600;
      text-transform: uppercase;
    }

    .badge.status-done { background: #dcfce7; color: #166534; }
    .badge.status-in-progress { background: #dbeafe; color: #1e40af; }
    .badge.status-todo { background: #fef3c7; color: #92400e; }

    .badge.priority-high { background: #fee2e2; color: #991b1b; }
    .badge.priority-medium { background: #fed7aa; color: #9a3412; }
    .badge.priority-low { background: #d1fae5; color: #065f46; }

    .badge.complexity { background: #e0e7ff; color: #3730a3; }

    .narrative {
      background: white;
      border-left: 3px solid #8b5cf6;
      padding: 15px;
      margin: 15px 0;
      font-style: italic;
    }

    .narrative-line {
      margin: 5px 0;
    }

    .narrative-label {
      font-weight: 600;
      color: #7c3aed;
    }

    .section {
      margin: 15px 0;
    }

    .section-title {
      font-weight: 600;
      color: #475569;
      margin-bottom: 8px;
      font-size: 0.9em;
      text-transform: uppercase;
    }

    .criteria-list, .task-list {
      list-style: none;
    }

    .criteria-list li, .task-list li {
      padding: 5px 0;
      padding-left: 25px;
      position: relative;
    }

    .criteria-list li:before {
      content: "✓";
      position: absolute;
      left: 0;
      color: #10b981;
      font-weight: bold;
    }

    .task-list li.completed {
      text-decoration: line-through;
      opacity: 0.6;
    }

    .task-list li:before {
      content: "□";
      position: absolute;
      left: 0;
      color: #94a3b8;
    }

    .task-list li.completed:before {
      content: "✓";
      color: #10b981;
    }

    .dependencies {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }

    .dependency {
      background: #fef3c7;
      color: #92400e;
      padding: 3px 10px;
      border-radius: 4px;
      font-size: 0.85em;
      font-weight: 500;
    }

    .no-print {
      margin: 20px 0;
      padding: 15px;
      background: #eff6ff;
      border-radius: 8px;
      text-align: center;
    }

    .btn {
      display: inline-block;
      padding: 10px 20px;
      background: #2563eb;
      color: white;
      text-decoration: none;
      border-radius: 6px;
      font-weight: 600;
      cursor: pointer;
      border: none;
      margin: 0 5px;
    }

    .btn:hover {
      background: #1d4ed8;
    }

    @media print {
      body {
        background: white;
        padding: 0;
      }
      .container {
        box-shadow: none;
        padding: 20px;
      }
      .no-print {
        display: none;
      }
    }

    @media (max-width: 768px) {
      .container {
        padding: 20px;
      }
      h1 {
        font-size: 1.8em;
      }
      .stats-grid {
        grid-template-columns: 1fr;
      }
    }
  </style>
</head>
<body>
  <div class="container">
    <header>
      <h1>📋 Backlog - ${data.projectName}</h1>
      <div class="metadata">
        <strong>Generated:</strong> ${new Date().toLocaleString()} |
        <strong>Total User Stories:</strong> ${data.userStories.length}
      </div>
    </header>

    <div class="no-print">
      <button class="btn" onclick="window.print()">🖨️ Print / Save as PDF</button>
      <button class="btn" onclick="toggleDetailed()">👁️ Toggle Details</button>
    </div>

    <div class="stats-grid">
      <div class="stat-card status">
        <h3>Status Overview</h3>
        <div class="value">${data.userStories.length}</div>
        <div class="stat-list">
          ✅ Done: ${data.statistics.byStatus.done}<br>
          🔵 In Progress: ${data.statistics.byStatus.in_progress}<br>
          🟡 To Do: ${data.statistics.byStatus.todo}
        </div>
      </div>

      <div class="stat-card priority">
        <h3>By Priority</h3>
        <div class="value">${data.statistics.byPriority.high}</div>
        <div class="stat-list">
          🔴 High: ${data.statistics.byPriority.high}<br>
          🟠 Medium: ${data.statistics.byPriority.medium}<br>
          🟢 Low: ${data.statistics.byPriority.low}
        </div>
      </div>

      <div class="stat-card complexity">
        <h3>By Complexity</h3>
        <div class="value">${Object.values(data.statistics.byComplexity).reduce((a, b) => a + b, 0)}</div>
        <div class="stat-list">
          S: ${data.statistics.byComplexity.S} | M: ${data.statistics.byComplexity.M}<br>
          L: ${data.statistics.byComplexity.L} | XL: ${data.statistics.byComplexity.XL}
        </div>
      </div>
    </div>

    ${generateEpicsHTML(data, filterConfig)}
  </div>

  <script>
    function toggleDetailed() {
      document.querySelectorAll('.section').forEach(el => {
        el.style.display = el.style.display === 'none' ? 'block' : 'none';
      });
    }
  </script>
</body>
</html>`;

  fs.writeFileSync(outputPath, html, 'utf-8');

  console.log('\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
  console.log('✅ EXPORT COMPLETE');
  console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n');
  console.log(`Format: HTML`);
  console.log(`User Stories exported: ${data.userStories.length}\n`);
  console.log('📁 Generated file:');
  console.log(`   ${outputPath}\n`);
  console.log('💡 Next steps:');
  console.log(`   1. Open the HTML file in your browser`);
  console.log(`   2. Click "Print / Save as PDF" or use Ctrl+P`);
  console.log(`   3. Select "Save as PDF" as the destination\n`);
}

function generateEpicsHTML(data, filterConfig) {
  let html = '';

  // Group user stories by epic
  const epicMap = new Map();
  data.userStories.forEach(us => {
    if (!epicMap.has(us.epic)) {
      epicMap.set(us.epic, []);
    }
    epicMap.get(us.epic).push(us);
  });

  // Generate HTML for each epic
  data.epics.forEach(epic => {
    const epicUserStories = epicMap.get(epic.id) || [];
    if (epicUserStories.length === 0) return;

    html += `
    <div class="epic">
      <div class="epic-header">
        <div class="epic-title">Epic ${epic.number}: ${epic.name}</div>
        <div>${epicUserStories.length} user stor${epicUserStories.length === 1 ? 'y' : 'ies'}</div>
      </div>
      ${epicUserStories.map(us => generateUserStoryHTML(us, filterConfig)).join('')}
    </div>`;
  });

  // Handle user stories without epic
  const noEpicStories = epicMap.get(null) || [];
  if (noEpicStories.length > 0) {
    html += `
    <div class="epic">
      <div class="epic-header">
        <div class="epic-title">Other User Stories</div>
        <div>${noEpicStories.length} user stor${noEpicStories.length === 1 ? 'y' : 'ies'}</div>
      </div>
      ${noEpicStories.map(us => generateUserStoryHTML(us, filterConfig)).join('')}
    </div>`;
  }

  return html;
}

function generateUserStoryHTML(us, filterConfig) {
  const statusClass = us.status.toLowerCase().replace('_', '-');
  const priorityClass = us.priority.toLowerCase();

  return `
    <div class="user-story">
      <div class="us-header">
        <div class="us-title">[${us.id}] ${us.title}</div>
        <div class="badges">
          <span class="badge status-${statusClass}">${us.status.replace('_', ' ')}</span>
          <span class="badge priority-${priorityClass}">${us.priority}</span>
          <span class="badge complexity">${us.complexity}</span>
        </div>
      </div>

      ${us.narrative.as_a ? `
      <div class="narrative">
        <div class="narrative-line">
          <span class="narrative-label">As a</span> ${us.narrative.as_a}
        </div>
        <div class="narrative-line">
          <span class="narrative-label">I want</span> ${us.narrative.i_want}
        </div>
        <div class="narrative-line">
          <span class="narrative-label">So that</span> ${us.narrative.so_that}
        </div>
      </div>` : ''}

      ${filterConfig.detailed && us.acceptanceCriteria.length > 0 ? `
      <div class="section">
        <div class="section-title">Acceptance Criteria</div>
        <ul class="criteria-list">
          ${us.acceptanceCriteria.map(ac => `<li>${ac.text}</li>`).join('')}
        </ul>
      </div>` : ''}

      ${filterConfig.detailed && us.tasks.length > 0 ? `
      <div class="section">
        <div class="section-title">Tasks (${us.tasks.filter(t => t.completed).length}/${us.tasks.length})</div>
        <ul class="task-list">
          ${us.tasks.map(task => `<li class="${task.completed ? 'completed' : ''}">${task.text}</li>`).join('')}
        </ul>
      </div>` : ''}

      ${us.dependencies.length > 0 ? `
      <div class="section">
        <div class="section-title">Dependencies</div>
        <div class="dependencies">
          ${us.dependencies.map(dep => `<span class="dependency">${dep}</span>`).join('')}
        </div>
      </div>` : ''}

      ${us.estimation ? `
      <div class="section">
        <div class="section-title">Estimation</div>
        <div>${us.estimation}</div>
      </div>` : ''}
    </div>`;
}

/**
 * Generate JSON export
 */
function generateJSON(data, filterConfig) {
  const outputPath = path.join(EXPORT_DIR, `backlog-${timestamp}.json`);

  const jsonData = {
    metadata: {
      project_name: data.projectName,
      generated_at: new Date().toISOString(),
      total_user_stories: data.userStories.length,
      version: '1.0.0'
    },
    statistics: data.statistics,
    epics: data.epics,
    user_stories: data.userStories
  };

  fs.writeFileSync(outputPath, JSON.stringify(jsonData, null, 2), 'utf-8');

  console.log('\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
  console.log('✅ EXPORT COMPLETE');
  console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n');
  console.log(`Format: JSON`);
  console.log(`User Stories exported: ${data.userStories.length}\n`);
  console.log('📁 Generated file:');
  console.log(`   ${outputPath}\n`);
}

/**
 * Generate CSV export
 */
function generateCSV(data, filterConfig) {
  const outputPath = path.join(EXPORT_DIR, `backlog-${timestamp}.csv`);

  const headers = ['ID', 'Title', 'Status', 'Priority', 'Complexity', 'Epic', 'Total Tasks', 'Completed Tasks', 'Progress %', 'Estimation', 'Dependencies'];
  const rows = data.userStories.map(us => {
    const totalTasks = us.tasks.length;
    const completedTasks = us.tasks.filter(t => t.completed).length;
    const progress = totalTasks > 0 ? Math.round((completedTasks / totalTasks) * 100) : 0;

    return [
      us.id,
      `"${us.title.replace(/"/g, '""')}"`,
      us.status,
      us.priority,
      us.complexity,
      us.epic || 'N/A',
      totalTasks,
      completedTasks,
      progress,
      `"${us.estimation}"`,
      `"${us.dependencies.join(', ')}"` || 'None'
    ].join(',');
  });

  const csv = [headers.join(','), ...rows].join('\n');
  fs.writeFileSync(outputPath, csv, 'utf-8');

  console.log('\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
  console.log('✅ EXPORT COMPLETE');
  console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n');
  console.log(`Format: CSV`);
  console.log(`User Stories exported: ${data.userStories.length}\n`);
  console.log('📁 Generated file:');
  console.log(`   ${outputPath}\n`);
}

/**
 * Generate Markdown export
 */
function generateMarkdown(data, filterConfig) {
  const outputPath = path.join(EXPORT_DIR, `backlog-${timestamp}.md`);

  let md = `# 📋 Backlog - ${data.projectName}\n\n`;
  md += `**Generated:** ${new Date().toLocaleString()}\n`;
  md += `**Total User Stories:** ${data.userStories.length}\n\n`;
  md += `---\n\n`;

  md += `## 📊 Overview\n\n`;
  md += `| Status | Count | % |\n`;
  md += `|--------|-------|---|\n`;
  md += `| ✅ Done | ${data.statistics.byStatus.done} | ${Math.round((data.statistics.byStatus.done / data.userStories.length) * 100)}% |\n`;
  md += `| 🔵 In Progress | ${data.statistics.byStatus.in_progress} | ${Math.round((data.statistics.byStatus.in_progress / data.userStories.length) * 100)}% |\n`;
  md += `| 🟡 To Do | ${data.statistics.byStatus.todo} | ${Math.round((data.statistics.byStatus.todo / data.userStories.length) * 100)}% |\n\n`;

  md += `---\n\n`;
  md += `## 🎯 User Stories\n\n`;

  // Group by epic
  const epicMap = new Map();
  data.userStories.forEach(us => {
    if (!epicMap.has(us.epic)) {
      epicMap.set(us.epic, []);
    }
    epicMap.get(us.epic).push(us);
  });

  data.epics.forEach(epic => {
    const epicUserStories = epicMap.get(epic.id) || [];
    if (epicUserStories.length === 0) return;

    md += `### Epic ${epic.number}: ${epic.name}\n\n`;

    epicUserStories.forEach(us => {
      const statusEmoji = us.status === 'DONE' ? '✅' : us.status === 'IN_PROGRESS' ? '🔵' : '🟡';
      const priorityEmoji = us.priority === 'High' ? '🔴' : us.priority === 'Medium' ? '🟠' : '🟢';

      md += `#### [${us.id}] - ${us.title}\n\n`;
      md += `**Status:** ${statusEmoji} ${us.status.replace('_', ' ')}  \n`;
      md += `**Priority:** ${priorityEmoji} ${us.priority}  \n`;
      md += `**Complexity:** ${us.complexity}\n\n`;

      if (us.narrative.as_a) {
        md += `> **As a** ${us.narrative.as_a}  \n`;
        md += `> **I want** ${us.narrative.i_want}  \n`;
        md += `> **So that** ${us.narrative.so_that}\n\n`;
      }

      if (filterConfig.detailed && us.acceptanceCriteria.length > 0) {
        md += `**Acceptance criteria:**\n`;
        us.acceptanceCriteria.forEach(ac => {
          md += `- ${ac.text}\n`;
        });
        md += `\n`;
      }

      if (us.dependencies.length > 0) {
        md += `**Dependencies:** ${us.dependencies.join(', ')}\n\n`;
      }

      if (us.estimation) {
        md += `**Estimation:** ${us.estimation}\n\n`;
      }

      md += `---\n\n`;
    });
  });

  fs.writeFileSync(outputPath, md, 'utf-8');

  console.log('\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
  console.log('✅ EXPORT COMPLETE');
  console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n');
  console.log(`Format: Markdown`);
  console.log(`User Stories exported: ${data.userStories.length}\n`);
  console.log('📁 Generated file:');
  console.log(`   ${outputPath}\n`);
}
