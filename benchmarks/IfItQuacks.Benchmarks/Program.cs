using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(IfItQuacks.Benchmarks.CallBenchmarks).Assembly).Run(args);
