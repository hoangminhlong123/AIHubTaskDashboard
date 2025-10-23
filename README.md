AIHub Task Management Module (V2)
 Overview

Task Management V2 là module mới trong hệ thống AIHub Dashboard, cho phép:

Xem danh sách công việc (Task List)

Thêm mới / cập nhật / xóa task

Đồng bộ với backend API hoặc mock data

Realtime cập nhật bằng SignalR

 Tech Stack

ASP.NET Core MVC

SignalR (Realtime)

Bootstrap 5

jQuery + AJAX

API Backend: https://aihubtasktracker-bwbz.onrender.com/api/v1

 Folder Structure
AIHubTaskDashboard/
├── Controllers/
│   ├── TasksController.cs
│   ├── ManagerTask.cs
│
├── Views/
│   ├── Tasks/IndexV2.cshtml
│   ├── ManagerTask/Index.cshtml
│
├── Services/ApiClientService.cs
├── Hubs/TaskHub.cs
└── wwwroot/js/tasks-v2.js

 Features

Hiển thị task theo người phụ trách (assignee_id)

Filter theo trạng thái (status)

Tạo / chỉnh sửa / xóa task qua AJAX

Gửi sự kiện SignalR để cập nhật realtime

Chế độ Mock Data cho dev test (ManagerTask)

 API Endpoints
Method	Endpoint	Mô tả
POST	/api/v1/auth/login	Đăng nhập lấy JWT
GET	/api/v1/tasks	Lấy danh sách task
POST	/api/v1/tasks	Tạo mới task
PUT	/api/v1/tasks/{id}	Cập nhật task
DELETE	/api/v1/tasks/{id}	Xóa task
 Local Setup
git checkout task-v2
dotnet run


Truy cập:

https://localhost:7291/Tasks/v2 → Task module thật

https://localhost:7291/ManagerTask → Mock data view

 Realtime Hub

Endpoint: /taskHub

Events:

"ReceiveTaskUpdate", "new_task"

"ReceiveTaskUpdate", "update_task"

"ReceiveTaskUpdate", "delete_task"
