# Schema Aiven thực tế — 29/10/2026

Đã đối chiếu `information_schema.COLUMNS` của Aiven `VegetarianSupportSystemDB` với entity, API, BLL và FE. Sau khi thêm cột ảnh và dọn schema ngày 29/10/2026, có **32 bảng ứng dụng + 1 bảng lịch sử EF, tổng 163 cột**. `PK` là khóa chính, `FK` là khóa ngoại. Property điều hướng của C# không phải cột SQL.

## Tài khoản và hồ sơ

- **Users (11 hàng):** `UserId` PK tài khoản; `Email` đăng nhập; `PasswordHash` mật khẩu băm; `Role` quyền; `CreatedAt`, `UpdatedAt` thời điểm tạo/sửa; `IsActive` khóa/mở; `IsEmailVerified` xác nhận email.
- **UserProfiles (11):** `UserId` PK/FK Users; `FullName` tên; `Sex`, `BirthDate` thông tin cá nhân; `HeightCm`, `WeightKg` tính BMI; `DietTypeId` FK kiểu ăn chay; `Latitude`, `Longitude` vị trí đã lưu; `ActivityLevel` mức vận động.
- **EmailOtpCodes (3):** `EmailOtpCodeId` PK; `Email` người nhận; `Purpose` đăng ký/đặt lại mật khẩu; `CodeHash` mã băm; `ExpiresAt` hết hạn; `FailedAttempts` số lần nhập sai; `UsedAt` thời điểm sử dụng; `CreatedAt` thời điểm tạo.
- **DietTypes (5):** `DietTypeId` PK; `Name` mã kiểu ăn chay.
- **Allergens (7):** `AllergenId` PK; `Name` tên dị ứng trong danh mục dùng chung.
- **UserAllergens (9):** `UserId` FK người dùng; `AllergenId` FK dị ứng; cặp này là PK để không gắn trùng.

## Bài viết và công thức

- **Posts (73):** `PostId` PK; `UserId` tác giả; `Title`, `Content` tiêu đề/nội dung; `PostType` loại bài; `VideoUrl` video; `Status` nháp/đã đăng/đã gỡ; `ThumbnailUrl` ảnh xem trước; `CreatedAt`, `UpdatedAt` thời điểm.
- **Comments (17):** `CommentId` PK; `PostId` bài; `UserId` người viết; `ParentCommentId` bình luận cha; `Content` nội dung; `Status` trạng thái; `CreatedAt`, `UpdatedAt` thời điểm.
- **PostRatings (11):** `PostId`, `UserId` PK ghép xác định người chấm bài; `Rating` số điểm.
- **Bookmarks (5):** `UserId`, `PostId` PK ghép xác định người lưu bài.
- **Recipes (67):** `PostId` PK/FK Posts; `PrepMinutes`, `CookMinutes` thời gian; `Servings` số phần; `VideoUrl` URL video; `ImageUrl` ảnh món ăn; `CaloriesPerServing` calo/phần (API còn hỗ trợ, meal planner không dùng); `RecipeName` tên món.
- **RecipeSteps (230):** `RecipeStepId` PK; `RecipeId` FK món; `StepNumber` thứ tự; `Instruction` hướng dẫn.
- **IngredientCategories (5):** `IngredientCategoryId` PK; `Code` mã nhóm; `Name` tên; `DietaryGroup` quy tắc PLANT/DAIRY/EGG/HONEY/UNVERIFIED.
- **Ingredients (152):** `IngredientId` PK; `Name` tên dùng chung; `IngredientCategoryId` FK nhóm; `AllergenId` FK dị ứng chuẩn, có thể null.
- **RecipeIngredients (330):** `RecipeIngredientId` PK; `RecipeId` FK món; `IngredientId` FK nguyên liệu dùng chung; `Amount` lượng trong món.
- **RecipeCategories (35):** `Id` PK; `Name` tên; `Slug` mã URL; `Type` nhóm MEAL_TYPE/CUISINE/DISH_TYPE/LIFESTYLE/DIETARY_TYPE; `Description` mô tả; `ImageUrl` ảnh; `IsActive` bật/tắt; `CreatedAt` thời điểm tạo.
- **RecipeCategoryMappings (197):** `RecipeId`, `CategoryId` PK ghép/FK, liên kết nhiều món và nhiều danh mục.
- **RecipeDietCompatibility (317):** `RecipeId`, `DietTypeId` PK ghép/FK, liên kết món với kiểu ăn chay; `IsCompatible` cờ mà meal planner kiểm tra.
- **BMITypes (3):** `BMITypeId` PK; `Code` UNDERWEIGHT/NORMAL/OBESE; `Name` tên hiển thị; `Description` mô tả.
- **RecipeBMITypes (57):** `RecipeId`, `BMITypeId` PK ghép/FK, admin gán món cho một hay nhiều nhóm BMI.

