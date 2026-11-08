# Kiểm tra vùng hiển thị màn SCAN chính

Ngày: 08/11/2026. Nền: commit 37e9213, v10.48.0. Màn hình mục tiêu: 1980 × 1080.

## Đã xác nhận trực quan trước khi sửa

1. Bảng tem dùng dòng cố định 38 px và dấu … nên cắt mã dài/thông báo NG; tooltip có nội dung đầy đủ nhưng không đọc hết trực tiếp trong bảng.
2. Ô nhập mã dùng một dòng, cao 72 px; mã dài chỉ hiển thị một phần.
3. Khung kết quả bên phải xếp tất cả hàng Auto. Thông báo NG nhiều dòng vượt chiều cao card, làm phần Hướng xử lý ở cuối biến mất mà không có thanh cuộn.
4. Khi đang xem các dòng lịch sử cũ, control chưa có cơ chế đưa tem mới về vùng nhìn thấy.

## Điều chỉnh

- Mã scan và thông báo trong bảng xuống dòng, tự giãn chiều cao; giữ hàng tối thiểu 38 px và virtualization/recycling. Bật cuộn theo pixel để đọc được cả các hàng cao.
- Ô scanner xuống dòng để xem mã dài; cao 72–140 px, có cuộn khi vượt giới hạn. AcceptsReturn=False và giữ KeyBinding Enter để xử lý tem như trước.
- Mã tem yêu cầu có giới hạn chiều cao và cuộn, tránh chiếm hết vùng lịch sử nếu cấu hình chứa chuỗi quá dài.
- Chi tiết NG nằm trong hàng * được giới hạn bởi card; có cuộn riêng, giữ PASS/NG và biểu tượng luôn nhìn thấy.
- Khi thêm tem mới ở đầu danh sách, bảng tự ScrollIntoView dòng mới nhất; không đổi focus bàn phím. Đăng ký/hủy event theo Loaded/Unloaded.
- Tooltip mã hàng hoạt động cả khi ComboBox bị khóa trong phiên quét.

## Các tình huống đã chạy thử

- Dùng MainWindow thật từ source, kích thước 1980 × 1080, trong bản build preview với database và mutex riêng; không ghi dữ liệu vận hành.
- Chuỗi dài 134 ký tự, tên sản phẩm dài, thông báo NG nhiều đoạn và 80 dòng lịch sử: xác nhận mã/chi tiết trong bảng xuống dòng; cuộn khung NG đến dòng cuối của Hướng xử lý đọc được.
- PASS với mã thông thường: bố cục, trạng thái và bảng hiển thị đầy đủ.
- Cuộn bảng đến dòng 80, thêm tem mô phỏng: bảng quay về tem mới ở dòng 1.

Đây là kiểm tra hiển thị bằng dữ liệu mô phỏng; chưa kiểm thử đầu đọc vật lý hoặc thay đổi DPI. Không chỉnh thuật toán xác thực tem hoặc định dạng lưu database.
