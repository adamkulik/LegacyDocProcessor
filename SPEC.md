# LegacyDocProcessor — Specification

## 1. Project Overview

**Project Name:** LegacyDocProcessor  
**Type:** .NET 8 Console Application  
**Core Feature:** Process legacy documentation from a network share using LLM and publish to Confluence  
**Target Users:** Developers and technical staff processing legacy project documentation

## 2. Goals

- Scan a network share directory for supported file types
- Extract text content from: `.msg`, `.doc`, `.docx`, `.pdf`, `.txt`
- Filter out binary files, installers, and non-document files
- Use LLM to analyze and extract topics from each document
- **Merge content from multiple files into unified topic-based articles**
- Publish structured knowledge base articles to Confluence
- **Build a local Markdown-based knowledge base for offline access**
- Provide progress tracking and logging

## 3. Architecture

### 3.1 Project Structure
```
LegacyDocProcessor/
├── LegacyDocProcessor.sln
├── src/
│   └── LegacyDocProcessor/
│       ├── LegacyDocProcessor.csproj
│       ├── Program.cs
│       ├── Commands/
│       │   └── ScanCommand.cs
│       │   └── ProcessCommand.cs
│       │   └── PublishCommand.cs
│       ├── Services/
│       │   ├── FileScanner.cs
│       │   ├── TextExtractors/
│       │   │   ├── IMsgExtractor.cs
│       │   │   ├── DocExtractor.cs
│       │   │   ├── PdfExtractor.cs
│       │   │   └── TextExtractor.cs
│       │   ├── LlmClient.cs
│       │   ├── TopicAggregator.cs
│       │   ├── ConfluenceClient.cs
│       │   └── LocalKnowledgeBaseService.cs
│       ├── Models/
│       │   ├── Document.cs
│       │   ├── ProcessedDocument.cs
│       │   ├── Topic.cs
│       │   └── ConfluencePage.cs
│       └── Config/
│           └── AppConfig.cs
└── SPEC.md
```

### 3.2 Supported File Types

| Extension | Priority | Notes |
|-----------|----------|-------|
| `.msg`    | High     | Outlook email messages |
| `.doc`    | High     | Legacy Word (binary) |
| `.docx`   | High     | Modern Word |
| `.pdf`    | High     | Portable Document Format |
| `.txt`    | High     | Plain text |

### 3.3 File Filtering (Phase 1)

**Exclude patterns:**
- Binary executables: `.exe`, `.dll`, `.msi`, `.bat`, `.cmd`, `.ps1`
- Archives: `.zip`, `.rar`, `.7z`, `.tar`, `.gz`
- Images: `.jpg`, `.png`, `.gif`, `.bmp`, `.ico`
- Videos/Audio: `.mp4`, `.mp3`, `.wav`, `.avi`
- Installers: `.msi`, `.iso`, `.img`
- Compiled: `.class`, `.pyc`, `.o`, `.obj`

**Exclude directories:**
- `node_modules`, `bin`, `obj`, `packages`, `.git`, `.svn`

### 3.4 Topic-Based Processing (Key Change)

**Why:** Files are disorganized — a single file may cover multiple topics, and multiple files may cover the same topic.

**New Flow:**

1. **Scan** → Inventory all supported files
2. **Extract** → Get raw text from each file
3. **LLM Analysis** → For each file, extract:
   - Multiple topics (not just one category)
   - Relevance score per topic
   - Which other files relate to same topics
4. **Topic Aggregation** → Group all content by topic
5. **Merge** → Combine related content into unified topic pages
6. **Publish** → Topic-organized Confluence pages

**LLM Output per Document (JSON structured):**
```json
{
  "fileName": "project_overview.docx",
  "filePath": "\\\\server\\docs\\project_overview.docx",
  "topics": [
    {
      "topic": "Project Architecture",
      "relevance": 0.95,
      "summary": "Overview of system components and dependencies",
      "content": "Extracted content related to this topic..."
    },
    {
      "topic": "API Documentation",
      "relevance": 0.3,
      "summary": "Brief mention of REST endpoints",
      "content": "Content snippet..."
    }
  ],
  "relatedFiles": ["architecture_diagram.pdf", "api_spec.docx"],
  "confidence": 0.85
}
```

**Topic Aggregation Output:**
```json
{
  "topic": "Project Architecture",
  "sourceFiles": ["project_overview.docx", "architecture_diagram.pdf"],
  "mergedContent": "Combined and deduplicated content...",
  "sectionCount": 3,
  "confidence": 0.9
}
```

### 3.5 Confluence Integration

- **Auth:** SAML/OAuth via Microsoft Entra ID (to be clarified)
  - May require using user's existing session or service account
  - Alternative: Personal Access Token (PAT) if available
- **Space:** Configurable target space key
- **Page Structure:** Flat topic-based structure (no deep hierarchy)
- **Content:** Markdown converted to Confluence storage format

### 3.6 Local Knowledge Base

Build an offline, Markdown-based knowledge base from processed documents.