## Nhà hàng và thực đơn

- **Restaurants (8):** `RestaurantId` PK; `Name` tên; `Address` địa chỉ; `Latitude`, `Longitude` tọa độ; `ImageUrl` ảnh nhà hàng. Loại ăn chay được lưu trong `RestaurantDietTypes`.
- **RestaurantDietTypes (8):** `RestaurantId`, `DietTypeId` PK ghép/FK, một nhà hàng có nhiều kiểu ăn chay.
- **RestaurantFoods (17):** `RestaurantId`, `FoodName` PK ghép, tên món nhà hàng để tìm kiếm/hiển thị.
- **RestaurantIngredients (5):** `RestaurantId`, `IngredientId` PK ghép/FK, nguyên liệu admin gắn để lọc nhà hàng.
- **MealPlans (12):** `MealPlanId` PK; `UserId` chủ thực đơn; `StartDate` ngày bắt đầu; `AvailableIngredients` nguyên liệu nhập; `BMI` BMI tại thời điểm tạo.
- **MealPlanMeals (199):** `MealPlanMealId` PK; `MealPlanId` FK thực đơn; `DayNumber` ngày thứ mấy; `MealType` bữa; `RecipeId` FK món.

## AI, nháp và kiểm duyệt

- **ChatSessions (23):** `ChatSessionId` PK; `UserId` chủ phiên nếu đăng nhập; `GuestAccessTokenHash` nhận diện phiên khách; `CreatedAt` thời điểm mở.
- **ChatMessages (82):** `ChatMessageId` PK; `ChatSessionId` FK phiên; `Sender` người dùng/AI; `Content` tin nhắn; `CreatedAt` thời điểm.
- **VideoRecipeDrafts (42):** `VideoRecipeDraftId` PK; `UserId` chủ nháp; `VideoUrl` video; `GeminiFileName` định danh file Google; `Status` tiến trình; `Title`, `RecipeName`, `Description` nội dung đề xuất; `Transcript` lời nói nhận diện; `EstimatedPrepMinutes` thời gian; `ErrorMessage` lỗi; `CreatedAt`, `UpdatedAt` thời điểm; `RecipePostId` bài công thức sau khi đăng.
- **VideoRecipeDraftIngredients (298):** `VideoRecipeDraftIngredientId` PK; `VideoRecipeDraftId` FK nháp; `IngredientName` tên; `Amount` lượng; `DietaryGroup` nhóm tạm; `AllergenId` dị ứng nếu có. Khi đăng, hệ thống mới liên kết vào bảng Ingredients/RecipeIngredients.
- **VideoRecipeDraftSteps (186):** `VideoRecipeDraftStepId` PK; `VideoRecipeDraftId` FK nháp; `StepNumber` thứ tự; `Instruction` cách làm.
- **ModerationFlags (11):** `FlagId` PK; `PostId` hoặc `CommentId` đối tượng; `Source` AI/người báo cáo; `ReportedBy` người báo; `Reason` lý do; `Status` kết quả xử lý; `ReviewedBy` admin.
- **__EFMigrationsHistory (13):** `MigrationId` migration đã chạy; `ProductVersion` phiên bản EF Core. Bảng hệ thống cần giữ.

## Đánh giá cột/bảng có thể dư

