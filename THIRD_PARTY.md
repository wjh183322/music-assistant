# 格式实现参考与许可证

本应用的本地文件格式实现参考以下开源实现，并在 `licenses` 保留适用许可证。实现移植至 C#，增加块长度边界、取消、安全暂存、源文件保留、音频一致性校验、歌曲 ID 验证和 Windows 界面。

| 参考 | 使用范围 | 许可证 |
|---|---|---|
| [pyNCMDUMP](https://github.com/allenfrostline/pyNCMDUMP) | NCM 头、AES 元信息与载荷恢复 | MIT |
| [bczhc/qmc-dec](https://github.com/bczhc/qmc-dec) | QMC 静态表与 QMC2 流算法 | MIT / Apache-2.0，采用 MIT 条款 |
| [qmc2-rust 的保留源码](https://github.com/bczhc/qmc-dec/tree/master/third_party/qmc2-rust) | Map、RC4、EncV2 及公开测试向量 | MIT / Apache-2.0，采用 MIT 条款 |
| [tc_tea 0.1.4](https://crates.io/crates/tc_tea/0.1.4) | Tencent TEA CBC 模式、固定测试向量 | MIT / Apache-2.0，采用 MIT 条款 |
| [QM Unlock](https://github.com/mary20050520/qmunlock) | musicex V1 尾部、主动调用的 QQ 会话读取与单文件密钥服务 | MIT |

QQ 公开歌单读取使用 QQ 官方域名公开元数据服务，不发送账号信息、不调用音频下载服务。musicex 密钥获取是单独功能：仅在用户点击并确认本批文件后运行，只请求当前歌单内已下载文件的密钥。

FFmpeg 9.0.2 独立进程的 GPLv3 许可证和构建信息随包位于 `tools`。

注意：上游项目的格式支持/实测声明是实现参考，不等于本应用已在用户所有客户端版本上验证。应用自身的自动化测试记录保存在开发目录 `.test-output/latest-test-results.txt`，包含已知解密向量、合成音频容器、音频一致性及真实公开歌单读取。真实 QQ 登录状态没有在开发测试中读取。
