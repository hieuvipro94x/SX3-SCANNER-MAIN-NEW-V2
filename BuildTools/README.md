# Build bộ cài và phát hành GitHub

Chạy `Build-Installer-GitHub.cmd` ở thư mục gốc. File dùng đường dẫn tương đối nên có thể di chuyển checkout.

## Cách chạy

```cmd
Build-Installer-GitHub.cmd
Build-Installer-GitHub.cmd -Version 10.50.0
Build-Installer-GitHub.cmd -Version 10.50.0 -Draft
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

Script kiểm tra tên/email Git, đăng nhập GitHub và origin trước khi thay đổi phiên bản. Nếu thiếu tên/email, cấu hình trong project:

```cmd
git config --local user.name "Ten cua ban"
git config --local user.email "Email cua ban"
```

Trước khi chọn version và build, script fetch và rebase nhánh hiện tại lên nhánh tương ứng trên origin. Thay đổi chưa commit (kể cả file mới và trạng thái staged) được tạm lưu rồi khôi phục. Nếu commit xung đột, script hủy rebase và khôi phục thay đổi local; nếu khôi phục stash xung đột, bản sao stash được giữ lại và script dừng để xử lý. Không force push. Nhánh mới chưa có trên origin sẽ được tạo khi push.

Nếu origin có thêm commit trong lúc build, script giữ commit/bộ cài và yêu cầu chạy lại để build đúng source mới, không rebase rồi phát hành bộ cài từ source cũ.

Tự tìm MSBuild qua Visual Studio Installer, kể cả Visual Studio cài ở ổ D. Với Inno Setup ở vị trí khác, dùng `-InnoSetupPath "D:\Tools\Inno Setup 6\ISCC.exe"`.

Kiểm tra riêng luồng Git (dùng repository tạm, không đẩy lên GitHub):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\BuildWorkflow.Tests.ps1
```

Nếu bước GitHub thất bại sau khi đã push tag, kiểm tra tag/release hiện có và hoàn tất upload bằng GitHub CLI; không xóa hoặc ghi đè tag tự động. Script dừng ngay khi một bước lỗi và trả exit code khác 0.

Khi chạy không tương tác, đặt `SX3_NO_PAUSE=1` để không dừng tại lệnh pause cuối file CMD.
