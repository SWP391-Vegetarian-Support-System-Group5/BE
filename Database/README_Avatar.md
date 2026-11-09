# Ảnh đại diện hồ sơ

## API và dữ liệu FE

- `PUT /api/users/me/avatar`: Bearer token, `multipart/form-data`, trường `file`. Không tự đặt Content-Type của FormData trong FE.
- `DELETE /api/users/me/avatar`: Bearer token, không body.
- Cả hai trả HTTP 200: `{ "imageUrl": "https://res.cloudinary.com/...", "isGenerated": false, "previousImageDeleted": true }`.
- Xóa trả `imageUrl: null`. User ID chỉ lấy từ JWT, không lấy từ form/body.
- `GET /api/users/me`, login, cập nhật hồ sơ và vị trí đều trả `avatarUrl`. Không dùng `imageUrl` để đọc avatar từ response hồ sơ.
- JPEG/PNG/WebP, tối đa 5 MiB. BE kiểm tra chữ ký bytes và MIME; rỗng/sai định dạng trả 415, quá lớn trả 413, thiếu trường file trả 400.
- 401: chưa đăng nhập; 404: hồ sơ không tồn tại/không hoạt động; 409: có cập nhật đồng thời; 503: Cloudinary lỗi hoặc chưa cấu hình.

FE có sẵn `uploadAvatar`, `deleteAvatar`, `ImageManager` và cập nhật session user sau khi nhận kết quả. Bản sửa xác minh response trước khi áp dụng, bỏ kết quả khi tài khoản đã đổi. Sau lưu, FE đọc lại hồ sơ với `cache: no-store`.

## MySQL

Thêm hai cột nullable vào `UserProfiles`: `AvatarUrl varchar(1000)` và `AvatarPublicId varchar(255)`. Không sửa các trường sức khỏe/vị trí, không DROP dữ liệu. `AvatarPublicId` là concurrency token của EF; BE trả 409 cho cập nhật hồ sơ bị xung đột.

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --apply-avatar-schema
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --verify-avatar-schema
```

Lệnh đọc kết nối trong `API/.env`, `API/.aiven.env` và biến môi trường; không in credentials. API không tự cập nhật schema khi khởi động. Sau khi schema đã sẵn sàng, cần rebuild và chạy lại BE để nạp route/model mới.

## Lưu và thay ảnh

Cloudinary dùng cấu hình hiện có, thư mục `{Cloudinary:ImageFolder}/avatars/{userId}`, public ID mới cho mỗi lần tải. Ảnh mới được tải lên, lưu DB thành công rồi mới dọn ảnh cũ. Lưu DB lỗi sẽ thử dọn ảnh mới; lỗi cạnh tranh trả 409, không ghi đè ảnh mới hơn.

Chỉ xóa asset có public ID trong đúng thư mục avatar của user. `previousImageDeleted: false` nghĩa là thay đổi DB đã lưu nhưng ảnh cũ chưa được dọn, hoặc ảnh cũ không có public ID quản lý; không báo upload thất bại trong trường hợp này. Không có worker thử lại việc dọn ảnh; cần theo dõi warning logs.

## Kiểm tra

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --avatar-api
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --verify-avatar-cloudinary
```

`--avatar-api` kiểm tra controller/JWT/dịch vụ/đọc lại với DB và storage cô lập, gồm giữ ảnh khi lỗi, cập nhật đồng thời và phân tách tài khoản. `--verify-avatar-cloudinary` thực sự tải một ảnh nhỏ vào thư mục kiểm tra avatar user 0 rồi xóa đúng asset mới; không ghi hồ sơ user.

Thay đổi này chỉ khôi phục avatar. Không bổ sung API ảnh riêng bữa ăn hoặc AI tạo ảnh.
