# cultist 网站

`overview/` 部署到 `yycoding.cc/cultist/`，`log/` 部署到 `cultist.yycoding.cc/`。两页都是静态 HTML、CSS 和少量筛选脚本。

每次更新在 `updates.json` 新增一条记录，日期与时间按北京时间填写。类别用 `release`、`fix`、`work` 或 `test`。版本可填写 `version`，补充说明可填写 `items` 字符串数组。构建会按日期和时间倒序排列。

```powershell
# 在 cultist 仓库根目录执行，用临时目录保存静态产物
python website/build.py (Join-Path $env:TEMP 'cultist-site')
```

将构建出的两个目录同步至相应站点的文档根目录即可。封面由内置 ImageGen 生成，主题为夜雾中的边缘科研殖民地和透出金光的古老石门。