- **Đã xóa trên Aiven ngày 29/10/2026:** `Restaurants.DietTypeId`; `Posts.ImageUrl`, `Posts.ImagePublicId`; 7 cột ảnh cũ của `MealPlanMeals`; `UserProfiles.AvatarUrl`, `UserProfiles.AvatarPublicId`; bảng `UserNotifications`. Việc xóa này cũng loại bỏ 3 thông báo cũ, 1 ảnh đại diện cũ và 2 trạng thái tạo ảnh cũ vốn không được hệ thống hiện tại sử dụng.
- **Đang dùng:** `Recipes.Servings` hiển thị ở chi tiết món; `Recipes.CaloriesPerServing` còn nhận/trả qua API và 50 món có giá trị nhưng meal planner không tính calo; `RecipeDietCompatibility.IsCompatible` được lọc; `__EFMigrationsHistory` cần cho migration.
- **Khoảng trống dữ liệu:** 6 công thức đã đăng chưa gán BMI; 2 công thức chưa có diet type tương thích; 1 hồ sơ thiếu diet type; 1 hồ sơ thiếu chiều cao/cân nặng; 18/42 video draft đang FAILED. Các số này không tự chứng minh lỗi ở phiên bản code mới, nhưng giải thích vì sao một số luồng không có kết quả.

## Chức năng hệ thống và kiểm thử

- **Tài khoản:** đăng ký, OTP email, đăng nhập JWT, quên/đặt lại mật khẩu, hồ sơ, địa chỉ, kiểu ăn chay, dị ứng có sẵn và dị ứng mới qua AI.
- **Cộng đồng:** bài viết/công thức, nháp, video, tìm kiếm/lọc, bình luận/trả lời, đánh giá sao, bookmark, báo cáo, AI kiểm duyệt và admin xử lý.
- **Meal planner:** ưu tiên món trùng nguyên liệu nhập; khi không có món trùng, chọn ngẫu nhiên trong các món vẫn phù hợp dị ứng, kiểu ăn chay và nhóm BMI do admin gán; tạo/xem/tạo lại/thay món có hạn mức. Không tính calories/macro.
- **Nhà hàng:** admin thêm/sửa/xóa, geocode địa chỉ, nhiều diet type, món và nguyên liệu; người dùng tìm nhà hàng đã lưu theo vị trí/kiểu ăn/nguyên liệu.
- **AI:** chatbot có lịch sử, phân tích video thành nháp có thể sửa/đăng, kiểm tra tên dị ứng/đồng nghĩa, kiểm duyệt bài. Google và Cloudinary là phụ thuộc ngoài.
- **Admin:** dashboard, tài khoản, hoạt động thành viên từ dữ liệu thật (`GET /api/admin/users/{id}/activity`), nội dung/bình luận, báo cáo, nguyên liệu, danh mục món/nguyên liệu, BMI của món, dị ứng, nhà hàng.
- **Thông báo:** SignalR và chuông khi tương tác/gỡ nội dung; tối đa 100 thông báo mỗi user trong bộ nhớ tiến trình API, mất khi restart hoặc chuyển instance.

**Chưa đồng bộ hoàn toàn với sản phẩm mong muốn:** FE vẫn có route `/explore`; route `/view-detail` và một số slug mở dữ liệu thiết kế tĩnh; bộ lọc `CommunityFeed`/`PostEditor` còn QUESTION/VIDEO; `MockIngredientRecognitionService` được đăng ký nhưng không thấy API route dùng nó. README gốc có endpoint và bảng đã lỗi thời.

**Ảnh mới:** FE chọn tệp JPEG/PNG/WebP/GIF (tối đa 8 MB), gửi `POST /api/media/images?purpose=recipes|restaurants|categories` bằng JWT; API kiểm tra chữ ký tệp, tải lên thư mục `Cloudinary:ImageFolder` và trả `imageUrl`. Chỉ admin được tải ảnh nhà hàng/danh mục. URL ảnh lưu vào Recipes, Restaurants và RecipeCategories phải là URL HTTPS Cloudinary; thumbnail video được tạo từ video Cloudinary. Cấu hình `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret` và tùy chọn `Cloudinary__ImageFolder` trên máy chạy API/Render. Ảnh tĩnh đi kèm mã FE không thuộc luồng upload này.

**Kiểm thử 29/10:** BE build và test logic đạt; FE TypeScript, lint (0 lỗi, 5 cảnh báo có sẵn) và 27 test đạt. Bộ test HTTP admin có thêm trường hợp ảnh/quyền upload nhưng chưa chạy lại được vì sandbox chặn kết nối localhost và xét duyệt mở quyền bị giới hạn sử dụng. FE build biên dịch và typecheck thành công, bước prerender bị `EPERM` khi tạo thư mục trong `.next` đang dùng. Chưa kiểm thử upload Cloudinary thực tế hay browser end-to-end. Đã cập nhật schema Aiven và xác nhận 33 bảng/163 cột sau khi thêm ảnh và dọn legacy.
