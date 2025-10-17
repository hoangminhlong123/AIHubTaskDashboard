# AIHubTaskDashboard

Đây là một ứng dụng web **ASP.NET Core MVC** được xây dựng trên **.NET 8.0**. Dự án này có chức năng chính là một giao diện dashboard (bảng điều khiển) cho hệ thống AIHub Task Tracker, sử dụng các dịch vụ API bên ngoài và quản lý phiên (Session) người dùng.

-----

## 📁 Cấu Trúc Dự Án

Cấu trúc dự án điển hình của ASP.NET Core MVC:

| Tên File/Thư mục | Mô tả |
| :--- | :--- |
| **Controllers/** | Chứa các Controller (ví dụ: `Home/Index`) xử lý yêu cầu HTTP, tương tác với các dịch vụ và trả về View. |
| **Views/** | Chứa các tệp View (`.cshtml`) chịu trách nhiệm trình bày giao diện người dùng. |
| **Services/** | (Được suy ra) Chứa các lớp dịch vụ, như `ApiClientService` được đăng ký trong `Program.cs`. Đây là nơi chứa logic kinh doanh và tương tác với các dịch vụ bên ngoài (như API). |
| **Models/** | (Được suy ra) Chứa các lớp Model (thường là các lớp POCO) đại diện cho dữ liệu và logic nghiệp vụ. |
| **wwwroot/** | Thư mục gốc cho các tệp tĩnh (Static Files) như CSS, JavaScript, hình ảnh. |
| `Program.cs` | Tệp điểm vào của ứng dụng. Cấu hình các dịch vụ (Dependency Injection) và Request Pipeline (Middleware). |
| `AIHubTaskDashboard.csproj` | Tệp cấu hình dự án, xác định Framework mục tiêu và các gói NuGet phụ thuộc. |
| `appsettings.json` | Cấu hình ứng dụng chính, bao gồm chuỗi kết nối cơ sở dữ liệu và các cài đặt API. |
| `appsettings.Development.json` | Cấu hình dành riêng cho môi trường Development (Phát triển), ghi đè lên `appsettings.json` khi chạy ở môi trường này. |

-----

## ⚙️ Chi Tiết Cấu Hình Quan Trọng

### 1\. `AIHubTaskDashboard.csproj`

  * Framework: `net8.0`.
  * Thư viện cơ sở dữ liệu: Sử dụng PostgreSQL thông qua Entity Framework Core (EF Core). Các gói chính: `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL`.

### 2\. `appsettings.json`

  * ConnectionStrings:
      * `DefaultConnection`: Chuỗi kết nối đến cơ sở dữ liệu PostgreSQL (dịch vụ Render.com) với cấu hình SSL yêu cầu (`SSL Mode=Require; Trust Server Certificate=true`).
  * ApiSettings:
      * `BaseUrl`: URL cơ sở của dịch vụ API bên ngoài (`https://aihubtasktracker-bwbz.onrender.com/`).

### 3\. `Program.cs` (Cấu hình Dịch vụ và Middleware)

Ứng dụng sử dụng cấu hình sau trong `Program.cs`:

  * **Dịch vụ (Services):**
      * `AddControllersWithViews()`: Thêm các dịch vụ cần thiết cho mô hình MVC.
      * `AddHttpClient<ApiClientService>()`: Đăng ký `HttpClient` cho `ApiClientService` để gọi API.
      * `AddSingleton<IHttpContextAccessor, HttpContextAccessor>()`: Đăng ký để truy cập `HttpContext`.
      * `AddSession(options => ...)`: Đăng ký dịch vụ quản lý phiên (Session) với cấu hình:
          * `options.IdleTimeout = TimeSpan.FromHours(1)`: Phiên hết hạn sau 1 giờ không hoạt động.
          * `options.Cookie.HttpOnly = true`: Cookie phiên chỉ có thể truy cập bởi máy chủ (bảo mật).
          * `options.Cookie.IsEssential = true`: Cookie phiên là thiết yếu.
  * **Middleware (Request Pipeline):**
      * `UseHttpsRedirection()`: Chuyển hướng sang HTTPS.
      * `UseStaticFiles()`: Phục vụ các tệp tĩnh từ `wwwroot`.
      * `UseRouting()`: Thiết lập routing.
      * `UseSession()`: Kích hoạt middleware quản lý phiên.
      * `UseAuthorization()`: Kích hoạt ủy quyền (Authorization).
      * `MapControllerRoute(...)`: Định nghĩa route mặc định: `{controller=Home}/{action=Index}/{id?}`.

-----

## 🚀 Hướng Dẫn Cài Đặt (Setup Guide)

Để chạy dự án này trên môi trường phát triển cục bộ, bạn cần làm theo các bước sau:

### 1\. Yêu cầu Tiên quyết (Prerequisites)

  * **.NET 8 SDK** (hoặc phiên bản cao hơn, do dự án nhắm mục tiêu `net8.0`).
  * Một IDE như Visual Studio (được gợi ý bởi `AIHubTaskDashboard.csproj.user`) hoặc Visual Studio Code.

### 2\. Tải Mã Nguồn

Clone repository hoặc tải mã nguồn về máy cục bộ.

### 3\. Khôi phục Gói NuGet

Mở Terminal hoặc Command Prompt trong thư mục gốc của dự án và chạy lệnh sau:

```bash
dotnet restore
```

### 4\. Cấu Hình Cơ Sở Dữ Liệu và API

Bạn không cần phải thay đổi cấu hình mặc định để ứng dụng hoạt động với API và DB từ xa:

  * Kết nối DB: Đã được định nghĩa trong `appsettings.json` tại `ConnectionStrings:DefaultConnection`.
  * API Bên Ngoài: Base URL đã được đặt trong `appsettings.json` tại `ApiSettings:BaseUrl`.

### 5\. Chạy Dự Án

#### Sử dụng .NET CLI:

Chạy ứng dụng bằng lệnh:

```bash
dotnet run
```

#### Sử dụng Visual Studio:

1.  Mở tệp `AIHubTaskDashboard.csproj` hoặc giải pháp (solution) trong Visual Studio.
2.  Đảm bảo profile debug đang hoạt động là **https**.
3.  Nhấn F5 hoặc nút "Run" để chạy ứng dụng.

Ứng dụng sẽ khởi chạy trong trình duyệt và điều hướng đến trang chủ.
