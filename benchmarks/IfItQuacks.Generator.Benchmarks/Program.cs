using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(IfItQuacks.Generator.Benchmarks.GeneratorBenchmarks).Assembly).Run(args);
