# 📝 TaskManager

**TaskManager** is a multi-component project management and task tracking system built with **.NET 9**, **SignalR**, **RabbitMQ**, **Serilog**, and a layered architecture (**BLL**, **DAL**, **WebAPI**, **Client**, **Notifier**).  
The system supports **real-time notifications**, **asynchronous task processing**, **analytics**, and **modular expansion**.

---

## 🚀 Main Features
- **Project and Task Management**
  - Provide Analytics about projects: analyze tasks, examine team structure, investigate projects, measure user performance, compare teams and participants
  - Create, update, and delete projects, tasks, users, teams
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

    %% Logging (dotted)
    D -.->|logs| LG
    B -.->|logs| LG
    C -.->|logs| LG
    F -.->|logs| LG
    H -.->|logs| LG

```

⚙️ Local Setup Instructions
Install SQL Server and RabbitMQ (or run them via Docker)

Configure connection strings in appsettings.json for WebAPI and Notifier

Run the following commands in project root:

```bash
dotnet run --project WebAPI
dotnet run --project Notifier
dotnet run --project Client
```

Use the console client to interact with the system:

0. Get Tasks Count In Projects By User Id
1. Get Capital Tasks By User Id
2. Get Projects By Team Size
3. Get Sorted Team By Members With Year
4. Get Sorted Users With Sorted Tasks
5. Get User Info
6. Get Projects Info
7. Get Sorted Filtered Page Of Projects
8. Start Timer Service To Execute Random Tasks With Delay
9. Stop Timer Service
10. Exit the program

<img src="Img_1.jpg" style="max-width: 100%; height: auto;"/>

<img src="Img_2.jpg" style="max-width: 100%; height: auto;"/>
