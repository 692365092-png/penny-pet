# 交互路径微基准

从仓库根目录执行。需 .NET 8 SDK；不需要 Windows，也不测 native/UI/磁盘性能。

```powershell
dotnet build docs/architecture-review/benchmark/AuditBenchmark.csproj -c Release -o benchmark-after
dotnet benchmark-after/PennyPet.SelfTests.dll benchmark-after.json

git worktree add --detach ../penny-baseline d8b682c0c55552312d426e269337cfa1bab088bf
$pennyBaseline = (Resolve-Path ../penny-baseline).Path
dotnet build docs/architecture-review/benchmark/AuditBenchmark.csproj -c Release "-p:PennySource=$pennyBaseline" -p:DefineConstants=BASELINE -o benchmark-before
dotnet benchmark-before/PennyPet.SelfTests.dll benchmark-before.json
```

`PennySource` 使用绝对路径，避免项目目录和当前 shell 目录解析不同。输出目录是临时测量产物，请不要提交 EXE/DLL。程序输出 3 次预热、7 个原始样本及中位数；分配量含测量器本身的小额分配。side-tabs 测量的是无相交时的完整扫描，基线实现精确复制旧列表排序算法。代码还直接调用生产 codec，测量两条字段合法的大便签是否超出读文件预算。

运行结果见上级 `verification.json`。不同机器的耗时会变，算法消除的复制/排序与分配量更容易复核。不要把这些数字解释为整机帧率或 UI 延迟改善。
