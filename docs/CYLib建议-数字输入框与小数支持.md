# CYLib 改进建议：数字输入框与小数支持

> 来源：愈深愈肉（Deeper and Tankier）接入 CYLib 配置时的用户反馈与需求。
> 本文档面向 **CYLib 的开发会话**，按清单实施即可；DAT 侧随后跟进对接。
> 目标发版号：由用户统一改号（实际以 **v0.1.3** 发布，本文档不代改版本字段）。

## 背景（用户反馈原话）

1. 「设置系数的框太长了」——数字输入框宽度 320px，对纯数字过宽；
2. 「系数的数字太小了」——`LineEdit` 未设字号，继承主题默认值偏小；
3. 「系数上限可以调至最高 99999」——现 `IntInput` 上限由调用方传参，DAT 侧放宽即可，**无需改 CYLib**；
4. 「系数目前是否可以设置为小数？如 1.5？如果不可能改为可以」——CYLib 目前只有 `IntInput`（整数）
   与 `FloatSlider`（滑条），**没有小数输入框**。

对应现状（v0.1.0/更新中的代码）：

| 位置 | 现状 |
| --- | --- |
| `Code/Ui/CyStyle.cs` → `CreateRow(..., float valueWidth = 320f)` | 输入行统一 320px 宽 |
| `Code/Ui/CyStyle.cs` → `ApplyFont(control, fontName, fontType)` | 只换字体（CJK 替换），不设字号 |
| `Code/Ui/SettingRowFactory.cs` → `CreateIntInput` | 整数输入框（解析 + 夹取 + 红框回退），形态良好 |
| `Code/Config/ConfigModel.cs` → `FloatSetting` | 无 `Style` 形态字段，只有滑条 |
| `Code/Config/ConfigPageBuilder.cs` | 无 `FloatInput` 构建方法 |

---

## 建议 1：输入框收窄 + 字号调大

**文件**：`Code/Ui/CyStyle.cs`、`Code/Ui/SettingRowFactory.cs`

`ApplyFont` 增加可选字号参数：

```csharp
public static void ApplyFont(Control control, StringName themeFontName,
    FontType fontType = FontType.Regular, int? fontSize = null)
{
    control.ApplyLocaleFontSubstitution(fontType, themeFontName);
    if (fontSize is int size)
        control.AddThemeFontSizeOverride("font_size", size);
}
```

各输入行创建处（`CreateIntInput` / `CreateText` / 新增的 `CreateFloatInput`）：

```csharp
CyStyle.ApplyFont(edit, "font", FontType.Regular, CyStyle.FontSizeLabel);  // 字号 20
// ...
Root = CyStyle.CreateRow(def.Label.Resolve(), def.Description.Resolve(), edit, valueWidth: 160f);
```

- 数字输入框建议 `valueWidth: 160f`；文本输入框如需保持宽敞可用 `220f`；
- 可选的更通用做法：给 `SettingDef` 加 `ControlWidth` / `FontSize` 属性透传到行渲染——不必须，统一调好即可。

## 建议 2：`FloatInput` 小数输入框（镜像 `IntInput`，共 3 处小改）

### ① `Code/Config/ConfigModel.cs` — `FloatSetting` 加形态开关

```csharp
public sealed class FloatSetting : SettingDef
{
    // …… 现有字段 ……

    /// <summary>UI 形态：滑条（默认）或数字输入框。</summary>
    public NumberStyle Style { get; set; } = NumberStyle.Slider;

    // ……
}
```

### ② `Code/Config/ConfigPageBuilder.cs` — 新增构建方法

```csharp
/// <summary>小数数字输入框（直接填小数；提交时解析，越界按 Min/Max 夹取，解析失败红框提示并回退）。</summary>
public ConfigSectionBuilder FloatInput(
    string id, LazyText label, double min, double max, double defaultValue,
    Action<double>? onChanged = null, LazyText description = default)
{
    Add(new FloatSetting(id, label, defaultValue)
    {
        Min = min,
        Max = max,
        Style = NumberStyle.Input,
        OnChanged = onChanged,
        Description = description,
    });
    return this;
}
```

### ③ `Code/Ui/SettingRowFactory.cs` — 路由 + `CreateFloatInput`

```csharp
// Create 的 switch 里：
FloatSetting f => f.Style == NumberStyle.Input ? CreateFloatInput(store, page, f) : CreateFloat(store, page, f),
```

`CreateFloatInput` 完全镜像 `CreateIntInput`，仅解析与回显不同：

```csharp
private static SettingRow CreateFloatInput(ConfigStore store, ConfigPage page, FloatSetting def)
{
    var edit = new LineEdit { FocusMode = Control.FocusModeEnum.All };
    CyStyle.ApplyFont(edit, "font", FontType.Regular, CyStyle.FontSizeLabel);
    edit.TextChanged += _ => CyStyle.ShowInvalidEdit(edit, false);

    void Commit(string text)
    {
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            CyStyle.ShowInvalidEdit(edit, false);
            var clamped = Math.Clamp(value, def.Min, def.Max);
            store.Set(page.ModId, def.Id, clamped, ConfigChangeSource.Ui);
            edit.Text = clamped.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            CyStyle.ShowInvalidEdit(edit, true);
            edit.Text = store.GetFloat(page.ModId, def.Id, def.DefaultValueRaw)
                .ToString(CultureInfo.InvariantCulture);
        }
    }

    edit.TextSubmitted += Commit;
    edit.FocusExited += () => Commit(edit.Text);

    void Refresh()
    {
        edit.Text = store.GetFloat(page.ModId, def.Id, def.DefaultValueRaw)
            .ToString(CultureInfo.InvariantCulture);
    }

    Refresh();
    return new SettingRow
    {
        Root = CyStyle.CreateRow(def.Label.Resolve(), def.Description.Resolve(), edit, valueWidth: 160f),
        Refresh = Refresh,
    };
}
```

存储层**无需改动**：`ConfigStore` 的 `Normalize` / `FromJsonNode` 已把 JSON 数字还原为
`int` / `double`，`FloatSetting` 的持久化与「重置为默认」天然可用。

## 建议 3（可选）：`CreateText` 校验失败也给红框

`CreateText` 的 validator 拒绝输入时目前是静默回退；建议与 `CreateIntInput` 一致，
拒绝时调 `CyStyle.ShowInvalidEdit(edit, true)`（重新输入即消除）。

---

## 验收清单（供 CYLib 会话自查）

- [ ] `FloatInput("x", "标签", 0, 99999, 1)` 在配置页渲染为可填小数的输入框；
- [ ] 填 `1.5` 提交 → 值保持 1.5；重启后仍是 1.5；
- [ ] 填 `abc` → 红框 + 回退旧值；填 `100000`（超上限）→ 夹取为 99999 并回显；
- [ ] 数字输入框宽度约 160px、框内数字为 `FontSizeLabel`（20）；
- [ ] 「重置为默认」把小数值还原为默认值；
- [ ] `pack-release.ps1` 版本核对通过（版本字段由用户统一改 **0.1.2**）。

## CYLib 更新后 DAT 侧将跟进（届时再做）

1. 成长系数改用 `FloatInput`：min 0、**max 99999**、默认 1，支持小数（如 1.5 = 0.0015）；
2. 内部系数链路改十进制（‰ ÷ 1000），配置页预览行自动适配（1.5‰ → 16 层 24%、48 层 72%）；
3. 联机同步消息改带十进制文本（各端逐位一致）；
4. DAT 清单 `dependencies` 写 `{"id": "CYLib", "min_version": "v0.1.3"}`；
5. DAT 自测补小数用例。
