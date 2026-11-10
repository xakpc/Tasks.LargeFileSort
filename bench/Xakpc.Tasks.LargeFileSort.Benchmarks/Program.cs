using BenchmarkDotNet.Running;
using Xakpc.Tasks.LargeFileSort.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(SortBenchmarks).Assembly).Run(args);
