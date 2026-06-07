SX3 SCANER - Optimized ViewModel source V2

Cách dùng:
1. Sao lưu project hiện tại.
2. Chép các file .cs trong gói này đè vào thư mục ViewModel của project.
3. Build lại bằng Visual Studio.
4. Test các ca: scan PASS, sai độ dài, sai prefix, sai PartName, sai SealNo, trùng LotNo, đủ số lượng đóng box.

Các thay đổi chính:
- Sửa lỗi Replace() làm cắt sai chuỗi QR; đổi sang Substring theo đúng vị trí.
- Sửa ScanWorker/BoxWorker không còn lưu cứng dấu '?', lấy theo Worker nhập trên màn hình.
- Sửa lỗi thông báo SealNo bị nhầm sang lỗi tên sản phẩm.
- Sửa xử lý Box: dùng đúng _CurrentBoxName, không gọi GetNextBoxName() lần hai khi tạo box.
- Chỉ cập nhật box khi scan PASS, tránh scan NG làm ảnh hưởng tiến độ hộp.
- Sửa kiểm tra LotNo thiếu ký tự để tránh crash Substring(0,4).
- Tối ưu lọc không phân biệt hoa thường, tránh ToLower lặp lại.
- Sửa RelayCommand không nuốt lỗi rồi trả về true.
- Thêm reset trạng thái scan rõ ràng hơn.

Lưu ý:
- Đây là bộ ViewModel đã tối ưu từ các file anh gửi, chưa có đầy đủ Model/Repository/XAML nên em không build trực tiếp được trong môi trường này.
- Nếu project có code-behind/XAML bind vào tên cũ CheckLenght thì không ảnh hưởng vì hàm này đang private; đã đổi thành CheckLength trong nội bộ file.
