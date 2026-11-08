# 首次发布到 GitHub

建议仓库名：`airpods-tray`。

1. 在 GitHub 新建仓库。上传此源码目录的内容，使 `README.md` 直接位于仓库根目录。
2. 如果先在本地使用 Git，执行 `git init`，再提交源码并连接你自己的远程仓库。`.gitignore` 已排除配置、日志和编译文件。
3. 在 Windows 运行 `Build.ps1 -Test`。完成 `TESTING.md` 中与你的发布声明有关的手动检查。
4. 运行 `Package.ps1 -SkipBuild`，得到 `dist/AirPodsTray-0.2.0-beta-win-x64.zip`。
5. 在 GitHub Releases 创建标签 `v0.2.0-beta`，标记为 Pre-release，上传该运行包。
6. 发布说明列出本次设备选择功能、测试环境和仍未完成的测试项目。

本源码包已包含 MIT 许可证。发布前确认你希望按该许可证分享代码。

当前仓库提供 `downloads/AirPodsTray-0.2.0-beta-win-x64.zip` 直接下载。正式创建 Release 后，可更新 README 的下载入口。