**Features:**
- Output directory: configurable (default: `./output/knowledge-base`)
- Folder structure: `topics/{topic-name}/index.md`
- Index file: `README.md` with topic overview
- Each topic page includes:
  - Title and summary
  - Source file references
  - Full merged content
  - Metadata (date processed, confidence score)

**Markdown format per topic:**
```markdown
# {Topic Name}

**Summary:** {LLM-generated summary}
**Sources:** [[file1.docx]], [[file2.pdf]]
**Processed:** {timestamp}
**Confidence:** {score}

---

{Merged content from all source files}

---

### Source References

- [[file1.docx]] - Relevance: 0.95
- [[file2.pdf]] - Relevance: 0.72
```

### 3.7 Merging Strategy

When multiple files cover the same topic:

1. **Deduplicate** repeated paragraphs/sentences
2. **Order by relevance** — higher relevance content first
3. **Add source references** — "Content from: [filename]"
4. **Preserve unique insights** — don't lose minority perspectives
5. **Handle conflicts** — if contradictory, keep both with note

## 4. Configuration

All configuration via `config.json`:
```json
{
  "sourcePath": "\\\\server\\share\\documentation",
  "llm": {
    "endpoint": "https://api.openai.com/v1",
    "model": "gpt-4",
    "apiKey": "sk-...",
    "maxTokens": 4000
  },
  "confluence": {
    "baseUrl": "https://confluence.internal.company.com",
    "spaceKey": "DOC",
    "auth": {
      "type": "saml" | "pat",
      "username": "user@company.com",
      "apiToken": "...",
      // SAML handled by Entra ID — requires session or service account
    }
  },
  "localKnowledgeBase": {
    "enabled": true,
    "outputPath": "./output/knowledge-base",
    "createIndex": true,
    "includeMetadata": true
  },
  "processing": {
    "maxFileSizeMb": 50,
    "batchSize": 10,
    "delayBetweenBatchesMs": 1000
  }
}
```

## 5. Commands

### 5.1 Scan
- List all supported files in source directory
- Show filtering stats (included/excluded)
- Output: JSON file with file inventory

### 5.2 Process
- Read scan output
- Extract text from each file
- Send to LLM for topic extraction
- Output: JSON with processed documents (includes topic list per document)

### 5.3 Aggregate
- Read processed documents
- Group by topic
- Merge related content
- Output: JSON with unified topic pages

### 5.4 Publish
- Read aggregated topics
- Create Confluence pages (one per topic)
- Handle duplicates via title matching

### 5.5 Export (Local KB)
- Read aggregated topics
- Generate Markdown files in output directory
- Create index/README with topic overview

## 6. Non-Functional Requirements

