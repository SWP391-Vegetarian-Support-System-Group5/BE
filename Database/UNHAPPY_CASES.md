# Các tình huống cần kiểm thử từ user đến admin

| Luồng | Tình huống xấu | Kỳ vọng |
| --- | --- | --- |
| Đăng ký/đăng nhập | OTP hết hạn, email trùng, token hết hạn, user bị khóa | API báo lỗi rõ; không lộ dữ liệu; UI cho đăng nhập lại |
| Hồ sơ | Thiếu chiều cao/cân nặng, allergy hoặc diet type thay đổi | Meal plan cũ không được dùng nếu đã không còn an toàn |
| Tạo bài | Nội dung tục tĩu, tình dục, món mặn, prompt injection, AI timeout/403/429 | Không public; lưu DRAFT và lý do; sửa rồi kiểm duyệt lại |
| Tạo bài | Bấm đăng nhiều lần, request mất kết nối sau khi server đã lưu | Không tạo bài trùng; tải lại danh sách bản nháp trước khi thử lại |
| Video | File quá lớn/sai định dạng, mất file gốc, Cloudinary lỗi, transcript sai hoặc không rõ nguyên liệu | Dừng đúng bước, giữ bản nháp và lỗi để sửa; không public khi kiểm duyệt chưa đạt |
| Recipe | Nguyên liệu trùng tên nhưng nhóm khác; ingredient không chay; category ngừng hoạt động | Không tự gắn nhãn ăn chay sai; báo lỗi cần phân loại/xác nhận |
| Bình luận/đánh giá | Post DRAFT/REMOVED, bình luận cha đã ẩn, tự đánh giá bài mình | API từ chối thao tác không hợp lệ; không gửi thông báo sai |
| Thông báo | SignalR mất kết nối, nhiều tab, refresh, token hết hạn, 100+ thông báo | Lưu DB, tải lại sau reconnect; số chưa đọc lấy từ DB; không lộ thông báo user khác |
| Admin | Gỡ bài/bình luận hai lần hoặc review nhiều flag cùng nội dung | Chỉ một trạng thái gỡ và thông báo phù hợp; không công khai lại vô ý |
| Meal planner | Không có món khớp ingredient + allergy + diet + BMI; thay allergy sau khi tạo plan | Empty state rõ; không dùng món sai điều kiện |
| Nhà hàng | Geocoding không ra tọa độ, địa chỉ mơ hồ, thiếu diet type | Không gắn tọa độ giả; user chỉ thấy nhà hàng đủ điều kiện đã tạo |
| Reset dữ liệu | Chọn sai DB, thiếu backup, seed tự tạo lại dữ liệu, mất admin | Kiểm tra tên DB/backup/admin trước; chạy staging và transaction |

Kiểm thử tích hợp trên Aiven staging vẫn cần cho các luồng AI, Cloudinary, SignalR, moderation và thông báo; build và unit test không chứng minh được hoạt động của dịch vụ ngoài.
