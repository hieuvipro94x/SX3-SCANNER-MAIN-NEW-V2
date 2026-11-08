# Rà soát giao diện và gỡ cập nhật trực tuyến

Ngày: 08/11/2026. Nền code: `43a1551` (v10.47.0). Màn hình mục tiêu theo yêu cầu: **1980 × 1080**.

## Đã gỡ

- Dịch vụ nhận thông báo HTTP/WebSocket, cache và animation thông báo.
- Kiểm tra cập nhật khi mở app, polling, cập nhật bắt buộc, tải/xác thực/chạy installer và cửa sổ release notes.
- Gửi trạng thái version lên thư mục server và công cụ SX3VersionMonitor.
- Nút cập nhật, badge kết nối server, cấu hình endpoint, update.json và cơ chế sinh/upload manifest cập nhật trong script release.
- Dependency Markdig và System.Net.Http không còn được sử dụng.
- Phiên bản ứng dụng vẫn hiển thị bằng metadata assembly, không truy cập mạng.

Backup thư mục mạng/NAS là sao lưu dữ liệu, vẫn được giữ. Script build/phát hành installer thủ công trên GitHub vẫn được giữ.

## Các màn hình đã rà soát

| Màn hình | Cách kiểm tra | Kết quả / điều chỉnh |
|---|---|---|
| Khung chính | Chạy bản sửa và đọc XAML | Gỡ banner/badge server và cập nhật, giữ điều hướng và version; bổ sung viền focus bàn phím; status không đổi cỡ chữ, màu chấm khớp thông báo |
| Quét sản xuất | Xem trực tiếp bằng dữ liệu riêng | Giữ cột trái 300 và cột kết quả 370 cho màn hình mục tiêu, khoảng còn lại dành cho ô scan và bảng tem; bảng tem có cuộn ngang và tooltip; bỏ badge lặp làm cắt tiêu đề |
| Tiến độ thùng | Xem trực tiếp và kiểm tra template | 0% không tô đầy khi Maximum bằng 0; bỏ animation vô hạn; dùng PART_Track / PART_Indicator chuẩn WPF |
| Quản lý mã hàng | Xem control độc lập, đọc XAML/binding | Giữ form rộng 450 ở màn hình mục tiêu, bỏ MinWidth 650 ép bảng tràn; giữ cuộn form/bảng; thêm trạng thái rỗng; mô tả hỗ trợ QR và DataMatrix |
| Lịch sử / Excel | Xem trực tiếp control độc lập, đọc XAML | Bộ lọc và nút tìm/xuất tách rõ, có trạng thái rỗng, bảng bật virtualization; giữ nguyên luồng tra cứu/xuất |
| Bảo trì dữ liệu | Xem trực tiếp control độc lập, đọc XAML | Có cuộn dọc, backup/restore phân khu; giữ nguyên chức năng dữ liệu |
| Đổi phiên quét, xác nhận đóng lẻ/hủy thùng, thông báo NG, BOX hôm nay | Đọc XAML và code-behind | Kiểm tra kích thước, nội dung và nút xác nhận/hủy; chưa chạy các thao tác ghi/xóa/restore |
| Hộp mật khẩu, startup, ProfessionalMessageBox | Đọc XAML/code | Chưa thay đổi cơ chế xác thực; xem các điểm còn cần tối ưu bên dưới |

## Những điểm còn cần tối ưu

1. **Ưu tiên cao: backup/restore.** Tắt local có thể làm backup trước restore bị bỏ qua mà trả thành công; backup chỉ qua server thất bại vẫn tính thành công; ping đang quyết định có thử ghi thư mục mạng hay không. Các lỗi này thuộc luồng dữ liệu, chưa sửa trong đợt gỡ updater và chỉnh giao diện.
2. **Hộp mật khẩu:** hiện có Enter/Escape nhưng thiếu nút Hủy nhìn thấy được và hướng dẫn thao tác. Chưa thay đổi hộp xác thực.
3. **Bảng BOX hôm nay:** nhiều tiêu đề còn là tên property tiếng Anh, có cả ScanResult và ResultText cùng biểu diễn kết quả. Nên đổi tiêu đề và thu gọn cột sau khi thống nhất thông tin vận hành cần giữ.
4. **Form mã hàng:** cần thống nhất các nhãn tiếng Anh/tiếng Việt (CAR, SAMPLE, LENGTH, QTY) trong một đợt chỉnh nội dung.
5. **Khả năng truy cập:** một số template nút riêng còn tắt FocusVisualStyle. Đã bổ sung focus cho điều hướng chính; nên rà chuỗi Tab trên từng dialog khi làm đợt accessibility riêng.

## Kết quả build và kiểm tra

- Build Debug x64 và Release x64 thành công.
- 14/14 test còn lại đạt; hai test updater được gỡ cùng tính năng.
- Kiểm tra trực quan cả bốn control chính ở 1980 × 1080 theo yêu cầu cuối cùng, gồm một dòng tem mẫu và tiến độ 50%; khung chính cũng được xem ở trạng thái maximized. Các kích thước nhỏ hơn chỉ được khảo sát trước khi chốt màn hình mục tiêu.
- Script release được kiểm tra cú pháp PowerShell; không chạy publish hoặc installer.

## Giới hạn kiểm chứng

Preview dùng source sửa trong thư mục tạm, mutex và database riêng; không dùng thao tác quét/restore/xóa trên dữ liệu vận hành. Control quản lý mã hàng được xem độc lập để kiểm tra bố cục, không tự động nhập mật khẩu. Không kiểm chứng máy quét vật lý, mạng NAS thực hoặc toàn bộ mức DPI. Việc build thành công không thay thế kiểm tra vận hành thực tế.
