# 📝 TaskManager

**TaskManager** is a multi-component project management and task tracking system built with **.NET 9**, **SignalR**, **RabbitMQ**, and a layered architecture (**BLL**, **DAL**, **WebAPI**, **Client**, **Notifier**).  
The system supports **real-time notifications**, **asynchronous task processing**, and **modular expansion**.

---

## 🚀 Main Features
- **Project and Task Management**
  - Create, update, and delete tasks
  - Link tasks to projects and users
- **SignalR Integration**
  - Instant real-time messages between server and clients
- **RabbitMQ Messaging**
  - Asynchronous communication between services
- **Layered Architecture**
  - **BLL** (Business Logic Layer)
  - **DAL** (Data Access Layer)
  - **WebAPI** (REST API)
  - **Client** (Console client application)
  - **Notifier** (Notification service)
- **Scalability**
  - Can be deployed locally or in Docker
  - Supports SQL Server

---

## 🛠 Technology Stack
- **.NET 9**
- **C#**
- **Entity Framework Core**
- **SignalR**
- **RabbitMQ**
- **SQL Server**
- **Docker** (optional)

---

## 📦 Project Structure
```mermaid
graph TD
    A[TaskManager Solution] --> B[BLL - Business Logic]
    A --> C[DAL - Data Access Layer]
    A --> D[WebAPI - REST API]
    A --> E[Client - Console App]
    A --> F[Notifier - Notification Service]

    C -->|Entity Framework Core| DB[(SQL Server)]
    E -->|SignalR| D
    F -->|RabbitMQ| D
```

## 📦 Sequence Diagram
```mermaid
sequenceDiagram
    participant Client as Console Client
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