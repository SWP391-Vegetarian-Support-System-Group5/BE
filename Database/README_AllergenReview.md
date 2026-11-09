# Duyệt yêu cầu dị ứng và tích hợp FE

User gửi tên dị ứng mới. BE giữ kiểm tra tên/trùng bằng AI như trước, sau đó lưu yêu cầu `PENDING`. Chỉ sau khi admin duyệt, dị ứng mới được đưa vào bảng `Allergens` và được chọn cho người gửi trong `UserAllergens`. Danh mục dị ứng có sẵn được giữ nguyên.

## Cập nhật database

Khi chạy bản BE này với `Database:RunStartupTasks=true` (mặc định), `AllergenSubmissionSchemaUpdate` tự tạo bảng `AllergenSubmissions` nếu chưa có. Tài khoản DB cần quyền CREATE TABLE. Không cần thêm cột vào `Allergens`. Chế độ tắt startup tasks không thực hiện cập nhật này.

Code và kiểm thử được chuẩn bị local; không có thao tác trực tiếp lên DB Aiven trong quá trình kiểm thử.

## API

Tất cả endpoint cần `Authorization: Bearer <token>`. Endpoint `/api/admin/...` chỉ cho role `ADMIN`.

| Method | Endpoint | Nội dung |
| --- | --- | --- |
| POST | `/api/users/me/allergens/custom` | Body `{"name":"Mè"}`. Trả yêu cầu đang chờ duyệt, HTTP 200. |
| GET | `/api/users/me/allergen-requests` | Lịch sử yêu cầu của chính user, gồm trạng thái và lý do từ chối. |
| GET | `/api/admin/allergen-requests` | Tất cả yêu cầu; thêm `?status=PENDING`, `APPROVED` hoặc `REJECTED` để lọc. |
| POST | `/api/admin/allergen-requests/{id}/approve` | Không cần body. Duyệt yêu cầu và trả kết quả. |
| POST | `/api/admin/allergen-requests/{id}/reject` | Body `{"reason":"Tên cần bổ sung thông tin để xác minh."}`. Lý do bắt buộc, tối đa 1000 ký tự. |
| GET | `/api/allergens` | Toàn bộ danh mục chung, gồm dị ứng mới đã được duyệt; không chứa yêu cầu pending/rejected. |
| GET | `/api/users/me/allergens` | Các dị ứng user đã chọn, gồm mục vừa được duyệt cho user. |

**Thay đổi response:** `POST /api/users/me/allergens/custom` trước đây trả `UserAllergensResponse`; nay trả `AllergenSubmissionResponse`. FE cần cập nhật cách đọc response, không thêm ngay một yêu cầu pending vào danh sách dị ứng đã chọn.

Các API duyệt/trả lịch sử dùng cùng cấu trúc (danh sách là mảng trực tiếp):

```json
{
  "id": 12,
  "userId": 7,
  "submittedName": "mè",
  "name": "Mè",
  "status": "REJECTED",
  "message": "Yêu cầu dị ứng bị từ chối: Tên cần bổ sung thông tin để xác minh.",
  "rejectionReason": "Tên cần bổ sung thông tin để xác minh.",
  "allergenId": null,
  "createdAt": "2026-11-08T08:00:00Z",
  "reviewedAt": "2026-11-08T09:00:00Z",
  "reviewedByUserId": 1
}
```

- `PENDING`: `allergenId`, `rejectionReason`, `reviewedAt`, `reviewedByUserId` đều null; `message` báo chờ duyệt.
- `APPROVED`: `allergenId` là ID trong danh mục; `rejectionReason` null. Nếu mục đã được thêm trong khi chờ, dùng lại mục đó.
- `REJECTED`: lưu nguyên lý do admin sau khi bỏ khoảng trắng đầu/cuối; FE đọc `rejectionReason` hoặc hiển thị `message`.
- Sau khi admin thao tác, tải lại hàng đợi và `/api/allergens`. User tải lại lịch sử để thấy kết quả; có thể polling khi trang đang mở. Chưa có push notification cho quyết định này.
- User được gửi lại sau khi bị từ chối. Gửi trùng yêu cầu đang chờ hoặc duyệt lại một yêu cầu đã xử lý trả HTTP 409.
- Lý do rỗng/quá dài trả 400; không có quyền trả 403; ID yêu cầu không tồn tại trả 404.
- AI từ chối ngay ở bước gửi vẫn trả lỗi 422 với `message`; trường hợp đó chưa tạo yêu cầu chờ admin. Thiếu dịch vụ AI trả 503, tên trùng trả 409.

Lỗi nghiệp vụ theo envelope hiện có: `{"success":false,"message":"...","errors":[]}`. FE hiển thị `message` bằng text, không render HTML từ nội dung lý do.

## Kiểm thử

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --allergen-review
```

Bộ kiểm thử dùng EF InMemory và AI giả lập, kiểm tra quyền HTTP, phân tách dữ liệu theo user, pending không vào catalog, duyệt/từ chối, lý do sau reload, gửi lại, chống xử lý lại, và dùng lại mục đã tồn tại. Không xác nhận giao dịch/khóa MySQL bằng bộ kiểm thử này.
