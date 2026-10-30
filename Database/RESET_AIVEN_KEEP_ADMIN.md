# Kế hoạch làm sạch Aiven trước khi nhập dữ liệu mới

Trạng thái: **chưa thực thi**. Công cụ ở `Tests/RecipeCategories/AivenResetPreview.cs` chỉ xóa khi được gọi bằng cờ thực thi và chuỗi xác nhận riêng. Kết nối dùng `API/.aiven.env`; không ghi thông tin kết nối ra log.

## Kiểm kê chỉ đọc ngày 30/10/2026

- Database: `VegetarianSupportSystemDB`, 33 bảng.
- `Users`: 11 tài khoản, trong đó đúng một admin là `UserId=1`.
- `Posts`: 73; `Recipes`: 67; `VideoRecipeDrafts`: 43; `Restaurants`: 9; `MealPlans`: 12; `ChatSessions`: 24.
- `RecipeCategories`: sau reset chỉ giữ 10 danh mục hiển thị. Bốn danh mục `DIETARY_TYPE` từng dùng nội bộ được xóa riêng; khả năng phù hợp kiểu ăn chay vẫn lưu tại `RecipeDietCompatibility`.

## Phạm vi xóa dự kiến

Xóa toàn bộ bài viết, công thức, nguyên liệu, ánh xạ danh mục/BMI/kiểu ăn, bình luận, đánh giá, lưu bài, báo cáo, video draft, nhà hàng, thực đơn, chat, OTP và quan hệ dị ứng của người dùng. Xóa 10 tài khoản không phải admin và hồ sơ của họ. Xóa dị ứng tự thêm, giữ 6 loại dị ứng mặc định.

Giữ `Users.UserId=1` và hồ sơ admin; giữ `__EFMigrationsHistory`, `DietTypes`, `BMITypes`, `IngredientCategories` và 6 dị ứng mặc định vì ứng dụng cần các bảng tra cứu này. Tạo lại 10 danh mục món theo yêu cầu. ID tự tăng không được đặt lại.

Seed admin đã được đổi sang kiểm tra có **bất kỳ** tài khoản `ADMIN` nào, để không sinh thêm admin mặc định nếu tài khoản giữ lại sử dụng email khác.

Lệnh xóa kiểm tra tên database, danh sách bảng và đúng một admin `UserId=1` trước khi bắt đầu. Các thao tác xóa và tạo danh mục nằm trong một transaction. Nếu kiểm tra cuối thất bại, transaction sẽ rollback. Dữ liệu media trên Cloudinary và nháp thủ công trong bộ nhớ trình duyệt không thuộc database Aiven.

Trước khi chạy, dừng các instance API/worker kết nối Aiven để không phát sinh ghi mới trong lúc reset. Công cụ kiểm tra toàn bộ bảng là InnoDB trước khi thực hiện transaction.

## Kiểm kê và thực thi

Chạy từ thư mục `BE`:

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --preview-reset-aiven
```

Lệnh thực thi chỉ dùng sau khi đã xem kết quả kiểm kê và xác nhận riêng việc xóa. Không chạy lệnh này trong quy trình build hoặc migration tự động.

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --reset-aiven-keep-admin --confirm=DELETE_AIVEN_DATA_KEEP_ADMIN_1
```

Sau khi thực thi, chạy lại lệnh preview để đối chiếu số hàng. Không chạy backend bản cũ có seed 35 danh mục, vì bản cũ có thể thêm lại danh mục và bài mẫu.
