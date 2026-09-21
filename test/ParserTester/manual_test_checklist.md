# 配置导入手动测试清单

> 操作：启动 `Bpm Measurer.exe` → 先加载任意音频（如 `test\Galaxy Blaster.ogg`）→ 点击 **"导入配置"** 按钮 → 选择对应 `.txt` 文件 → 观察弹窗。

> 所有错误弹窗标题均为 `配置文件解析失败。`（中文）/ `Failed to parse config file.`（英文），正文为具体错误原因。

---

## 非法配置（应全部弹出错误）

| # | 文件 | 预期弹窗内容 |
|---|------|-------------|
| R1.1 | `InvalidConfigs\R1.1_missing_offset.txt` | 缺少 global_offset，或其值无效 |
| R1.2 | `InvalidConfigs\R1.2_duplicate_offset.txt` | global_offset 只能出现 1 次 |
| R1.3 | `InvalidConfigs\R1.3_invalid_offset_value.txt` | 缺少 global_offset，或其值无效 |
| R1.5 | `InvalidConfigs\R1.5_offset_mixed_with_segment.txt` | global_offset 行不能混入其它内容 |
| R1.6 | `InvalidConfigs\R1.6_two_offsets_one_line.txt` | global_offset 行不能混入其它内容 |
| R2 | `InvalidConfigs\R2_no_segment.txt` | 至少需要 1 个 bpm 段 |
| R3.1 | `InvalidConfigs\R3.1_missing_bpm.txt` | 第 1 段缺少 beat_index 或 bpm |
| R3.2 | `InvalidConfigs\R3.2_missing_beat_index.txt` | 第 1 段缺少 beat_index 或 bpm |
| R3.3 | `InvalidConfigs\R3.3_duplicate_beat_index.txt` | 第 1 段所在行只能有一个段落 |
| R3.4 | `InvalidConfigs\R3.4_duplicate_bpm.txt` | 第 1 段所在行只能有一个段落 |
| R3.5 | `InvalidConfigs\R3.5_segment_mixed_with_offset.txt` | global_offset 行不能混入其它内容 |
| R4.1 | `InvalidConfigs\R4.1_beat_index_float.txt` | 第 1 段的 beat_index 不是非负整数 |
| R4.2 | `InvalidConfigs\R4.2_beat_index_negative.txt` | 第 1 段的 beat_index 不是非负整数 |
| R4.3 | `InvalidConfigs\R4.3_beat_index_nan.txt` | 第 1 段的 beat_index 不是非负整数 |
| R5.1 | `InvalidConfigs\R5.1_bpm_zero_or_negative.txt` | 第 1 段的 bpm 不是非负数字 |
| R5.2 | `InvalidConfigs\R5.2_bpm_scientific_notation.txt` | 第 1 段的 bpm 不是非负数字 |
| R5.3 | `InvalidConfigs\R5.3_bpm_not_number.txt` | 第 1 段的 bpm 不是非负数字 |
| R6.1 | `InvalidConfigs\R6.1_beat_index_equal.txt` | beat_index 未从小到大排列（第 3 段） |
| R6.2 | `InvalidConfigs\R6.2_beat_index_decreasing.txt` | beat_index 未从小到大排列（第 3 段） |
| R7 | `InvalidConfigs\R7_first_beat_not_zero.txt` | 第一个 bpm 段的 beat_index 必须是 0 |

---

## 合法配置（应全部成功导入）

| # | 文件 | 验证要点 |
|---|------|---------|
| N1 | `ValidConfigs\N1_minimal.txt` | 导入成功：1 个段，offset=0，bpm=120 |
| N2 | `ValidConfigs\N2_multi_segment.txt` | 导入成功：3 个段，offset=0.5 |
| N3 | `ValidConfigs\N3_old_format_no_beats.txt` | 旧格式（无 beats_per_bar）→ 所有段拍号默认 4 |
| N4 | `ValidConfigs\N4_beats_per_bar_bounds.txt` | 段1=每小节1拍，段2=每小节20拍（边界均正确） |
| N5 | `ValidConfigs\N5_beats_per_bar_oob_clamp.txt` | beats=0 → clamp 到 1；beats=99 → clamp 到 20 |
| N6 | `ValidConfigs\N6_with_comments_and_blank_lines.txt` | 注释和空行正确跳过，2 个段导入 |
| N7 | `ValidConfigs\N7_bpm_thousand_sep.txt` | bpm=1,000 解析为 1000（千位分隔符正确） |
| N8 | `ValidConfigs\N8_offset_thousand_sep.txt` | offset=1,000.5 解析为 1000.5 |
| N9 | `ValidConfigs\N9_bpm_low_clamp.txt` | bpm=5 → clamp 到 10 |
| N10 | `ValidConfigs\N10_bpm_high_clamp.txt` | bpm=2000 → clamp 到 1000 |
| N12 | `ValidConfigs\N12_negative_offset.txt` | 导入成功：offset=-1.5（负值正确接受），1 个段 bpm=120 |

---

## 拖放场景手动测试

> 归类规则（音频 / 配置的挑选）由 `DroppedFiles.Resolve` 承担，已由 `TestDroppedFiles` 覆盖；
> 下表只验证 UI 行为（弹窗、时序、状态）。

| # | 操作 | 预期 |
|---|------|------|
| P1 | 未加载音频时，仅拖入 1 个 `.txt` | 无弹窗、无状态变化（静默忽略） |
| P2 | 未加载音频时，同时拖入音频 + 同名 `.txt` | 音频加载完成后，offset/BPM 已按 txt 生效 |
| P3 | 已加载音频时，同时拖入 2 个音频 + 2 个 `.txt` | 加载拖入顺序中的第一个音频，并套用同主名的 txt（无同主名则用第一个 txt） |
| P4 | 已有非默认配置（或先手动改过 BPM）→ 点击「导入配置」选合法 txt | 弹「已有配置」Yes/No 询问 |
| P5 | P4 选「是」→ 在保存对话框点「取消」或直接关闭 | 提示流程中断：**不导入**，当前配置保持原样 |
| P6 | P4 选「是」→ 正常保存 | 导出的文件内容正确，随后导入新配置 |
| P7 | P4 选「否」 | 直接导入新配置，当前配置被丢弃 |
| P8 | 已有配置时拖入/导入**非法** txt | 只弹解析错误，**不**询问、**不**触发导出 |
| P9 | 拖入 `.TXT` 大写扩展名 | 与 `.txt` 同等对待 |
| P10 | 拖入文件夹、`.png` 等非音视频文件 | 不加载（`.png` 会被当作音频候选交给 BASS，解码失败弹 `LoadError`；随行的 txt 不会被套用） |

---

## 运行自动化测试

```powershell
dotnet run --project test\ParserTester\ParserTester.csproj -c Debug
```

期望输出：`通过: 42  失败: 0`
