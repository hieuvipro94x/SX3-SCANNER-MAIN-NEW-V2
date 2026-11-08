# Build bộ cài và phát hành GitHub

Chạy `Build-Installer-GitHub.cmd` ở thư mục gốc. File dùng đường dẫn tương đối nên có thể di chuyển checkout.

## Cách chạy

```cmd
Build-Installer-GitHub.cmd
Build-Installer-GitHub.cmd -Version 10.48.0
Build-Installer-GitHub.cmd -Version 10.48.0 -Draft
Build-Installer-GitHub.cmd -BuildOnly
```

- Mặc định: tăng version theo nhánh minor của repo (10.47.0 → 10.48.0), build Release x64, chạy test, đóng gói Inno Setup, commit **toàn bộ thay đổi source trong checkout**, push nhánh hiện tại lên origin, tạo tag và GitHub Release công khai kèm bộ cài.
- `-Version`: chỉ định version mới, phải lớn hơn version đang có trong AssemblyInfo khi phát hành. Không ghi đè release/tag đã tồn tại.
- `-Draft`: tải assets lên một Draft Release để xem trước.
- `-BuildOnly`: build/test/đóng gói version hiện tại; không cần đăng nhập GitHub, không commit, không push, không tạo tag/release. Nếu truyền thêm `-Version`, AssemblyInfo cũng được cập nhật theo version đó.
- `BuildTools/Buildapp.bat` gọi cùng quy trình để tương thích lối chạy cũ.

## Đầu ra

`InstallerOutput/SX3ScannerSetup-<version>.exe`, `release-note.txt` và `release-manifest.txt` (SHA256).

Script không chạy installer trên máy hiện tại. File `BuildTools/release-note.txt` là nội dung bản phát hành; sửa nội dung trước khi chạy.

## Công cụ cần có

Visual Studio/Build Tools với .NET Framework 4.8 và WPF, Inno Setup 6.3 trở lên, Git; cần thêm GitHub CLI và `gh auth login` khi phát hành. Script khôi phục NuGet qua solution trước khi build.

Nhánh hiện tại phải có nhánh tương ứng trên origin và chứa code mới nhất từ nhánh đó. Script kiểm tra origin đúng repository trước khi push.

Nếu bước GitHub thất bại sau khi đã push tag, kiểm tra tag/release hiện có và hoàn tất upload bằng GitHub CLI; không xóa hoặc ghi đè tag tự động. Script dừng ngay khi một bước lỗi và trả exit code khác 0.

Khi chạy không tương tác, đặt `SX3_NO_PAUSE=1` để không dừng tại lệnh pause cuối file CMD.
