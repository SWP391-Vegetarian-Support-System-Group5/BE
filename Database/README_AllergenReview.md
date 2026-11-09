# Xét duyệt dị ứng — MySQL và FE

## Quy tắc

- Danh mục đã duyệt vẫn nằm trong `Allergens`; lựa chọn của user nằm trong `UserAllergens`.
- Yêu cầu mới nằm riêng trong `AllergenSubmissions`: `PENDING`, `APPROVED`, `REJECTED`.
- Tên trùng danh mục hoặc yêu cầu đang chờ của chính user được chặn trước khi gọi AI. So sánh chuẩn hóa hoa/thường, dấu, khoảng trắng và các nhóm đồng nghĩa đã biết, gồm mè/vừng/sesame. Tên đồng nghĩa chưa biết vẫn cần AI xác minh.
- AI kiểm tra nguyên liệu phù hợp ứng dụng ăn chay. Kết quả sai/trùng trả 422/409; AI lỗi trả 503, không thêm dữ liệu.
- MySQL có hai khóa duy nhất nullable: `PendingKey` (tên chuẩn hóa sau AI) và `SubmittedPendingKey` (tên user nhập). Hai request gửi đồng thời không được tạo hai yêu cầu chờ với cùng khóa của một user.
- Duyệt thêm hoặc dùng lại mục trong danh mục, không tự chọn thay user. User chọn rồi lưu hồ sơ.
- Từ chối bắt buộc có lý do 1–1000 ký tự. Yêu cầu đã xử lý tự rời hàng chờ, không xóa lịch sử. User có thể gửi lại sau khi sửa.
- Chỉ admin đang hoạt động được duyệt. Trạng thái là concurrency token; giao dịch MySQL serializable giữ quyết định và cập nhật danh mục nguyên tử.

## API

Tất cả cần Bearer token, `/api/admin/...` cần role ADMIN.

- `POST /api/users/me/allergens/custom`, body `{"name":"Hạt chia"}`: trả yêu cầu chờ duyệt (HTTP 200), không còn trả danh sách dị ứng đã chọn.
- `GET /api/users/me/allergen-requests`: lịch sử riêng user.
- `GET /api/admin/allergen-requests`: mặc định chỉ PENDING; `?status=APPROVED` hoặc `REJECTED` để xem lịch sử.
- `POST /api/admin/allergen-requests/{id}/approve`: không cần body.
- `POST /api/admin/allergen-requests/{id}/reject`, body `{"reason":"Lý do cụ thể"}`.
- `GET /api/allergens`: danh mục để user chọn, không chứa yêu cầu đang chờ/từ chối.
- `PUT /api/users/me/allergens`, body `{"allergenIds":[1,2]}`: thay toàn bộ lựa chọn.
- `GET /api/users/me/allergens`: đọc lại lựa chọn sau F5/đăng nhập lại.

Response yêu cầu có `id`, `userId`, `submittedName`, `name`, `status`, `message`, `rejectionReason`, `allergenId`, `createdAt`, `reviewedAt`, `reviewedByUserId`.
Lỗi nghiệp vụ dùng `{success:false,message,errors}`; duplicate/quyết định đã xử lý là 409, không có quyền 403, thiếu ID 404.

## Cập nhật MySQL

API không tự sửa schema khi khởi động. Từ thư mục BE chạy rõ ràng:

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --apply-allergen-review-schema
```

Lệnh đọc kết nối từ `API/.env`, `API/.aiven.env`, rồi biến môi trường; không in thông tin kết nối. Chỉ tạo bảng xét duyệt nếu thiếu, hoặc bổ sung khóa chặn trùng và ràng buộc lý do vào bảng xét duyệt cũ. Không DROP bảng/cột hay sửa tài khoản/công thức.

Lệnh đồng thời kiểm tra ràng buộc trên MySQL thật trong giao dịch luôn rollback; các hàng kiểm tra không được lưu. Tài khoản DB cần CREATE/ALTER và quyền ghi bảng xét duyệt. Cần ít nhất một user đang hoạt động cho phép thử ràng buộc có FK hợp lệ.

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --verify-allergen-review-schema
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --allergen-review
```

Lệnh verify không sửa schema, chỉ thực hiện phép thử giao dịch rollback; `--allergen-review` dùng storage/AI giả lập, không truy cập DB thật.

## Giao diện

- Hồ sơ giữ lựa chọn có sẵn; thêm panel gửi đề xuất và lịch sử trạng thái/lý do. Đồng bộ danh mục và lịch sử mỗi 15 giây khi tab hiển thị, khi quay lại cửa sổ, hoặc bấm Tải lại. Đồng bộ không ghi đè những checkbox user đang chỉnh mà chưa lưu.
- Admin mặc định xem PENDING, có lọc APPROVED/REJECTED; quyết định thành công đưa mục ra khỏi hàng chờ và nạp lại danh mục. Trang tự đồng bộ mỗi 15 giây.
- Hai giao diện dùng ngôn ngữ EN/VI hiện có; lý do/nội dung từ user hoặc backend giữ nguyên.
