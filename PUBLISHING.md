# 发布与维护

仓库：<https://github.com/caishaoweimeheat/airpods-tray>

## 本次测试版

`v0.2.0-beta` 使用已经在本机检查过的运行包，不在发布流程中重新编译。运行包、对应源码提交和 SHA-256 均写入 `release-manifest.json`，说明位于 `docs/releases/v0.2.0-beta.md`。

发布流程会先核对压缩包内容与校验值，再创建草稿，上传运行包和 `SHA256SUMS.txt`，检查上传结果后公开为 Pre-release。重跑时会校验既有版本，不覆盖已发布文件。

## 发布新版本

1. 修改代码和版本号，在 Windows 执行 `Build.ps1 -Test`，并完成 `TESTING.md` 中本次声明涉及的硬件测试。
2. 执行 `Package.ps1 -SkipBuild`，把测试过的运行包放进 `downloads/`，并在 `.gitignore` 中允许提交该版本的 ZIP。
3. 提交源码、运行包和验证记录，记下这个提交的完整 SHA。
4. 在 `docs/releases/` 编写该版本说明，明确功能、运行条件和未验证项目。
5. 更新 `release-manifest.json` 中的标签、标题、源码提交、运行包路径、SHA-256、说明路径和测试版标志。请使用新标签，不复用已发布标签。
6. 将清单提交到 `main`，会启动 **Publish tested release**。也可以在 GitHub 的 Actions 页面选择该流程，再点击 **Run workflow**。
7. 确认 Actions 成功、Release 中有运行包和校验文件，再更新 README 中的下载链接。

流程只使用 GitHub 为当前任务提供的令牌，权限限于仓库内容读写，无需设置个人访问令牌。清单中的仓库名称必须与执行仓库一致，分叉仓库需要先修改清单。

本地检查发布逻辑（需要 Python 3.9 或以上，应用用户无需 Python）：

```powershell
python -m unittest discover -s tests -p '*_test.py' -v
Get-FileHash .\downloads\AirPodsTray-0.2.0-beta-win-x64.zip -Algorithm SHA256
```

如果流程失败，请查看 Actions 日志。上传失败时可能保留未公开的草稿；文件一致时重跑会继续上传。文件或标签发生冲突时，流程停止，由维护者检查，不能直接覆盖旧版本。

## 手动发布备用方法

在仓库 Releases 页面创建版本，填入清单中的标签，并以 `target_commit` 指定的源码提交为基准。粘贴对应版本说明，上传运行包及校验文件，按清单设置 Pre-release 后发布。

运行包下载用于直接使用，GitHub 自动生成的 Source code 用于查看和编译源码。
