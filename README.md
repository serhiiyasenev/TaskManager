# 📝 TaskManager

![Tests](https://github.com/serhiiyasenev/TaskManager/actions/workflows/run-tests.yml/badge.svg)
![Coverage](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/serhiiyasenev/43220e786710833306a7a0e291f25449/raw/coverage.json)
![AI Validation](https://github.com/serhiiyasenev/TaskManager/actions/workflows/ai-validation.yml/badge.svg)

**TaskManager** is a multi-component project management and task tracking system built with **.NET 10**, **SignalR**, **RabbitMQ**, **Serilog**, and a layered architecture (**BLL**, **DAL**, **WebAPI**, **Client**, **Notifier**).  
The system supports **real-time notifications**, **asynchronous task processing**, **analytics**, and **modular expansion**.

---

## 🚀 Main Features
- **Project and Task Management**
  - Provide Analytics about projects: analyze tasks, examine team structure, investigate projects, measure user performance, compare teams and participants
  - Create, update, and delete projects, tasks, users, and teams
  - Link tasks to projects and users
  - Auth
- **SignalR Integration**
  - Instant real-time messages between the server and clients
- **RabbitMQ Messaging**
  - Asynchronous communication between services
- **Layered Architecture**
  - **BLL** (Business Logic Layer)
  - **DAL** (Data Access Layer)
  - **WebAPI** (REST API)
  - **Client** (Console client application)
  - **Notifier** (Notification service)
  - **AI** (Directory contains all AI Governance & Context Engineering artifacts)
- **Scalability**
  - Can be deployed locally, in Docker or in any cloud provider
  - Supports SQL Server or you can switch to your DB provider

---

## 🛠 Technology Stack
- **.NET 10**
- **C#**
- **Entity Framework Core**
- **SignalR**
- **RabbitMQ**
- **SQL Server**
- **Serilog**
- **Docker** (optional)

---

## 📊 Main types of Code Coverage
- **Line coverage** — what % of lines of code were executed by tests.
- **Statement coverage** — what % of instructions/operators were executed.
- **Branch coverage** — what % of condition branches (`if/else`, `switch`, `case`) were passed.
- **Condition coverage** — whether individual Boolean conditions were checked inside complex expressions.
- **Functions and Methods coverage** — what % of methods or functions were called by tests.
- **Path coverage** — what % of possible code execution paths were passed.

---

## 📦 Sequence Diagram
```mermaid
sequenceDiagram
    participant Client as Console Client/Postman/WebUI
    participant WebAPI as WebAPI
    participant Notifier as Notifier Service
    participant Rabbit as RabbitMQ
    participant SignalR as SignalR Hub

    Client->>WebAPI: Request data or create task
    WebAPI-->>Client: Response with task/project info
    WebAPI->>Rabbit: Publish message about task update
    Rabbit->>Notifier: Deliver task update message
    Notifier->>SignalR: Send "ReceiveMessage" event
    SignalR->>Client: Display notification in real time
```

## 📦 Project Structure
```mermaid
graph TD
    A[TaskManager Solution]
    subgraph LAYERS[Layers]
      B[BLL - Business Logic]
      C[DAL - Data Access Layer]
      D[WebAPI - REST API]
      E[Client - Console/SPA]
      F[Notifier - Background Service]
      H[SignalR Hub]
      R[RabbitMQ]
      DB[(SQL Server)]
      LG[Serilog Logger]
      LS[(Logs Store: Grafana Loki)]
      G[Grafana Dashboards]
    end

    A --> D
    A --> B
    A --> C
    A --> E
    A --> F
    A --> H

    %% Dependencies
    D --> B
    B --> C
    C -->|EF Core| DB

    %% Messaging
    D -->|Publish| R
    F -->|Consume| R

    %% Realtime
    D -->|WebSockets| H
    F -->|Notify| H
    H -->|Push| E

    %% Clients call API
    E -->|HTTP| D

    %% Logging pipeline
    D -.->|logs| LG
    B -.->|logs| LG
    C -.->|logs| LG
    F -.->|logs| LG
    H -.->|logs| LG

    LG -->|sinks| LS
    G ---|queries/visualizes| LS
```

⚙️ Local Setup Instructions

Install SQL Server and RabbitMQ (or run them via Docker)

Configure connection strings in appsettings.json for WebAPI and Notifier

`WebAPI/appsettings.Development.json` has `BootstrapAdmin` enabled in `Development`, but the password is intentionally empty.

- Email: `admin@example.com`
- Password: set `BootstrapAdmin:Password` via User Secrets or an environment variable (for example `BootstrapAdmin__Password`) before starting the app

If `BootstrapAdmin:Password` is missing (or left as a placeholder value), WebAPI startup will fail with a configuration error.

Run the following commands in the projects root:

```bash
dotnet run --project WebAPI
dotnet run --project Notifier
dotnet run --project Client
```

Use the console client to interact with the system

Also, you can use Swagger, Postman or any other client to execute Web API requests 

0. Get Tasks Count In Projects By User Id
1. Get Capital Tasks By User Id
2. Get Projects By Team Size
3. Get Sorted Team By Members With Year
4. Get Sorted Users With Sorted Tasks
5. Get User Info
6. Get Projects Info
7. Get Sorted Filtered Page Of Projects
8. Get Tasks Status By Project User Id
9. Start Timer Service To Execute Random Tasks With a Delay
10. Stop Timer Service
11. Exit the program

<img src="Img_1.jpg" style="max-width: 100%; height: auto;"/>

<img src="Img_2.jpg" style="max-width: 100%; height: auto;"/>

<img src="Img_3.jpg" style="max-width: 100%; height: auto;"/>

<img src="Grafana_1.jpg" style="max-width: 100%; height: auto;"/>

<img src="Grafana_2.jpg" style="max-width: 100%; height: auto;"/>

---
## 🛢️ Database Diagram

```mermaid
erDiagram
    USER {
        int id
        int teamId
        string userName
        string normalizedUserName
        string email
        string normalizedEmail
        string firstName
        string lastName
        datetime registeredAt
        date birthDay
    }

    TEAM {
        int id
        string name
        datetime createdAt
    }

    PROJECT {
        int id
        int authorId
        int teamId
        string name
        string description
        datetime createdAt
        datetime deadline
    }

    TASK {
        int id
        int projectId
        int performerId
        string name
        string description
        int state
        datetime createdAt
        datetime finishedAt
    }

    EXECUTED_TASK {
        int id
        int taskId
        string taskName
        datetime createdAt
    }

    ROLE {
        int id
        string name
        string normalizedName
    }

    USER_ROLE {
        int userId
        int roleId
    }

    %% ---------- Relationships ----------
    TEAM    ||--o{ USER    : has
    TEAM    ||--o{ PROJECT : owns
    USER    ||--o{ PROJECT : authors
    PROJECT ||--o{ TASK    : contains
    USER    ||--o{ TASK    : performs
    TASK    ||--o{ EXECUTED_TASK : logs

    ROLE ||--o{ USER_ROLE : maps
    USER ||--o{ USER_ROLE : maps
```

---

## 📘 AI System Overview

TaskManager is fully integrated with an **AI Governance & Context Engineering System**
based on the **ACE-FCA (Advanced Context Engineering — Frequent Compaction Approach)** standard.

This framework ensures that all AI-assisted and automated development remains  
**structured, reproducible, transparent, and human-reviewable**.

---

### 🧠 Core AI Artifacts (`/AI`)

| File / Folder | Purpose |
|----------------|----------|
| [`AI/README.md`](./AI/README.md) | Overview of AI governance logic and workflow phases. |
| [`AI/AGENTS.md`](./AI/AGENTS.md) | Rules for AI context management and compaction policy. |
| [`AI/AI_GUIDE.md`](./AI/AI_GUIDE.md) | Integration manual for AI assistants, Copilot, and CI tools. |
| [`AI/plan.md`](./AI/plan.md) | Active roadmap of AI-driven development steps. |
| [`AI/research.md`](./AI/research.md) | Technical findings and contextual data for planning. |
| [`AI/decisions.md`](./AI/decisions.md) | Architecture Decision Records (ADRs). |
| [`AI/traces/`](./AI/traces/) | Historical logs of all AI actions and completed steps. |

---

### ⚙️ GitHub Automation (`/.github`)

| File / Workflow | Description |
|------------------|-------------|
| [`ABOUT.md`](./.github/ABOUT.md) | GitHub-level configuration files and workflow definitions. |
| [`PULL_REQUEST_TEMPLATE.md`](./.github/PULL_REQUEST_TEMPLATE.md) | Standardized PR template for AI-assisted and manual submissions. |
| [`workflows/ai-validation.yml`](./.github/workflows/ai-validation.yml) | Validates all AI artifacts and ACE-FCA compliance for each PR. |
| [`workflows/ai-auto-trace.yml`](./.github/workflows/ai-auto-trace.yml) | Automatically appends trace entries after each merge. |
| [`workflows/README.md`](./.github/workflows/README.md) | Documentation for all GitHub workflows. |

---

### 🧩 Governance Principles

- 🧠 **AI never acts blindly** — it follows explicit context from `research.md`, `plan.md`, and `decisions.md`.  
- 🧾 **All AI actions are logged** — every merge creates a trace file in `AI/traces/`.  
- 🧩 **All contributions are reviewable** — human reviewers validate only `plan` and `decisions`, not raw code.  
- 🧼 **Context hygiene enforced** — the AI resets and compacts memory according to `AGENTS.md`.  

---

### 🚀 Validation & Transparency

| Workflow | Status |
|-----------|---------|
| AI Validation | ![AI Validation](https://github.com/serhiiyasenev/TaskManager/actions/workflows/ai-validation.yml/badge.svg) |
| AI Auto-Trace | ![AI Auto-Trace](https://github.com/serhiiyasenev/TaskManager/actions/workflows/ai-auto-trace.yml/badge.svg) |

---

> 💡 **Tip:** Before making changes with AI assistance, always review  
> [`AI/AI_GUIDE.md`](./AI/AI_GUIDE.md) to understand context loading, reset, and compaction rules.

---

### 🧪 AI & QA

```mermaid
graph LR
    A["🧠 AI Assistive Layer<br/>(Arcetix, OpenAI, Testim, Mabl, Anthropic)"]
    B["⚙️ Automation Framework Layer<br/>(Playwright, Selenium, Cypress, Robot Framework)"]
    C["🧮 Core Testing Layer<br/>(Execution Engine, 'Input → Action → Observation → Assertion → Report' Cycle)"]
    D["🧰 Infrastructure & Orchestration<br/>(CI/CD, Jenkins, GitHub Actions, Azure Pipelines)"]
    E["📦 System Under Test (SUT)<br/>(Web App, Desktop, API, DB, Cloud)"]
    F["💻 Monitoring<br/>(Grafana, Loki, Serilog, Elasticsearch, Cloud Systems)"]

    A --> B
    B --> C
    C --> D
    D --> E

    A -.->|Generates & Optimizes Tests| B
    B -.->|Executes Tests| C
    C -.->|Reports Results| D
    D -.->|Deploys & Monitors| E
    E -.->|Feeds Metrics| F
```
