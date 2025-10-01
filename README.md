# 📝 TaskManager

**TaskManager** is a multi-component project management and task tracking system built with **.NET 9**, **SignalR**, **RabbitMQ**, **Serilog**, and a layered architecture (**BLL**, **DAL**, **WebAPI**, **Client**, **Notifier**).  
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
- **Scalability**
  - Can be deployed locally, in Docker or in any cloud provider
  - Supports SQL Server or you can switch to your DB provider

---

## 🛠 Technology Stack
- **.NET 9**
- **C#**
- **Entity Framework Core**
- **SignalR**
- **RabbitMQ**
- **SQL Server**
- **Serilog**
- **Docker** (optional)

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