- **Logging:** Serilog with file + console sinks
- **Error Handling:** Continue on individual file errors, log and skip
- **Progress:** Show progress bar and ETA
- **Idempotency:** Update existing pages by title matching (don't duplicate)

## 7. Dependencies

- .NET 8 SDK
- MsgReader (`.msg` files)
- DocumentFormat.OpenXml (`.docx`)
- **Aspose.Total (`.doc` - licensed commercial product)**
- PdfPig (`.pdf` extraction)
- ConfluenceRESTClient or manual REST calls
- Serilog
- Spectre.Console (CLI UI)

## 8. Interruption & Resume Support

### 8.1 Current Status

**Pipeline:** `scan → scan-results.json → process → processed.json → aggregate → aggregated.json → publish/export`

| Command | Resume Support | Notes |
|---------|----------------|-------|
| `scan` | ✅ Idempotent | Can re-run anytime, produces same output |
| `process` | ✅ **Resumable** | Checkpoint-based with state file |
| `aggregate` | ✅ Works | Reads processed.json |
| `publish` | ✅ Idempotent | Handles duplicates |
| `export` | ✅ Idempotent | Can re-run |

### 8.2 Implementation

Checkpoint-based resume is implemented using a state file (`.processing-state/{scanFileName}-state.json`):

**State File Structure:**
```json
{
  "scanFile": "scan-results.json",
  "outputFile": "processed.json",
  "lastProcessedIndex": 450,
  "lastProcessedFile": "document_450.pdf",
  "totalFiles": 1000,
  "completedFiles": [
    "document_001.pdf",
    "document_002.docx",
    ...
  ],
  "failedFiles": [
    { "filePath": "document_123.msg", "errorMessage": "...", "failedAt": "...", "attemptCount": 1 }
  ],
  "processedDocuments": [
    { /* ProcessedKnowledge JSON objects */ }
  ],
  "startedAt": "2025-01-19T10:30:00Z",
  "lastUpdated": "2025-01-19T14:45:00Z"
}
```

**Behavior:**
1. On start of `process`:
   - Check if state file exists for the input scan file
   - If exists, load and skip already-completed files
   - Continue from `lastProcessedIndex + 1`
2. After each file is successfully processed:
   - Add to `completedFiles` list
   - Store processed document in `processedDocuments`
   - Update `lastProcessedIndex` and `lastUpdated`
3. If a file fails:
   - Add to `failedFiles` with error message and attempt count
   - Continue processing next file (don't stop)
4. On completion:
   - Show summary: X completed, Y failed, Z remaining
   - Option to retry failed files

**State File Location:**
- Stored in `.processing-state/` directory in the working directory
- Named after the scan file: `{scanFileName}-state.json`

**Command Line Options:**
- `--resume` - Resume from previous run (default if state file exists)
- `--force` - Start fresh, ignore state file
- `--retry-failed` - Only process previously failed files

### 8.3 User Workflow

```bash
# Normal run (creates state file)
LegacyDocProcessor process --input scan-results.json

# If interrupted, just run again - automatically resumes
LegacyDocProcessor process --input scan-results.json

# Force fresh start
LegacyDocProcessor process --input scan-results.json --force

# Retry only failed files
LegacyDocProcessor process --input scan-results.json --retry-failed
```

## 9. Unit Test Plan

### 9.1 Test Coverage Strategy

| Layer | Component | Test Focus |
|-------|-----------|-------------|
| **Models** | Document, Topic, ProcessedDocument | Serialization, validation |
| **Services** | FileScannerService | File filtering, inclusion logic |
| **Services** | TextExtractors | Extraction accuracy, edge cases |
| **Services** | TopicAggregator | Merging, deduplication, grouping |
| **Services** | ConfluenceClient | Markdown→Storage format conversion |
| **Services** | LocalKnowledgeBaseService | Markdown generation |
| **Services** | ProcessingStateService | Checkpoint, resume, retry logic |
| **Config** | AppConfig | Loading, validation |

### 9.2 Test Naming Convention

- `{ComponentName}.{MethodOrScenario}_{ExpectedBehavior}`
- Example: `FileScanner_ScanDirectory_WithExcludedExtensions_ExcludesBinaries`

### 9.3 Priority 1 Tests (Core Logic)

| # | Test | Description |
|---|------|-------------|
| 1 | FileScanner_FilterByExtension | Verify supported extensions are included |
| 2 | FileScanner_ExcludeBinaryExtensions | Verify .exe, .dll, .zip are excluded |
| 3 | FileScanner_ExcludeDirectories | Verify node_modules, bin, obj excluded |
| 4 | TopicAggregator_GroupByTopic | Single file → single topic grouping |
| 5 | TopicAggregator_MultipleFilesOneTopic | Multiple files merged under same topic |
| 6 | TopicAggregator_OrderByRelevance | Content ordered by relevance score |
| 7 | TopicAggregator_DeduplicateContent | Repeated paragraphs removed |
| 8 | ConfluenceClient_ConvertMarkdown_Bold | **bold** → `<strong>` |
| 9 | ConfluenceClient_ConvertMarkdown_Italic | _italic_ → `<em>` |
| 10 | ConfluenceClient_ConvertMarkdown_Code | \`code\` → `<pre><code>` |
| 11 | ConfluenceClient_ConvertMarkdown_Headers | # H1 → `<h1>` |
| 12 | ConfluenceClient_ConvertMarkdown_Lists | - item → `<ul><li>` |
| 13 | ConfluenceClient_ConvertMarkdown_Tables | Markdown table → Confluence table |
| 14 | LocalKB_GenerateTopicPage | Full topic page with metadata |
| 15 | LocalKB_GenerateIndex | README with topic overview |
| 16 | ProcessingState_SaveAndLoad | Verify state persistence |
| 17 | ProcessingState_ResumeFromCheckpoint | Verify resume functionality |
| 18 | ProcessingState_SkipCompletedFiles | Verify completed files skipped |
| 19 | ProcessingState_RetryFailedFiles | Verify retry logic |

### 9.4 Priority 2 Tests (Edge Cases)

| # | Test | Description |
|---|------|-------------|
| 20 | FileScanner_EmptyDirectory | Handle empty folder gracefully |
| 21 | FileScanner_NonExistentPath | Throw clear exception |
| 22 | TopicAggregator_EmptyInput | Handle no topics gracefully |
| 23 | TopicAggregator_SingleWordTopic | Handle single-word topic names |
| 24 | TopicAggregator_LongContent | Handle very long merged content |
| 25 | ConfluenceClient_EmptyMarkdown | Handle empty input |
| 26 | ConfluenceClient_SpecialCharacters | Escape HTML entities |
| 27 | LocalKB_CreateNestedFolders | Handle deep topic paths |

### 9.5 Priority 3 Tests (Integration Points)

| # | Test | Description |
|---|------|-------------|
| 28 | ConfigLoader_ValidJson | Load valid config.json |
| 29 | ConfigLoader_MissingFile | Handle missing config gracefully |
| 30 | ConfigLoader_InvalidJson | Report clear error for malformed JSON |

### 9.6 Test Data Requirements

- **Sample files:** Create minimal test files in `tests/LegacyDocProcessor.Tests/TestData/`
  - `test.txt` — simple text
  - `test.docx` — minimal Word doc
  - `test.pdf` — single page PDF
- **Mock responses:** JSON files for LLM response mocking
- **Fixtures:** Reusable test objects (sample Topic, Document)

### 9.7 Framework & Tools

- **Framework:** xUnit
- **Mocking:** Moq
- **Assertions:** FluentAssertions
- **Test Data:** Inline + JSON files in TestData folder

---

*Updated: 2025-01-19*
