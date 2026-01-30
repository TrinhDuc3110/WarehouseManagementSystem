
```markdown
# Smart Warehouse Management System (SWMS)

> **Graduation Thesis - International University (IU) - VNU-HCM**
> 
> **Topic:** Design and Implementation of an Intelligent Warehouse Management System leveraging AI and QR Code Technology.

---

## 👨‍💻 Student Information

| Role | Name | Student ID | Major |
| :--- | :--- | :--- | :--- |
| **Student** | **Trịnh Văn Đức** | **ITITIU21182** | Information Technology |
| **Supervisor** | Mr. Duong | | Faculty of IT |

---

## 📖 Executive Summary

This thesis presents a comprehensive **Smart Warehouse Management System (SWMS)** designed to digitize inventory operations and enhance logistical efficiency. By integrating **QR Code** technology for rapid identification and **Artificial Intelligence (AI)** for storage optimization, the system solves traditional warehousing challenges such as inventory discrepancies and inefficient space utilization.

### 🌟 Key Features
* **🚀 Automated Inventory Tracking:** Real-time stock management via high-speed QR Code scanning.
* **🤖 AI-Driven Optimization:** Intelligent algorithms to suggest optimal storage locations and predict stock trends.
* **📊 Dynamic Reporting:** Automated generation of import/export reports and visual analytics.
* **⚡ Modern API Documentation:** Integrated **Scalar UI** for a superior, interactive API testing experience (replacing traditional Swagger).
* **🐳 Dockerized Deployment:** Fully containerized architecture ensuring consistent performance across any environment.

---

## 🛠 Technology Stack

### Backend
* **Framework:** ASP.NET Core 8.0 (Web API)
* **Architecture:** Clean Architecture with Repository Pattern
* **API Documentation:** Scalar (Modern UI)

### Frontend
* **Framework:** ReactJS (Single Page Application)
* **Styling:** CSS Modules / Styled-components
* **State Management:** React Hooks / Context API

### Infrastructure & Database
* **Database:** Microsoft SQL Server
* **Containerization:** Docker & Docker Compose
* **Orchestration:** Docker Compose

---

## 📂 Project Structure

The repository is organized to separate concerns effectively:

```text
E:\THESIS-WMS
├── 📂 WarehousePro        # Backend Source Code (.NET 8.0)
├── 📂 WarehouseProFE      # Frontend Source Code (ReactJS)
├── 📂 Report              # Full Thesis Report (LaTeX/PDF)
├── 📂 Slide               # Defense Presentation Slides
├── 📄 docker-compose.yml  # Docker orchestration configuration
├── 📄 data.sql            # Database initialization script
└── 📄 README.md           # Project Documentation

```

---

## 🚀 Deployment Guide (Docker)

This project is fully configured with Docker Compose, allowing you to spin up the entire stack (Backend, Frontend, and Database) with a single command.

### 1. Prerequisites

Ensure you have the following installed on your machine:

* [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Running and updated)
* [Git](https://git-scm.com/)

### 2. Installation & Running

Open your terminal (PowerShell, Command Prompt, or Terminal) and follow these steps:

**Step 1: Clone the Repository**

```bash
git clone [https://github.com/TrinhDuc3110/WarehouseManagementSystem.git](https://github.com/TrinhDuc3110/WarehouseManagementSystem.git)
cd WarehouseManagementSystem

```

**Step 2: Build and Run Containers**
Execute the following command to build the images and start the services:

```bash
docker-compose up -d --build

```

> *Note: The first launch may take a few minutes as Docker downloads the SQL Server image and builds the .NET/React dependencies.*

**Step 3: Verify Installation**
Once the process completes, check if the containers are running:

```bash
docker ps

```

### 3. Accessing the System

| Service | Access URL | Description |
| --- | --- | --- |
| **User Interface** | [http://localhost:3000](https://www.google.com/search?q=http://localhost:3000) | The main ReactJS Web Application |
| **API Docs (Scalar)** | [http://localhost:5000/scalar/v1](https://www.google.com/search?q=http://localhost:5000/scalar/v1) | Interactive API documentation |
| **Backend API** | [http://localhost:5000](https://www.google.com/search?q=http://localhost:5000) | Core API endpoints |
| **Database** | `localhost:1433` | SQL Server instance |

---

## 📸 System Previews & API Docs

### Scalar API Documentation

The system uses **Scalar** for API visualization. Navigate to `http://localhost:5000/scalar/v1` to explore the endpoints. Scalar provides a clean, modern interface for testing API calls directly from the browser.

---

## 📝 License & Acknowledgements

This project is submitted in partial fulfillment of the requirements for the **Bachelor of Engineering** degree at **International University - VNU-HCM**.

**Copyright © 2026 Trịnh Văn Đức.**
All rights reserved.

```
